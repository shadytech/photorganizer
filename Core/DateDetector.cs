using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using MetadataExtractor;

namespace MediaSorter.Core;

public enum DateSource
{
    None,
    PhotoMetadata,
    VideoMetadata,
    FileName,
    FileModified,
    OnlineOnly,
    /// <summary>Modified date, because the user chose to sort by it (not a fallback).</summary>
    ModifiedChosen,
}

public readonly record struct DetectedDate(DateTime? Date, DateSource Source, string Detail)
{
    public static readonly DetectedDate Unknown = new(null, DateSource.None, "No date found");
    public bool IsLowConfidence => Source is DateSource.FileModified or DateSource.None;
}

/// <summary>
/// Works out when a photo or video was taken, trying the most trustworthy sources first:
/// embedded metadata → date in the filename → file modified time.
/// </summary>
public static partial class DateDetector
{
    // Earliest plausible date; QuickTime writes 1904-01-01 or 1970-01-01 when the clock was never set.
    private static readonly DateTime MinPlausible = new(1971, 1, 1);

    // (directory name, tag name, kind) in priority order. Matched by name so we don't depend on
    // which concrete Directory classes a given MetadataExtractor version exposes.
    private enum TagKind { LocalTime, UtcTime }

    private static readonly (string Dir, string Tag, TagKind Kind, DateSource Source)[] Candidates =
    [
        ("Exif SubIFD", "Date/Time Original", TagKind.LocalTime, DateSource.PhotoMetadata),
        // Apple-style creation date carries its own UTC offset ("2023-05-14T14:22:33-0700"),
        // so it already gives local wall-clock time where the video was shot.
        ("QuickTime Metadata Header", "Creation Date", TagKind.LocalTime, DateSource.VideoMetadata),
        ("Exif SubIFD", "Date/Time Digitized", TagKind.LocalTime, DateSource.PhotoMetadata),
        // Movie/track header timestamps are stored in UTC by spec.
        ("QuickTime Movie Header", "Created", TagKind.UtcTime, DateSource.VideoMetadata),
        ("QuickTime Track Header", "Created", TagKind.UtcTime, DateSource.VideoMetadata),
        ("AVI", "Date Time Original", TagKind.LocalTime, DateSource.VideoMetadata),
        ("Exif IFD0", "Date/Time", TagKind.LocalTime, DateSource.PhotoMetadata),
    ];

    public static DetectedDate Detect(string path, bool allowFileDateFallback)
    {
        var fromMeta = FromMetadata(path);
        if (fromMeta is { } m) return m;

        var fromName = FromFileName(Path.GetFileNameWithoutExtension(path));
        if (fromName is { } n) return new DetectedDate(n, DateSource.FileName, "Date in filename");

        if (allowFileDateFallback)
        {
            try
            {
                var modified = File.GetLastWriteTime(path);
                if (IsPlausible(modified))
                    return new DetectedDate(modified, DateSource.FileModified, "File modified date (no metadata)");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return DetectedDate.Unknown;
    }

    /// <summary>Uses only the file's last-modified time. Doesn't open the file.</summary>
    public static DetectedDate FromModifiedDate(string path)
    {
        try
        {
            var modified = File.GetLastWriteTime(path);
            if (IsPlausible(modified))
                return new DetectedDate(modified, DateSource.ModifiedChosen, "File modified date");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return DetectedDate.Unknown;
    }

    public static DetectedDate? FromMetadata(string path)
    {
        IReadOnlyList<MetadataExtractor.Directory> dirs;
        try
        {
            dirs = ImageMetadataReader.ReadMetadata(path);
        }
        catch (Exception)
        {
            // Unsupported/corrupt file - fall through to other strategies.
            return null;
        }

        foreach (var (dirName, tagName, kind, source) in Candidates)
        {
            foreach (var dir in dirs)
            {
                if (!string.Equals(dir.Name, dirName, StringComparison.OrdinalIgnoreCase)) continue;
                var tag = dir.Tags.FirstOrDefault(t => string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase));
                if (tag is null) continue;

                var dt = ReadDate(dir, tag.Type, kind);
                if (dt is { } value && IsPlausible(value))
                    return new DetectedDate(value, source, $"{dirName}: {tagName}");
            }
        }
        return null;
    }

    private static DateTime? ReadDate(MetadataExtractor.Directory dir, int tagType, TagKind kind)
    {
        var raw = dir.GetObject(tagType);

        // Strings that include an offset: keep the wall-clock time as written.
        var s = raw as string ?? (raw is DateTime ? null : dir.GetString(tagType));
        if (s is not null && TryParseOffset(s.Trim().TrimEnd('\0'), out var dto))
            return dto.DateTime;

        if (dir.TryGetDateTime(tagType, out var dt))
        {
            return kind == TagKind.UtcTime
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime()
                : DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        }
        return null;
    }

    private static bool TryParseOffset(string s, out DateTimeOffset dto)
    {
        // Only treat as offset-bearing if there is an explicit zone; otherwise let TryGetDateTime handle it.
        if (!OffsetSuffix().IsMatch(s)) { dto = default; return false; }
        // Normalise "-0700" to "-07:00" for the parser.
        var normalised = CompactOffset().Replace(s, "$1$2:$3");
        return DateTimeOffset.TryParse(normalised, CultureInfo.InvariantCulture, DateTimeStyles.None, out dto);
    }

    /// <summary>Extracts a date from names like IMG_20230514_142233, VID-20230514-WA0001, Screenshot_2023-05-14-14-22-33, FB_IMG_1684072953123.</summary>
    public static DateTime? FromFileName(string name)
    {
        var m = NameDate().Match(name);
        if (m.Success)
        {
            int y = int.Parse(m.Groups["y"].Value), mo = int.Parse(m.Groups["m"].Value), d = int.Parse(m.Groups["d"].Value);
            int h = 0, mi = 0, sec = 0;
            if (m.Groups["H"].Success)
            {
                h = int.Parse(m.Groups["H"].Value); mi = int.Parse(m.Groups["M"].Value); sec = int.Parse(m.Groups["S"].Value);
                if (h > 23 || mi > 59 || sec > 59) h = mi = sec = 0;
            }
            if (d <= DateTime.DaysInMonth(y, mo))
            {
                var dt = new DateTime(y, mo, d, h, mi, sec);
                if (IsPlausible(dt)) return dt;
            }
        }

        // Unix epoch milliseconds (Facebook/Instagram/Telegram exports).
        var e = EpochMillis().Match(name);
        if (e.Success && long.TryParse(e.Value, out var ms))
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
            if (IsPlausible(dt)) return dt;
        }
        return null;
    }

    private static bool IsPlausible(DateTime dt) => dt >= MinPlausible && dt <= DateTime.Now.AddDays(1);

    [GeneratedRegex(@"(?<!\d)(?<y>(?:19|20)\d{2})[-_.]?(?<m>0[1-9]|1[0-2])[-_.]?(?<d>0[1-9]|[12]\d|3[01])(?:[-_ T.]?(?<H>\d{2})[-_.:h]?(?<M>\d{2})[-_.:m]?(?<S>\d{2}))?(?!\d)")]
    private static partial Regex NameDate();

    [GeneratedRegex(@"(?<!\d)1[2-9]\d{11}(?!\d)")]
    private static partial Regex EpochMillis();

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$")]
    private static partial Regex OffsetSuffix();

    [GeneratedRegex(@"([+-])(\d{2})(\d{2})$")]
    private static partial Regex CompactOffset();
}
