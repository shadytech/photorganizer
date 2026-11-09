using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Draws the Media Sorter icon natively at each size (design space is 256x256) and packs an .ico.
// Original artwork: an amber folder holding a photo, with a calendar badge for "sorted by date".
// Usage: dotnet run --project tools/IconGen -- Assets
var outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
var pngs = new List<(int size, byte[] data)>();
var thread = new Thread(() =>
{
    foreach (var s in sizes)
    {
        var png = Render(s);
        pngs.Add((s, png));
        if (s == 256) File.WriteAllBytes(Path.Combine(outDir, "icon-preview.png"), png);
    }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start(); thread.Join();
WriteIco(Path.Combine(outDir, "app.ico"), pngs);
Console.WriteLine("ok");

static byte[] Render(int size)
{
    var dv = new DrawingVisual();
    using (var dc = dv.RenderOpen())
    {
        dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));
        bool tiny = size <= 24, small = size <= 40;

        Brush Lin(string a, string b) => new LinearGradientBrush((Color)ColorConverter.ConvertFromString(a), (Color)ColorConverter.ConvertFromString(b), 90);
        SolidColorBrush Sol(string c) => new((Color)ColorConverter.ConvertFromString(c));

        // Folder back + tab
        var back = Sol("#D98A1E");
        dc.DrawRoundedRectangle(back, null, new Rect(14, 38, 104, 44), 16, 16);
        dc.DrawRoundedRectangle(back, null, new Rect(14, 58, 228, 170), 20, 20);

        // Photo card, tilted, sticking out of the folder
        dc.PushTransform(new RotateTransform(-9, 128, 110));
        var card = new Rect(58, 22, 150, 128);
        dc.DrawRoundedRectangle(Sol("#33000000"), null, new Rect(card.X + 3, card.Y + 5, card.Width, card.Height), 10, 10);
        dc.DrawRoundedRectangle(Brushes.White, null, card, 10, 10);
        var pic = new Rect(card.X + 11, card.Y + 11, card.Width - 22, card.Height - 40);
        dc.PushClip(new RectangleGeometry(pic, 4, 4));
        dc.DrawRectangle(Lin("#7CC8FF", "#3D8EEA"), null, pic);
        dc.DrawEllipse(Sol("#FFD84D"), null, new Point(pic.Right - 28, pic.Top + 24), 13, 13);
        var far = Triangle(new(pic.Left - 4, pic.Bottom), new(pic.Left + 52, pic.Top + 34), new(pic.Left + 92, pic.Bottom));
        var near = Triangle(new(pic.Left + 30, pic.Bottom), new(pic.Left + 88, pic.Top + 22), new(pic.Right + 6, pic.Bottom));
        dc.DrawGeometry(Sol("#2E8B57"), null, far);
        dc.DrawGeometry(Sol("#3FB06B"), null, near);
        dc.Pop();
        dc.Pop();

        // Folder front with a soft top highlight
        var front = new Rect(14, 104, 228, 124);
        dc.DrawRoundedRectangle(Lin("#FFCF5C", "#F5A623"), null, front, 20, 20);
        dc.DrawRoundedRectangle(Sol("#55FFFFFF"), null, new Rect(front.X + 14, front.Y + 10, front.Width - 28, 6), 3, 3);

        if (!small)
        {
            // Calendar badge, bottom-right
            var cal = new Rect(144, 138, 104, 104);
            dc.DrawRoundedRectangle(Sol("#40000000"), null, new Rect(cal.X + 2, cal.Y + 5, cal.Width, cal.Height), 18, 18);
            dc.DrawRoundedRectangle(Brushes.White, null, cal, 18, 18);
            dc.PushClip(new RectangleGeometry(cal, 18, 18));
            dc.DrawRectangle(Sol("#E5484D"), null, new Rect(cal.X, cal.Y, cal.Width, 30));
            dc.Pop();
            // binder rings
            dc.DrawRoundedRectangle(Sol("#7A1E21"), null, new Rect(cal.X + 26, cal.Y - 8, 10, 22), 5, 5);
            dc.DrawRoundedRectangle(Sol("#7A1E21"), null, new Rect(cal.Right - 36, cal.Y - 8, 10, 22), 5, 5);
            // day grid: 3 x 2, one highlighted
            double gx = cal.X + 15, gy = cal.Y + 42, cell = 22, gap = 5;
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 3; c++)
                {
                    var b = (r == 1 && c == 1) ? Sol("#E5484D") : Sol("#D5DAE1");
                    dc.DrawRoundedRectangle(b, null, new Rect(gx + c * (cell + gap), gy + r * (cell + gap), cell, cell), 5, 5);
                }
        }
        else if (!tiny)
        {
            // Mid sizes: a simple red calendar tab hint keeps the date idea without clutter
            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(150, 146, 96, 96), 18, 18);
            dc.DrawRoundedRectangle(Sol("#E5484D"), null, new Rect(150, 146, 96, 34), 18, 18);
            dc.DrawRectangle(Sol("#E5484D"), null, new Rect(150, 166, 96, 14));
        }
        dc.Pop();
    }
    var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bmp.Render(dv);
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(bmp));
    using var ms = new MemoryStream();
    enc.Save(ms);
    return ms.ToArray();
}

static Geometry Triangle(Point a, Point b, Point c)
{
    var g = new StreamGeometry();
    using (var ctx = g.Open())
    {
        ctx.BeginFigure(a, isFilled: true, isClosed: true);
        ctx.PolyLineTo([b, c], isStroked: false, isSmoothJoin: false);
    }
    g.Freeze();
    return g;
}

static void WriteIco(string path, List<(int size, byte[] data)> images)
{
    using var w = new BinaryWriter(File.Create(path));
    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)images.Count);
    int offset = 6 + 16 * images.Count;
    foreach (var (s, d) in images)
    {
        w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
        w.Write((byte)0); w.Write((byte)0);
        w.Write((ushort)1); w.Write((ushort)32);
        w.Write(d.Length); w.Write(offset);
        offset += d.Length;
    }
    foreach (var (_, d) in images) w.Write(d);
}
