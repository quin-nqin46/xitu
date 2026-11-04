using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MakeIcon;

internal static class Program
{
    private static readonly Color Paper = Color.FromRgb(0xF6, 0xF1, 0xE7);
    private static readonly Color PaperStroke = Color.FromRgb(0xE0, 0xD7, 0xC6);
    private static readonly Color Red = Color.FromRgb(0xB5, 0x30, 0x2A);
    private static readonly Color Wood = Color.FromRgb(0xC8, 0xA5, 0x7A);
    private static readonly Color WoodStroke = Color.FromRgb(0x94, 0x74, 0x4C);
    private static readonly Color Ink = Color.FromRgb(0x2E, 0x34, 0x40);

    [STAThread]
    private static int Main(string[] args)
    {
        new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var assets = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Assets"));
        Directory.CreateDirectory(assets);

        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
        var images = new List<(int Size, byte[] Png)>();
        foreach (var size in sizes)
        {
            var png = RenderPng(size, simple: size <= 24);
            images.Add((size, png));
            if (size == 256)
                File.WriteAllBytes(Path.Combine(assets, "xitu-256.png"), png);
        }

        File.WriteAllBytes(Path.Combine(assets, "xitu.ico"), PackIco(images));
        Console.WriteLine("wrote " + Path.Combine(assets, "xitu.ico"));
        return 0;
    }

    private static byte[] RenderPng(int size, bool simple)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            Draw(dc, size, simple);

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void Draw(DrawingContext dc, double size, bool simple)
    {
        var s = size / 128.0;
        var paper = Brush(Paper);
        var paperEdge = new Pen(Brush(PaperStroke), Math.Max(1, s)) { EndLineCap = PenLineCap.Round };
        paperEdge.Freeze();
        dc.DrawRoundedRectangle(paper, paperEdge, new Rect(0.5, 0.5, size - 1, size - 1), 28 * s, 28 * s);

        var arcWidth = simple ? Math.Max(2, 5 * s) : Math.Max(1.2, 5 * s);
        var arc = new Pen(Brush(Red), arcWidth)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        arc.Freeze();
        var rope = new StreamGeometry();
        using (var ctx = rope.Open())
        {
            ctx.BeginFigure(new Point(6 * s, 34 * s), false, false);
            ctx.QuadraticBezierTo(new Point(64 * s, 58 * s), new Point(122 * s, 34 * s), true, false);
        }
        rope.Freeze();
        dc.DrawGeometry(null, arc, rope);

        var clipRect = new Rect(61 * s, 36 * s, Math.Max(2, 6 * s), Math.Max(4, 16 * s));
        var woodPen = new Pen(Brush(WoodStroke), Math.Max(0.6, s));
        woodPen.Freeze();
        dc.DrawRoundedRectangle(Brush(Wood), woodPen, clipRect, Math.Max(0.6, 1.2 * s), Math.Max(0.6, 1.2 * s));

        var card = new Rect(38 * s, 46 * s, 52 * s, 58 * s);
        var radius = Math.Max(1.5, 8 * s);
        if (!simple)
        {
            var shadow = new SolidColorBrush(Color.FromArgb(55, 0, 0, 0));
            shadow.Freeze();
            dc.DrawRoundedRectangle(shadow, null, new Rect(card.X + 1.5 * s, card.Y + 2.5 * s, card.Width, card.Height), radius, radius);
        }
        dc.DrawRoundedRectangle(Brushes.White, null, card, radius, radius);
        if (!simple)
        {
            dc.DrawRoundedRectangle(
                Brush(Ink),
                null,
                new Rect(46 * s, 54 * s, 36 * s, 30 * s),
                Math.Max(1, 3 * s),
                Math.Max(1, 3 * s));
        }
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static byte[] PackIco(List<(int Size, byte[] Png)> images)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);
        var offset = 6 + 16 * images.Count;
        foreach (var image in images)
        {
            var dim = image.Size >= 256 ? (byte)0 : (byte)image.Size;
            writer.Write(dim);
            writer.Write(dim);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(image.Png.Length);
            writer.Write(offset);
            offset += image.Png.Length;
        }
        foreach (var image in images) writer.Write(image.Png);
        return stream.ToArray();
    }
}
