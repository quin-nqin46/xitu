using System.Windows;
using System.Windows.Media;

namespace Clothesline;

/// 中国红绳子，两端各一个菱形绳结和流苏。没有渐隐。
internal sealed class RopeElement : FrameworkElement
{
    public RopeElement()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        if (width < 2) return;

        var geo = RopeGeometry(width);
        dc.PushTransform(new TranslateTransform(0, 2));
        dc.DrawGeometry(null, Theme.RopeShadowPen, geo);
        dc.Pop();
        dc.DrawGeometry(null, Theme.RopePen, geo);

        DrawKnot(dc, Layout.KnotInset, width);
        DrawKnot(dc, width - Layout.KnotInset, width);
    }

    private static void DrawKnot(DrawingContext dc, double x, double width)
    {
        if (x < 8 || x > width - 8) return;
        var y = Layout.RopeY(x, width);
        var half = Layout.KnotSize / 2;
        var diamond = new RectangleGeometry(new Rect(-half, -half, Layout.KnotSize, Layout.KnotSize), 2, 2);
        diamond.Freeze();

        dc.PushTransform(new TranslateTransform(x, y));
        dc.PushTransform(new RotateTransform(45));
        dc.DrawGeometry(Theme.RedBrush, null, diamond);
        dc.Pop();
        var drop = half * Math.Sqrt(2);
        dc.DrawRectangle(Theme.RedBrush, null, new Rect(-1.5, drop, 3, 26));
        dc.Pop();
    }

    private static StreamGeometry RopeGeometry(double width)
    {
        var top = Layout.RopeTop;
        var sag = Layout.Sag(Math.Max(width, 1));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(0, top), false, false);
            ctx.QuadraticBezierTo(new Point(width / 2, top + 2 * sag), new Point(width, top), true, false);
        }
        geo.Freeze();
        return geo;
    }
}
