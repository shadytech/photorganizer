using System.Globalization;
using MediaSorter.Core;

namespace MediaSorter.Tests;

public class EtaTests
{
    private static Eta At(double seconds) => new(() => TimeSpan.FromSeconds(seconds));

    [Fact]
    public void No_estimate_during_warm_up_or_before_any_progress()
    {
        Assert.Null(At(1).Remaining(50, 100));
        Assert.Null(At(10).Remaining(0, 100));
    }

    [Fact]
    public void Remaining_follows_average_rate()
    {
        // 25% done in 10s -> 30s to go.
        Assert.Equal(TimeSpan.FromSeconds(30), At(10).Remaining(25, 100));
        Assert.Equal(TimeSpan.Zero, At(10).Remaining(100, 100));
    }

    [Theory]
    [InlineData(null, "estimating time…")]
    [InlineData(4.0, "a few seconds left")]
    [InlineData(42.0, "about 40 sec left")]
    [InlineData(150.0, "about 3 min left")]
    [InlineData(3600.0, "about 1 hr left")]
    [InlineData(4800.0, "about 1 hr 20 min left")]
    [InlineData(7190.0, "about 2 hr left")]
    public void Friendly_wording(double? seconds, string expected)
    {
        TimeSpan? t = seconds is { } s ? TimeSpan.FromSeconds(s) : null;
        Assert.Equal(expected, Eta.Format(t));
    }

    [Theory]
    [InlineData(8, "8s")]
    [InlineData(192, "3m 12s")]
    [InlineData(3900, "1h 05m")]
    public void Elapsed_wording(int seconds, string expected) =>
        Assert.Equal(expected, Eta.FormatElapsed(TimeSpan.FromSeconds(seconds)));
}

public class ModifiedModeTests
{
    private static string Month(int m) => $"{m:00} - {CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)}";

    [Fact]
    public void Modified_mode_ignores_exif_and_filename()
    {
        using var t = new TempDir();
        var src = t.File("in");
        Directory.CreateDirectory(src);
        var photo = Path.Combine(src, "IMG_20100101_000000.jpg");
        Fixtures.WriteJpegWithExif(photo, "2019:07:04 15:30:00");
        File.SetLastWriteTime(photo, new DateTime(2023, 3, 15, 10, 0, 0));

        var dest = t.File("out");
        var plan = Organizer.BuildPlan(new OrganizeOptions(src, dest, Mode: DateMode.Modified), null, default);

        var item = Assert.Single(plan);
        Assert.Equal(DateSource.ModifiedChosen, item.Detected.Source);
        Assert.Equal(new DateTime(2023, 3, 15, 10, 0, 0), item.Detected.Date);
        Assert.False(item.IsLowConfidence);
        Assert.Equal(Path.Combine(dest, "2023", Month(3), "IMG_20100101_000000.jpg"), item.PlannedPath);
    }

    [Fact]
    public void Date_taken_mode_still_prefers_exif()
    {
        using var t = new TempDir();
        var src = t.File("in");
        Directory.CreateDirectory(src);
        var photo = Path.Combine(src, "a.jpg");
        Fixtures.WriteJpegWithExif(photo, "2019:07:04 15:30:00");
        File.SetLastWriteTime(photo, new DateTime(2023, 3, 15));

        var item = Assert.Single(Organizer.BuildPlan(new OrganizeOptions(src, t.File("out")), null, default));
        Assert.Equal(new DateTime(2019, 7, 4, 15, 30, 0), item.Detected.Date);
    }

    [Fact]
    public void Plan_records_file_sizes_for_eta()
    {
        using var t = new TempDir();
        var src = t.File("in");
        Directory.CreateDirectory(src);
        File.WriteAllBytes(Path.Combine(src, "IMG_20200101_000000.jpg"), new byte[12345]);

        var item = Assert.Single(Organizer.BuildPlan(new OrganizeOptions(src, t.File("out")), null, default));
        Assert.Equal(12345, item.SizeBytes);
    }
}
