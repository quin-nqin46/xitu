using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Clothesline;

/// 预先画好的柔和投影，垫在卡片下面当普通图片显示。
/// 程序用软件渲染（显卡渲染会让核显驱动多占约 200 MB 内存），软件渲染下 DropShadowEffect 每帧都在 CPU 上重新模糊，
/// 绳子一滑、照片一摆就很费；这里只在第一次用到某个尺寸时模糊一次，之后挪动、淡入淡出都只是贴图。
internal sealed class SoftShadow : Image
{
    private static readonly Dictionary<(int W, int H, int R, int B), BitmapSource> Cache = new();
    private readonly TranslateTransform _offset = new();

    /// 放进与卡片同大小的格子（Grid 第一个子元素）即可；投影向四周外扩 blur，向下偏 depth。
    public SoftShadow(double width, double height, double radius, double blur, double depth, double opacity)
    {
        Stretch = Stretch.Fill;
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        RenderTransform = _offset;
        _offset.Y = depth;
        Opacity = opacity;
        Resize(width, height, radius, blur);
    }

    public void Resize(double width, double height, double radius, double blur)
    {
        Source = Render(width, height, radius, blur);
        Width = width + blur * 2;
        Height = height + blur * 2;
        Margin = new Thickness(-blur, -blur, -blur, -blur);
    }

    /// 卡片大小每帧在变（截图飞行）：只拉伸已画好的投影，不重新模糊。
    public void Fit(double width, double height, double blur)
    {
        Width = Math.Max(1, width + blur * 2);
        Height = Math.Max(1, height + blur * 2);
    }

    /// 悬停加深：投影下移、变浓。
    public void To(double depth, double opacity, int ms)
    {
        Motion.Animate(_offset, TranslateTransform.YProperty, depth, ms, Motion.CubicOut, true, null);
        Motion.Fade(this, opacity, ms);
    }

    public void Snap(double depth, double opacity)
    {
        Motion.Clear(_offset, TranslateTransform.YProperty, depth);
        Motion.Clear(this, OpacityProperty, opacity);
    }

    private static BitmapSource Render(double width, double height, double radius, double blur)
    {
        var key = ((int)Math.Round(width), (int)Math.Round(height), (int)Math.Round(radius), (int)Math.Round(blur));
        if (Cache.TryGetValue(key, out var hit)) return hit;

        var w = key.Item1 + key.Item4 * 2;
        var h = key.Item2 + key.Item4 * 2;
        var shape = new Border
        {
            Width = key.Item1,
            Height = key.Item2,
            Margin = new Thickness(key.Item4),
            CornerRadius = new CornerRadius(key.Item3),
            Background = Brushes.Black,
            Effect = new BlurEffect { Radius = key.Item4, KernelType = KernelType.Gaussian },
        };
        var host = new Grid { Width = w, Height = h, Children = { shape } };
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        // 1.5 倍密度：200% 屏上放大后也不发糊得明显，投影本身就是模糊的
        const double density = 1.5;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(w * density), (int)Math.Ceiling(h * density),
            96 * density, 96 * density, PixelFormats.Pbgra32);
        bmp.Render(host);
        bmp.Freeze();
        Cache[key] = bmp;
        return bmp;
    }
}
