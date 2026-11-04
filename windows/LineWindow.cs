using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Clothesline;

/// 一块屏幕顶上的透明条。不进任务栏、不进 Alt+Tab、不抢焦点。
/// 只有照片区域吃鼠标，其余点击穿透。
internal sealed class LineWindow : Window
{
    private readonly LineView _view;
    private readonly TranslateTransform _slide;
    private bool _passThrough = true;
    private bool _revealed;
    private int _placedLeft, _placedTop, _placedWidth, _placedHeight;

    public LineWindow(Line line, MonitorDesc monitor)
    {
        Monitor = monitor;
        Title = "系图";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = Math.Max(1, monitor.Bounds.Width / monitor.Scale);
        Height = Layout.PanelHeight;
        UseLayoutRounding = true;

        _slide = new TranslateTransform(0, -(Layout.PanelHeight + Layout.TuckGap));
        _view = new LineView(line) { RenderTransform = _slide };
        Content = _view;

        SourceInitialized += (_, _) =>
        {
            ApplyExStyle(passThrough: true);
            Place(monitor);
        };
    }

    public MonitorDesc Monitor { get; private set; }
    public IntPtr Handle => new WindowInteropHelper(this).Handle;
    public bool AllowClose { get; set; }
    public bool IsRevealed => _revealed;
    public bool Pinned { get; set; }
    public DateTime PeekUntilUtc { get; set; } = DateTime.MinValue;

    public int Capacity => Layout.Capacity(ActualWidth > 1 ? ActualWidth : Width);

    public bool InZone(int x, int y)
    {
        var bottom = _placedTop + Math.Max(_placedHeight, 1);
        return x >= _placedLeft && x < _placedLeft + _placedWidth && y >= Monitor.Bounds.Top && y < bottom;
    }

    public bool HitPhoto(int x, int y) => _revealed && _view.HitPhoto(x, y);

    public void Detach() => _view.Detach();

    public void SetRevealed(bool on)
    {
        if (_revealed == on) return;
        _revealed = on;
        if (on)
        {
            if (!IsVisible) Show();
            Place(Monitor);
        }
        else
        {
            Pinned = false;
            PeekUntilUtc = DateTime.MinValue;
            SetPassThrough(true);
        }

        var to = on ? 0 : -(Layout.PanelHeight + Layout.TuckGap);
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(on ? Layout.RevealMs : Layout.HideMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = on ? EasingMode.EaseOut : EasingMode.EaseIn },
        };
        if (!on)
        {
            anim.Completed += (_, _) =>
            {
                if (!_revealed) Hide();
            };
        }
        else
        {
            anim.Completed += (_, _) =>
            {
                if (_revealed) _view.Sway();
            };
        }
        _slide.BeginAnimation(TranslateTransform.YProperty, anim);
        Log.Info((on ? "滑出 " : "收起 ") + Monitor.Device);
    }

    public void SetPassThrough(bool pass)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var current = NativeMethods.GetExStyle(hwnd).ToInt64();
        var want = current | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        if (pass) want |= NativeMethods.WsExTransparent;
        else want &= ~NativeMethods.WsExTransparent;
        // WPF 有时会改回扩展样式，所以和上次意图相同也要再写一次。
        if (want != current) NativeMethods.SetExStyle(hwnd, new IntPtr(want));
        _passThrough = pass;
    }

    public void Place(MonitorDesc monitor)
    {
        Monitor = monitor;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var width = Math.Max(1, monitor.Bounds.Width);
        var height = Math.Max(1, (int)Math.Round(Layout.PanelHeight * monitor.Scale));
        if (width == _placedWidth && height == _placedHeight
            && monitor.Bounds.Left == _placedLeft && monitor.Bounds.Top == _placedTop)
            return;

        Width = width / monitor.Scale;
        Height = Layout.PanelHeight;
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            monitor.Bounds.Left,
            monitor.Bounds.Top,
            width,
            height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        _placedLeft = monitor.Bounds.Left;
        _placedTop = monitor.Bounds.Top;
        _placedWidth = width;
        _placedHeight = height;
    }

    public void SetTopmost(bool on)
    {
        Topmost = on;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(
            hwnd,
            on ? NativeMethods.HwndTopmost : NativeMethods.HwndNotTopmost,
            0, 0, 0, 0,
            0x0001 | 0x0002 | NativeMethods.SwpNoActivate); // NOSIZE | NOMOVE
    }

    /// 窗口从主屏挪到缩放比例不同的屏时，Windows 会按新比例把窗口放大或缩小（WPF 随后也调整一次），
    /// 这时要按目标屏的物理像素重新摆一遍，否则窗口比屏幕宽、绳子中点跑到屏幕外。
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Replace();
        Dispatcher.BeginInvoke(Replace, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Replace()
    {
        _placedWidth = 0; // 跳过 Place 的「位置没变就不动」
        Place(Monitor);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose && !Dispatcher.HasShutdownStarted)
            e.Cancel = true;
        base.OnClosing(e);
    }

    private void ApplyExStyle(bool passThrough)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var ex = NativeMethods.GetExStyle(hwnd).ToInt64();
        ex |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        if (passThrough) ex |= NativeMethods.WsExTransparent;
        else ex &= ~NativeMethods.WsExTransparent;
        NativeMethods.SetExStyle(hwnd, new IntPtr(ex));
    }
}
