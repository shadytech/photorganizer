using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MediaSorter.Core;

/// <summary>Which date decides the Year\Month folder.</summary>
public enum DateMode
{
    /// <summary>When the photo/video was taken (metadata → filename → modified date).</summary>
    DateTaken,
    /// <summary>The file's last-modified date only; metadata is not read.</summary>
    Modified,
}

public sealed record OrganizeOptions(
    string SourceFolder,
    string DestinationFolder,
    bool IncludeSubfolders = true,
    bool CopyInsteadOfMove = false,
    bool UseFileDateFallback = true,
    bool DeleteEmptySourceFolders = false,
    bool DownloadOnlineOnlyFiles = false,
    DateMode Mode = DateMode.DateTaken);

public sealed record RunSummary(int Moved, int Copied, int Duplicates, int Failed, string LogPath);

public static class Organizer
{
    public const string UnknownFolderName = "Unknown Date";

    public static readonly HashSet<string> PhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".heic", ".heif", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp",
        ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2", ".raf", ".srw", ".pef",
    };

    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".3gp", ".3g2", ".avi", ".mts", ".m2ts", ".mkv", ".wmv",
    };

    public static bool IsMedia(string path)
    {
        // "._name.jpg" files are macOS metadata sidecars, not images.
        if (Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal)) return false;
        var ext = Path.GetExtension(path);
        return PhotoExtensions.Contains(ext) || VideoExtensions.Contains(ext);
    }

    // Cloud placeholder attributes (OneDrive "online-only"). Reading such a file triggers a download,
    // or fails outright if the sync app isn't running. Checking attributes does neither.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    public static bool IsOnlineOnly(string path)
    {
        try
        {
            var a = File.GetAttributes(path);
            return (a & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>Destination folder for a date, e.g. D:\Sorted\2024\03 - March.</summary>
    public static string FolderFor(string destinationRoot, DateTime? date)
    {
        if (date is not { } d) return Path.Combine(destinationRoot, UnknownFolderName);
        var month = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(d.Month);
        return Path.Combine(destinationRoot, d.Year.ToString("0000"), $"{d.Month:00} - {month}");
    }

    /// <summary>Finds media files and reads their dates. Does not touch any files.</summary>
    public static List<MediaItem> BuildPlan(OrganizeOptions opt, IProgress<(int done, int total)>? progress, CancellationToken ct)
    {
        var src = Path.GetFullPath(opt.SourceFolder);
        var dest = Path.GetFullPath(opt.DestinationFolder);
        var destPrefix = dest.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        var enumOpts = new EnumerationOptions
        {
            RecurseSubdirectories = opt.IncludeSubfolders,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.Hidden,
        };

        var files = System.IO.Directory.EnumerateFiles(src, "*", enumOpts)
            .Where(IsMedia)
            // If the destination sits inside the source, don't re-sort what's already sorted.
            .Where(f => !f.StartsWith(destPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var results = new ConcurrentBag<MediaItem>();
        int done = 0;
        progress?.Report((0, files.Count));

        Parallel.ForEach(files, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
        {
            // Length and attributes come from the directory entry, so neither downloads a cloud file.
            long size = 0;
            try { size = new FileInfo(file).Length; } catch (IOException) { } catch (UnauthorizedAccessException) { }

            if (!opt.DownloadOnlineOnlyFiles && IsOnlineOnly(file))
            {
                results.Add(new MediaItem
                {
                    SourcePath = file,
                    SizeBytes = size,
                    Detected = new DetectedDate(null, DateSource.OnlineOnly, "Online-only cloud file"),
                    PlannedPath = "",
                    Status = ItemStatus.Skipped,
                    Message = "online-only in OneDrive, not on this PC",
                });
            }
            else
            {
                var detected = opt.Mode == DateMode.Modified
                    ? DateDetector.FromModifiedDate(file)
                    : DateDetector.Detect(file, opt.UseFileDateFallback);
                results.Add(new MediaItem
                {
                    SourcePath = file,
                    SizeBytes = size,
                    Detected = detected,
                    PlannedPath = Path.Combine(FolderFor(dest, detected.Date), Path.GetFileName(file)),
                });
            }

            var n = Interlocked.Increment(ref done);
            if (n % 10 == 0 || n == files.Count) progress?.Report((n, files.Count));
        });

        return results
            .OrderBy(i => i.Detected.Date ?? DateTime.MaxValue)
            .ThenBy(i => i.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Moves (or copies) every item to its planned folder and writes a CSV log to the destination.</summary>
    public static RunSummary Execute(IReadOnlyList<MediaItem> items, OrganizeOptions opt, IProgress<(int done, int total)>? progress, CancellationToken ct)
    {
        var dest = Path.GetFullPath(opt.DestinationFolder);
        System.IO.Directory.CreateDirectory(dest);
        var logPath = Path.Combine(dest, $"MediaSorter-log-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

        int moved = 0, copied = 0, dupes = 0, failed = 0;
        using (var log = new StreamWriter(logPath, false, new UTF8Encoding(true)))
        {
            log.WriteLine("Action,Original,New,Date,DateSource");
            for (int i = 0; i < items.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                var item = items[i];
                if (item.Status != ItemStatus.Pending) { progress?.Report((i + 1, items.Count)); continue; }

                try
                {
                    var target = ResolveTarget(item.SourcePath, item.PlannedPath, out var isDuplicate);
                    if (isDuplicate)
                    {
                        item.Status = ItemStatus.Duplicate;
                        item.Message = $"identical file already at {target}";
                        item.FinalPath = target;
                        dupes++;
                        log.WriteLine(Csv("Duplicate", item.SourcePath, target, item.DateText, item.SourceText));
                    }
                    else
                    {
                        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        if (opt.CopyInsteadOfMove)
                        {
                            File.Copy(item.SourcePath, target);
                            File.SetLastWriteTime(target, File.GetLastWriteTime(item.SourcePath));
                            item.Status = ItemStatus.Copied; copied++;
                        }
                        else
                        {
                            File.Move(item.SourcePath, target);
                            item.Status = ItemStatus.Moved; moved++;
                        }
                        item.FinalPath = target;
                        if (!string.Equals(target, item.PlannedPath, StringComparison.OrdinalIgnoreCase))
                            item.Message = "renamed to avoid name clash";
                        log.WriteLine(Csv(opt.CopyInsteadOfMove ? "Copied" : "Moved", item.SourcePath, target, item.DateText, item.SourceText));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    item.Status = ItemStatus.Failed;
                    item.Message = ex.Message;
                    failed++;
                    log.WriteLine(Csv("Failed", item.SourcePath, "", item.DateText, ex.Message));
                }
                log.Flush();
                progress?.Report((i + 1, items.Count));
            }
        }

        if (!opt.CopyInsteadOfMove && opt.DeleteEmptySourceFolders)
            DeleteEmptyFolders(Path.GetFullPath(opt.SourceFolder));

        return new RunSummary(moved, copied, dupes, failed, logPath);
    }

    /// <summary>
    /// Picks a free path in the target folder. If a file with the same name and identical contents
    /// already exists there, reports a duplicate instead.
    /// </summary>
    internal static string ResolveTarget(string source, string planned, out bool isDuplicate)
    {
        isDuplicate = false;
        if (!File.Exists(planned)) return planned;
        if (SameContents(source, planned)) { isDuplicate = true; return planned; }

        var dir = Path.GetDirectoryName(planned)!;
        var name = Path.GetFileNameWithoutExtension(planned);
        var ext = Path.GetExtension(planned);
        for (int n = 2; ; n++)
        {
            var candidate = Path.Combine(dir, $"{name} ({n}){ext}");
            if (!File.Exists(candidate)) return candidate;
            if (SameContents(source, candidate)) { isDuplicate = true; return candidate; }
        }
    }

    private static bool SameContents(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using var sa = File.OpenRead(a);
        using var sb = File.OpenRead(b);
        return SHA256.HashData(sa).AsSpan().SequenceEqual(SHA256.HashData(sb));
    }

    /// <summary>Reverses a previous run using its CSV log: moved files go back where they came from.</summary>
    public static (int restored, int skipped) Undo(string logPath)
    {
        int restored = 0, skipped = 0;
        var root = Path.GetFullPath(Path.GetDirectoryName(logPath)!).TrimEnd(Path.DirectorySeparatorChar);
        var touchedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(logPath).Skip(1))
        {
            var cols = ParseCsvLine(line);
            if (cols.Count < 3 || cols[0] != "Moved") continue;
            var (original, moved) = (cols[1], cols[2]);
            if (File.Exists(moved) && !File.Exists(original))
            {
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                File.Move(moved, original);
                touchedFolders.Add(Path.GetDirectoryName(moved)!);
                restored++;
            }
            else skipped++;
        }

        // Remove only the month/year folders this run emptied, walking up but never past the destination root.
        foreach (var folder in touchedFolders.OrderByDescending(f => f.Length))
        {
            var dir = folder;
            while (dir.Length > root.Length && dir.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                   && System.IO.Directory.Exists(dir) && !System.IO.Directory.EnumerateFileSystemEntries(dir).Any())
            {
                System.IO.Directory.Delete(dir);
                dir = Path.GetDirectoryName(dir)!;
            }
        }
        return (restored, skipped);
    }

    private static void DeleteEmptyFolders(string root, bool deleteRoot = false)
    {
        foreach (var dir in System.IO.Directory.EnumerateDirectories(root))
        {
            try { DeleteEmptyFolders(dir, deleteRoot: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        if (deleteRoot && !System.IO.Directory.EnumerateFileSystemEntries(root).Any())
            System.IO.Directory.Delete(root);
    }

    private static string Csv(params string[] fields) =>
        string.Join(",", fields.Select(f => f.Contains(',') || f.Contains('"') ? $"\"{f.Replace("\"", "\"\"")}\"" : f));

    internal static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }
}
