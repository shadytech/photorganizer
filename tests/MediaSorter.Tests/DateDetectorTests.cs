using MediaSorter.Core;

namespace MediaSorter.Tests;

public class DateDetectorTests
{
    [Fact]
    public void Jpeg_exif_date_taken_is_used()
    {
        using var t = new TempDir();
        var path = t.File("holiday.jpg");
        Fixtures.WriteJpegWithExif(path, "2019:07:04 15:30:00");

        var d = DateDetector.Detect(path, allowFileDateFallback: true);

        Assert.Equal(DateSource.PhotoMetadata, d.Source);
        Assert.Equal(new DateTime(2019, 7, 4, 15, 30, 0), d.Date);
    }

    [Fact]
    public void Exif_beats_a_conflicting_filename()
    {
        using var t = new TempDir();
        var path = t.File("IMG_20010101_000000.jpg");
        Fixtures.WriteJpegWithExif(path, "2019:07:04 15:30:00");

        Assert.Equal(new DateTime(2019, 7, 4, 15, 30, 0), DateDetector.Detect(path, true).Date);
    }

    [Theory]
    [InlineData("qt  ", ".mov")]
    [InlineData("isom", ".mp4")]
    [InlineData("mp42", ".mp4")]
    public void Video_creation_time_is_converted_from_utc(string brand, string ext)
    {
        using var t = new TempDir();
        var path = t.File("clip" + ext);
        var utc = new DateTime(2020, 12, 31, 23, 30, 0, DateTimeKind.Utc);
        Fixtures.WriteMovieWithCreated(path, utc, brand);

        var d = DateDetector.Detect(path, allowFileDateFallback: false);

        Assert.Equal(DateSource.VideoMetadata, d.Source);
        Assert.Equal(utc.ToLocalTime(), d.Date);
    }

    [Fact]
    public void Unset_video_clock_1904_is_ignored()
    {
        using var t = new TempDir();
        var path = t.File("VID_20180305_101500.mp4");
        Fixtures.WriteMovieWithCreated(path, new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc), "isom");

        var d = DateDetector.Detect(path, true);

        Assert.Equal(DateSource.FileName, d.Source);
        Assert.Equal(new DateTime(2018, 3, 5, 10, 15, 0), d.Date);
    }

    [Theory]
    [InlineData("IMG_20230514_142233", 2023, 5, 14, 14, 22, 33)]
    [InlineData("PXL_20230514_142233123", 2023, 5, 14, 0, 0, 0)]
    [InlineData("VID-20230514-WA0001", 2023, 5, 14, 0, 0, 0)]
    [InlineData("Screenshot_2023-05-14-14-22-33", 2023, 5, 14, 14, 22, 33)]
    [InlineData("2023-05-14 14.22.33", 2023, 5, 14, 14, 22, 33)]
    [InlineData("Photo 2021.11.02", 2021, 11, 2, 0, 0, 0)]
    public void Dates_are_read_from_common_filenames(string name, int y, int mo, int d, int h, int mi, int s)
    {
        Assert.Equal(new DateTime(y, mo, d, h, mi, s), DateDetector.FromFileName(name));
    }

    [Fact]
    public void Epoch_millis_filenames_are_read()
    {
        var expected = DateTimeOffset.FromUnixTimeMilliseconds(1684072953123).LocalDateTime;
        Assert.Equal(expected, DateDetector.FromFileName("FB_IMG_1684072953123"));
    }

    [Theory]
    [InlineData("IMG_0042")]
    [InlineData("DSC01234")]
    [InlineData("20231345")]        // month 13
    [InlineData("IMG_20230230")]    // Feb 30
    [InlineData("phone 5551234567")]
    public void Non_dates_in_filenames_are_rejected(string name)
    {
        Assert.Null(DateDetector.FromFileName(name));
    }

    [Fact]
    public void File_date_is_used_only_when_allowed()
    {
        using var t = new TempDir();
        var path = t.File("IMG_0042.jpg");
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xD9]);
        var stamp = new DateTime(2015, 6, 1, 12, 0, 0);
        File.SetLastWriteTime(path, stamp);

        var allowed = DateDetector.Detect(path, true);
        Assert.Equal(DateSource.FileModified, allowed.Source);
        Assert.Equal(stamp, allowed.Date);
        Assert.True(allowed.IsLowConfidence);

        Assert.Equal(DateSource.None, DateDetector.Detect(path, false).Source);
    }
}
