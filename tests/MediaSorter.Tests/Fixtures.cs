using System.Buffers.Binary;
using System.Text;

namespace MediaSorter.Tests;

/// <summary>Builds tiny but valid media files so tests don't need binary fixtures checked in.</summary>
internal static class Fixtures
{
    /// <summary>A JPEG containing only an EXIF block with DateTimeOriginal (format "yyyy:MM:dd HH:mm:ss").</summary>
    public static void WriteJpegWithExif(string path, string dateTimeOriginal)
    {
        // TIFF, little endian: header(8) | IFD0 at 8: 1 entry -> ExifIFD ptr | ExifIFD at 26: 1 entry -> DateTimeOriginal | string at 44
        var tiff = new List<byte>();
        tiff.AddRange("II"u8.ToArray()); tiff.AddRange(U16(42)); tiff.AddRange(U32(8));
        tiff.AddRange(U16(1)); tiff.AddRange(Entry(0x8769, 4, 1, 26)); tiff.AddRange(U32(0));
        var value = Encoding.ASCII.GetBytes(dateTimeOriginal + "\0");
        tiff.AddRange(U16(1)); tiff.AddRange(Entry(0x9003, 2, (uint)value.Length, 44)); tiff.AddRange(U32(0));
        tiff.AddRange(value);

        var app1 = new List<byte>();
        app1.AddRange("Exif\0\0"u8.ToArray());
        app1.AddRange(tiff);

        using var f = File.Create(path);
        f.Write([0xFF, 0xD8]);                                   // SOI
        f.Write([0xFF, 0xE1]);                                   // APP1
        f.Write(BigU16((ushort)(app1.Count + 2)));
        f.Write(app1.ToArray());
        f.Write([0xFF, 0xD9]);                                   // EOI
    }

    /// <summary>A QuickTime/MP4 file whose movie header holds <paramref name="createdUtc"/>.</summary>
    public static void WriteMovieWithCreated(string path, DateTime createdUtc, string brand = "qt  ")
    {
        var secs = (uint)(createdUtc - new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        var mvhd = new List<byte>();
        mvhd.AddRange(new byte[4]);                 // version + flags
        mvhd.AddRange(BigU32(secs));                // creation
        mvhd.AddRange(BigU32(secs));                // modification
        mvhd.AddRange(BigU32(1000));                // timescale
        mvhd.AddRange(BigU32(0));                   // duration
        mvhd.AddRange(BigU32(0x00010000));          // rate 1.0
        mvhd.AddRange([0x01, 0x00]);                // volume 1.0
        mvhd.AddRange(new byte[10]);                // reserved
        int[] matrix = [0x10000, 0, 0, 0, 0x10000, 0, 0, 0, 0x40000000];
        foreach (var m in matrix) mvhd.AddRange(BigU32((uint)m));
        mvhd.AddRange(new byte[24]);                // pre-defined
        mvhd.AddRange(BigU32(2));                   // next track id

        var ftyp = Atom("ftyp", [.. Encoding.ASCII.GetBytes(brand), .. BigU32(0), .. Encoding.ASCII.GetBytes(brand)]);
        var moov = Atom("moov", Atom("mvhd", mvhd.ToArray()));
        File.WriteAllBytes(path, [.. ftyp, .. moov]);
    }

    private static byte[] Atom(string type, byte[] body) =>
        [.. BigU32((uint)(body.Length + 8)), .. Encoding.ASCII.GetBytes(type), .. body];

    private static byte[] Entry(ushort tag, ushort type, uint count, uint valueOrOffset) =>
        [.. U16(tag), .. U16(type), .. U32(count), .. U32(valueOrOffset)];

    private static byte[] U16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); return b; }
    private static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); return b; }
    private static byte[] BigU16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, v); return b; }
    private static byte[] BigU32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); return b; }
}

/// <summary>A throwaway folder under %TEMP% that is deleted after the test.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MediaSorterTests", Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}
