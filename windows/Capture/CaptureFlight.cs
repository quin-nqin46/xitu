// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Clothesline;

/// 截图从原位置缩小飞到卡片上。缓动和 30 DIP 的弧线参照原版，时长 0.56 秒。
internal sealed class CaptureFlight
{
    private static readonly List<CaptureFlight> Live = new();

    private readonly Window _window;
    private const double ShadowBlur = 14;
    private readonly Border _card;
    private readonly Grid _host;
    private readonly SoftShadow _shadow;
    private readonly Rect _area; // 窗口在屏内的范围（DIP，相对屏左上角）
    private readonly Rect _from;
    private readonly Rect _to;
    private readonly double _tilt;
    private readonly Action _landed;
    private TimeSpan _start = TimeSpan.MinValue;
    private bool _fading;
    private double _fade;

    private CaptureFlight(BitmapSource image, Rect from, Rect to, double tilt, MonitorDesc monitor, Action landed)
    {
        _from = from;
        _to = to;
        _tilt = tilt;
        _landed = landed;

        var photo = new Image { Source = image, Stretch = Stretch.Fill };
        _card = new Border
        {
            Child = photo,
            BorderThickness = new Thickness(0),
            IsHitTestVisible = false,
        };
        // 投影按落点卡片大小画一次，飞行中只拉伸（见 SoftShadow）
        _shadow = new SoftShadow(Math.Max(1, to.Width), Math.Max(1, to.Height), ThemeStore.Current.Frame.Radius, ShadowBlur, depth: 6, opacity: 0.35);
        _host = new Grid
        {
            RenderTransformOrigin = new Point(0.5, 0),
            RenderTransform = new RotateTransform(),
            IsHitTestVisible = false,
            Children = { _shadow, _card },
        };

        // 窗口只盖起点到落点这一块（含弧线、投影、倾斜的余量），不铺满整屏：
        // 透明分层窗口每帧都要把整块像素交给系统合成，软件渲染下整屏 2880×1800 一帧就是 20 MB。
        var screen = new Rect(0, 0, monitor.Bounds.Width / monitor.Scale, monitor.Bounds.Height / monitor.Scale);
        var area = Rect.Union(from, to);
        area.Inflate(Layout.FlightArc + ShadowBlur + 40, Layout.FlightArc + ShadowBlur + 40);
        area.Intersect(screen);
        _area = area.IsEmpty || area.Width < 1 || area.Height < 1 ? screen : area;

        var canvas = new Canvas { IsHitTestVisible = false };
        canvas.Children.Add(_host);
        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Width = Math.Max(1, _area.Width),
            Height = Math.Max(1, _area.Height),
            Content = canvas,
            IsHitTestVisible = false,
        };
        _window.SourceInitialized += (_, _) => Place(monitor);
        // 跨缩放比例的屏：Windows 自动缩放窗口后按目标屏物理像素重摆
        _window.DpiChanged += (_, _) =>
        {
            Place(monitor);
            _window.Dispatcher.BeginInvoke(() => Place(monitor), System.Windows.Threading.DispatcherPriority.Loaded);
        };
        Apply(0);
    }

    public static void Play(BitmapSource image, Rect fromDip, Rect toDip, double tilt, MonitorDesc monitor, Action landed)
    {
        var flight = new CaptureFlight(image, fromDip, toDip, tilt, monitor, landed);
        Live.Add(flight);
        flight._window.Show();
        CompositionTarget.Rendering += flight.OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not System.Windows.Media.RenderingEventArgs args) return;
        if (_start == TimeSpan.MinValue) _start = args.RenderingTime;
        var raw = (args.RenderingTime - _start).TotalSeconds / Layout.FlightSeconds;
        if (!_fading)
        {
            Apply(Math.Min(1, raw));
            if (raw < 1) return;
            _fading = true;
            _fade = 0;
            try { _landed(); }
            catch (Exception ex) { Log.Error("落地失败: " + ex.Message); }
            return;
        }

        _fade += 1.0 / 60.0 / 0.16;
        _window.Opacity = Math.Max(0, 1 - _fade);
        if (_fade < 1) return;
        CompositionTarget.Rendering -= OnRendering;
        Live.Remove(this);
        _window.Close();
        Memory.ReleaseSoon();
    }

    private void Apply(double raw)
    {
        var k = Ease(raw);
        var width = Lerp(_from.Width, _to.Width, k);
        var height = Lerp(_from.Height, _to.Height, k);
        var center = Lerp(_from.Left + _from.Width / 2, _to.Left + _to.Width / 2, k);
        var top = Lerp(_from.Top, _to.Top, k) - Math.Sin(Math.PI * k) * Layout.FlightArc;
        var chrome = Smooth(k, 0.35, 1);
        var f = ThemeStore.Current.Frame;
        var radius = f.Radius * chrome;

        _card.Width = Math.Max(1, width);
        _card.Height = Math.Max(1, height);
        _card.Padding = new Thickness(f.PadLeft * chrome, f.PadTop * chrome, f.PadRight * chrome, f.PadBottom * chrome);
        _card.CornerRadius = new CornerRadius(radius);
        _card.Background = new SolidColorBrush(Color.FromArgb((byte)(f.Paper.A * chrome), f.Paper.R, f.Paper.G, f.Paper.B));
        if (_card.Child is Image photo)
            photo.Opacity = 1;
        _shadow.Fit(_card.Width, _card.Height, ShadowBlur);
        Canvas.SetLeft(_host, center - width / 2 - _area.X);
        Canvas.SetTop(_host, top - _area.Y);
        if (_host.RenderTransform is RotateTransform rotate)
            rotate.Angle = _tilt * k;
    }

    private void Place(MonitorDesc monitor)
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var ex = NativeMethods.GetExStyle(hwnd).ToInt64();
        ex |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate | NativeMethods.WsExTransparent;
        NativeMethods.SetExStyle(hwnd, new IntPtr(ex));
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            monitor.Bounds.Left + (int)Math.Round(_area.X * monitor.Scale),
            monitor.Bounds.Top + (int)Math.Round(_area.Y * monitor.Scale),
            Math.Max(1, (int)Math.Round(_area.Width * monitor.Scale)),
            Math.Max(1, (int)Math.Round(_area.Height * monitor.Scale)),
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    private static double Ease(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    private static double Smooth(double t, double a, double b)
    {
        var u = Math.Clamp((t - a) / (b - a), 0, 1);
        return u * u * (3 - 2 * u);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
