using System.Globalization;
using MediaSorter.Core;

namespace MediaSorter.Tests;

public class OrganizerTests
{
    private static string Month(int m) => $"{m:00} - {CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)}";

    [Fact]
    public void Folder_name_is_year_then_month()
    {
        var f = Organizer.FolderFor(@"D:\Sorted", new DateTime(2024, 3, 9));
        Assert.Equal(Path.Combine(@"D:\Sorted", "2024", Month(3)), f);
        Assert.Equal(Path.Combine(@"D:\Sorted", Organizer.UnknownFolderName), Organizer.FolderFor(@"D:\Sorted", null));
    }

    [Fact]
    public void Full_run_moves_files_handles_clashes_and_duplicates_then_undo_restores()
    {
        using var t = new TempDir();
        var src = t.File("in");
        var dest = t.File("out");
        Directory.CreateDirectory(Path.Combine(src, "phone", "DCIM"));
        Directory.CreateDirectory(Path.Combine(src, "camera"));

        var a = Path.Combine(src, "phone", "DCIM", "photo.jpg");
        var b = Path.Combine(src, "camera", "photo.jpg");          // same name, same month, different content
        var dup = Path.Combine(src, "camera", "copy", "photo.jpg"); // byte-identical to a
        var vid = Path.Combine(src, "phone", "clip.mov");
        var noDate = Path.Combine(src, "random.png");
        var notMedia = Path.Combine(src, "notes.txt");

        Fixtures.WriteJpegWithExif(a, "2019:07:04 15:30:00");
        Fixtures.WriteJpegWithExif(b, "2019:07:20 09:00:00");
        Directory.CreateDirectory(Path.GetDirectoryName(dup)!);
        File.Copy(a, dup);
        Fixtures.WriteMovieWithCreated(vid, new DateTime(2021, 6, 15, 12, 0, 0, DateTimeKind.Utc));
        File.WriteAllBytes(noDate, [1, 2, 3]);
        File.WriteAllText(notMedia, "hello");

        var opt = new OrganizeOptions(src, dest, UseFileDateFallback: false, DeleteEmptySourceFolders: true);
        var plan = Organizer.BuildPlan(opt, null, CancellationToken.None);
        Assert.Equal(5, plan.Count); // .txt excluded

        var summary = Organizer.Execute(plan, opt, null, CancellationToken.None);

        Assert.Equal(4, summary.Moved);
        Assert.Equal(1, summary.Duplicates);
        Assert.Equal(0, summary.Failed);

        var july = Path.Combine(dest, "2019", Month(7));
        Assert.True(File.Exists(Path.Combine(july, "photo.jpg")));
        Assert.True(File.Exists(Path.Combine(july, "photo (2).jpg")));
        Assert.False(File.Exists(Path.Combine(july, "photo (3).jpg")));
        Assert.True(File.Exists(Path.Combine(dest, "2021", Month(6), "clip.mov")));
        Assert.True(File.Exists(Path.Combine(dest, Organizer.UnknownFolderName, "random.png")));
        Assert.True(File.Exists(a) ^ File.Exists(dup), "exactly one of the identical pair stays in source");
        Assert.True(File.Exists(notMedia), "non-media untouched");
        var emptied = File.Exists(a) ? Path.GetDirectoryName(dup)! : Path.Combine(src, "phone");
        Assert.False(Directory.Exists(emptied), "emptied source folder removed");
        Assert.True(Directory.Exists(src), "source root itself is kept");
        Assert.True(File.Exists(summary.LogPath));

        // Undo puts everything back and removes the folders it created.
        var (restored, skipped) = Organizer.Undo(summary.LogPath);
        Assert.Equal(4, restored);
        Assert.Equal(0, skipped);
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(dup));
        Assert.True(File.Exists(b));
        Assert.True(File.Exists(vid));
        Assert.True(File.Exists(noDate));
        Assert.False(Directory.Exists(Path.Combine(dest, "2019")));
        Assert.False(Directory.Exists(Path.Combine(dest, "2021")));
    }

    [Theory]
    [InlineData(@"C:\x\photo.JPG", true)]
    [InlineData(@"C:\x\clip.mov", true)]
    [InlineData(@"C:\x\._photo.jpg", false)]
    [InlineData(@"C:\x\notes.txt", false)]
    public void Media_filter(string path, bool expected) => Assert.Equal(expected, Organizer.IsMedia(path));

    [Fact]
    public void Copy_mode_leaves_originals()
    {
        using var t = new TempDir();
        var src = t.File("in");
        Directory.CreateDirectory(src);
        var a = Path.Combine(src, "IMG_20200102_030405.jpg");
        File.WriteAllBytes(a, [0xFF, 0xD8, 0xFF, 0xD9]);

        var opt = new OrganizeOptions(src, t.File("out"), CopyInsteadOfMove: true);
        var s = Organizer.Execute(Organizer.BuildPlan(opt, null, default), opt, null, default);

        Assert.Equal(1, s.Copied);
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(Path.Combine(t.File("out"), "2020", Month(1), "IMG_20200102_030405.jpg")));
    }

    [Fact]
    public void Destination_inside_source_is_not_rescanned()
    {
        using var t = new TempDir();
        var src = t.File("pics");
        var dest = Path.Combine(src, "Sorted");
        Directory.CreateDirectory(Path.Combine(dest, "2019"));
        File.WriteAllBytes(Path.Combine(dest, "2019", "IMG_20190101_000000.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);
        File.WriteAllBytes(Path.Combine(src, "IMG_20200101_000000.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);

        var plan = Organizer.BuildPlan(new OrganizeOptions(src, dest), null, default);

        Assert.Single(plan);
    }
}
