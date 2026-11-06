using System.Windows;
using System.Windows.Media;

namespace Clothesline;

/// 绳子、阴影、麻绳纹，两端按主题画圆钉、菱形结加流苏，或者什么都不画。和 Linux 版 Rope.qml 一致。
internal sealed class RopeElement : FrameworkElement
{
    private static readonly Argb TwistColor = new(89, 255, 255, 255); // 白 0.35
    private static readonly Argb PinShadow = new(64, 27, 36, 64);     // rgba(27,36,64,0.25)

    public RopeElement()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        if (width < 2) return;
        var t = ThemeStore.Current;
        var pin = t.Ends.Style == "pin";
        var x0 = pin ? t.Ends.Inset : 0;
        var x1 = pin ? Math.Max(x0, width - t.Ends.Inset) : width;

        var geo = RopeGeometry(x0, x1, width);
        if (!t.Rope.ShadowColor.IsTransparent && t.Rope.ShadowWidth > 0)
        {
            dc.PushTransform(new TranslateTransform(0, t.Rope.ShadowOffset));
            dc.DrawGeometry(null, ThemeStore.Pen(t.Rope.ShadowColor, t.Rope.ShadowWidth), geo);
            dc.Pop();
        }
        dc.DrawGeometry(null, ThemeStore.Pen(t.Rope.Color, t.Rope.Width), geo);
        if (t.Rope.Texture == "twist" && t.Rope.Width > 0)
        {
            // 白色半透明虚线，3 像素一段、空 5 像素。DashStyle 的单位是线宽。
            var w = Math.Max(t.Rope.Width, 0.1);
            var dash = new DashStyle(new[] { 3 / w, 5 / w }, 0);
            dash.Freeze();
            dc.DrawGeometry(null, ThemeStore.Pen(TwistColor, t.Rope.Width, dash), geo);
        }

        if (t.Ends.Style == "none") return;
        foreach (var x in new[] { t.Ends.Inset, Math.Max(0, width - t.Ends.Inset) })
        {
            if (x < 8 || x > width - 8) continue;
            if (pin) DrawPin(dc, t, x, Layout.RopeY(x, width));
            else DrawKnot(dc, t, x, Layout.RopeY(x, width));
        }
    }

    private static void DrawPin(DrawingContext dc, ThemeLook t, double x, double y)
    {
        var r = t.Ends.Radius;
        // 柔和投影：几圈逐渐变淡的圆代替模糊（软件渲染下不用 BlurEffect）
        for (var i = 3; i >= 1; i--)
        {
            var a = (byte)(PinShadow.A / (i + 1));
            dc.DrawEllipse(ThemeStore.Brush(PinShadow with { A = a }), null, new Point(x, y + 2), r + i, r + i);
        }
        var ring = t.Ends.PinRing.IsTransparent ? null : ThemeStore.Pen(t.Ends.PinRing, 1);
        dc.DrawEllipse(ThemeStore.Brush(t.Ends.PinColor), ring, new Point(x, y), r - 0.5, r - 0.5);
        dc.DrawEllipse(ThemeStore.Brush(t.Ends.DotColor), null, new Point(x, y), t.Ends.DotRadius, t.Ends.DotRadius);
    }

    /// 菱形结和流苏。结的边长是 ends.radius，流苏按 14 → 3×26 的比例。
    private static void DrawKnot(DrawingContext dc, ThemeLook t, double x, double y)
    {
        var side = t.Ends.Radius;
        var half = side / 2;
        var brush = ThemeStore.Brush(t.Rope.Color);
        var diamond = new RectangleGeometry(new Rect(-half, -half, side, side), side / 7, side / 7);
        diamond.Freeze();

        dc.PushTransform(new TranslateTransform(x, y));
        dc.PushTransform(new RotateTransform(45));
        dc.DrawGeometry(brush, null, diamond);
        dc.Pop();
        var tassel = side * 3 / 14;
        dc.DrawRectangle(brush, null, new Rect(-tassel / 2, half * Math.Sqrt(2), tassel, side * 26 / 14));
        dc.Pop();
    }

    /// 二次曲线，中点高度落在 Layout.RopeY 上：中点 = (两端 + 2×控制点) / 4。
    private static StreamGeometry RopeGeometry(double x0, double x1, double width)
    {
        var w = Math.Max(width, 1);
        var y0 = Layout.RopeY(x0, w);
        var y1 = Layout.RopeY(x1, w);
        var ctrlY = 2 * Layout.RopeY(w / 2, w) - 0.5 * (y0 + y1);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(x0, y0), false, false);
            ctx.QuadraticBezierTo(new Point(w / 2, ctrlY), new Point(x1, y1), true, false);
        }
        geo.Freeze();
        return geo;
    }
}
