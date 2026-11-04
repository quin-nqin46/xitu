// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clothesline;

/// 一块屏幕上的定格画面。框内把 40% 的压暗挖空，露出原来的亮度。
internal sealed class OverlayWindow : Window
{
    private readonly Image _image;
    private readonly System.Windows.Shapes.Path _dim;
    private readonly System.Windows.Shapes.Rectangle _stroke;
    private readonly Border _size;
    private readonly TextBlock _sizeText;
    private readonly System.Windows.Shapes.Rectangle _flash;
    private readonly double _scale;
    private readonly double _dragPx;

    private bool _pressed;
    private bool _dragging;
    private bool _holeOn;
    private int _downX;
    private int _downY;
    private NativeMethods.RECT? _box;

    private static readonly DependencyProperty HoleXProperty =
        DependencyProperty.Register(nameof(HoleX), typeof(double), typeof(OverlayWindow), new PropertyMetadata(0d, OnHoleChanged));
    private static readonly DependencyProperty HoleYProperty =
        DependencyProperty.Register(nameof(HoleY), typeof(double), typeof(OverlayWindow), new PropertyMetadata(0d, OnHoleChanged));
    private static readonly DependencyProperty HoleWProperty =
        DependencyProperty.Register(nameof(HoleW), typeof(double), typeof(OverlayWindow), new PropertyMetadata(0d, OnHoleChanged));
    private static readonly DependencyProperty HoleHProperty =
        DependencyProperty.Register(nameof(HoleH), typeof(double), typeof(OverlayWindow), new PropertyMetadata(0d, OnHoleChanged));

    private double HoleX { get => (double)GetValue(HoleXProperty); set => SetValue(HoleXProperty, value); }
    private double HoleY { get => (double)GetValue(HoleYProperty); set => SetValue(HoleYProperty, value); }
    private double HoleW { get => (double)GetValue(HoleWProperty); set => SetValue(HoleWProperty, value); }
    private double HoleH { get => (double)GetValue(HoleHProperty); set => SetValue(HoleHProperty, value); }

    public OverlayWindow(MonitorDesc monitor, BitmapSource frozen)
    {
        Monitor = monitor;
        _scale = monitor.Scale;
        _dragPx = Math.Max(4, SystemParameters.MinimumHorizontalDragDistance * _scale);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Black;
        Width = Math.Max(1, monitor.Bounds.Width / _scale);
        Height = Math.Max(1, monitor.Bounds.Height / _scale);
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Cursor = Cursors.Cross;
        Focusable = true;

        _image = new Image
        {
            Source = frozen,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(_image, EdgeMode.Aliased);

        _dim = new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(102, 0, 0, 0)),
            IsHitTestVisible = false,
            Opacity = 0,
        };
        _flash = new System.Windows.Shapes.Rectangle
        {
            Fill = Theme.WhiteBrush,
            Opacity = 0,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _stroke = new System.Windows.Shapes.Rectangle
        {
            Stroke = Theme.RedBrush,
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _sizeText = new TextBlock
        {
            Foreground = Theme.WhiteBrush,
            FontSize = 12,
        };
        _size = new Border
        {
            Background = Theme.RedBrush,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 10, 3),
            Child = _sizeText,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        var canvas = new Canvas { IsHitTestVisible = false };
        canvas.Children.Add(_image);
        canvas.Children.Add(_dim);
        canvas.Children.Add(_stroke);
        canvas.Children.Add(_flash);
        canvas.Children.Add(_size);
        Content = canvas;

        SourceInitialized += (_, _) =>
        {
            ApplyToolStyle();
            Place();
            PaintDim(null);
            Motion.Fade(_dim, 1, 120);
        };
        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnDown;
        MouseLeftButtonUp += OnUp;
        MouseRightButtonDown += (_, e) =>
        {
            e.Handled = true;
            CancelRequested?.Invoke();
        };
        SizeChanged += (_, _) =>
        {
            LayoutImage();
            PaintDim(_box);
        };
    }

    public MonitorDesc Monitor { get; }
    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public event Action? CancelRequested;
    public event Action<int, int>? Hover;
    public event Action<NativeMethods.RECT>? RegionChosen;
    public event Action? WindowChosen;

    public void ShowBox(NativeMethods.RECT? screen)
    {
        if (_dragging) return;
        if (Same(_box, screen)) return;
        _box = screen;
        if (screen == null)
        {
            _holeOn = false;
            BeginAnimation(HoleXProperty, null);
            BeginAnimation(HoleYProperty, null);
            BeginAnimation(HoleWProperty, null);
            BeginAnimation(HoleHProperty, null);
            PaintDim(null);
            return;
        }
        var local = ToDip(screen.Value);
        if (!_holeOn || !Motion.Enabled)
        {
            _holeOn = true;
            BeginAnimation(HoleXProperty, null);
            BeginAnimation(HoleYProperty, null);
            BeginAnimation(HoleWProperty, null);
            BeginAnimation(HoleHProperty, null);
            HoleX = local.X;
            HoleY = local.Y;
            HoleW = local.Width;
            HoleH = local.Height;
            PaintHole();
            return;
        }
        Motion.Animate(this, HoleXProperty, local.X, 120, Motion.CubicOut, true, null);
        Motion.Animate(this, HoleYProperty, local.Y, 120, Motion.CubicOut, true, null);
        Motion.Animate(this, HoleWProperty, local.Width, 120, Motion.CubicOut, true, null);
        Motion.Animate(this, HoleHProperty, local.Height, 120, Motion.CubicOut, true, null);
    }

    public void Flash(NativeMethods.RECT screen, Action done)
    {
        if (!Motion.Enabled)
        {
            done();
            return;
        }
        var local = ToDip(screen);
        _flash.Width = Math.Max(1, local.Width);
        _flash.Height = Math.Max(1, local.Height);
        Canvas.SetLeft(_flash, local.X);
        Canvas.SetTop(_flash, local.Y);
        _flash.Visibility = Visibility.Visible;
        _flash.Opacity = 0.6;
        Motion.Fade(_flash, 0, 80, () =>
        {
            _flash.Visibility = Visibility.Collapsed;
            done();
        });
    }

    private static void OnHoleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OverlayWindow overlay && overlay._holeOn) overlay.PaintHole();
    }

    private static bool Same(NativeMethods.RECT? a, NativeMethods.RECT? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        var x = a.Value;
        var y = b.Value;
        return x.Left == y.Left && x.Top == y.Top && x.Right == y.Right && x.Bottom == y.Bottom;
    }

    private void PaintHole()
    {
        if (Content is not Canvas canvas || canvas.ActualWidth <= 1) return;
        var local = new Rect(HoleX, HoleY, Math.Max(1, HoleW), Math.Max(1, HoleH));
        var full = new RectangleGeometry(new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight));
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(full);
        group.Children.Add(new RectangleGeometry(local));
        _dim.Data = group;
        _stroke.Width = local.Width;
        _stroke.Height = local.Height;
        Canvas.SetLeft(_stroke, local.X);
        Canvas.SetTop(_stroke, local.Y);
        _stroke.Visibility = Visibility.Visible;
        if (_box is { } screen)
        {
            _sizeText.Text = screen.Width + " × " + screen.Height;
            _size.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var labelTop = local.Y - _size.DesiredSize.Height - 4;
            if (labelTop < 2) labelTop = local.Y + 4;
            Canvas.SetLeft(_size, Math.Max(2, local.X));
            Canvas.SetTop(_size, labelTop);
            _size.Visibility = Visibility.Visible;
        }
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var screen = ToScreen(e.GetPosition(this));
        _pressed = true;
        _dragging = false;
        _downX = screen.X;
        _downY = screen.Y;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var screen = ToScreen(e.GetPosition(this));
        if (_pressed && e.LeftButton == MouseButtonState.Pressed)
        {
            var dx = Math.Abs(screen.X - _downX);
            var dy = Math.Abs(screen.Y - _downY);
            if (!_dragging && (dx >= _dragPx || dy >= _dragPx)) _dragging = true;
            if (_dragging)
            {
                var current = Clamp(screen.X, screen.Y);
                _box = Normalize(_downX, _downY, current.X, current.Y);
                PaintDim(_box);
                return;
            }
        }
        if (!_pressed) Hover?.Invoke(screen.X, screen.Y);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed || e.ChangedButton != MouseButton.Left) return;
        _pressed = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
        if (_dragging && _box is { } box && box.Width >= 2 && box.Height >= 2)
        {
            _dragging = false;
            RegionChosen?.Invoke(box);
            return;
        }
        _dragging = false;
        WindowChosen?.Invoke();
    }

    private void LayoutImage()
    {
        if (Content is not Canvas canvas) return;
        _image.Width = canvas.ActualWidth;
        _image.Height = canvas.ActualHeight;
        Canvas.SetLeft(_image, 0);
        Canvas.SetTop(_image, 0);
    }

    private void PaintDim(NativeMethods.RECT? screen)
    {
        if (Content is not Canvas canvas || canvas.ActualWidth <= 1) return;
        var full = new RectangleGeometry(new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight));
        if (screen == null)
        {
            _dim.Data = full;
            _stroke.Visibility = Visibility.Collapsed;
            _size.Visibility = Visibility.Collapsed;
            return;
        }

        var local = ToDip(screen.Value);
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(full);
        group.Children.Add(new RectangleGeometry(local));
        _dim.Data = group;

        _stroke.Width = Math.Max(1, local.Width);
        _stroke.Height = Math.Max(1, local.Height);
        Canvas.SetLeft(_stroke, local.X);
        Canvas.SetTop(_stroke, local.Y);
        _stroke.Visibility = Visibility.Visible;

        _sizeText.Text = screen.Value.Width + " × " + screen.Value.Height;
        _size.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var labelTop = local.Y - _size.DesiredSize.Height - 4;
        if (labelTop < 2) labelTop = local.Y + 4;
        Canvas.SetLeft(_size, Math.Max(2, local.X));
        Canvas.SetTop(_size, labelTop);
        _size.Visibility = Visibility.Visible;
    }

    private (int X, int Y) ToScreen(Point dip)
    {
        var x = Monitor.Bounds.Left + (int)Math.Round(dip.X * _scale);
        var y = Monitor.Bounds.Top + (int)Math.Round(dip.Y * _scale);
        return (x, y);
    }

    private (int X, int Y) Clamp(int x, int y)
    {
        var bounds = Monitor.Bounds;
        x = Math.Clamp(x, bounds.Left, bounds.Right - 1);
        y = Math.Clamp(y, bounds.Top, bounds.Bottom - 1);
        return (x, y);
    }

    private NativeMethods.RECT Normalize(int x1, int y1, int x2, int y2)
    {
        var bounds = Monitor.Bounds;
        var rect = new NativeMethods.RECT
        {
            Left = Math.Min(x1, x2),
            Top = Math.Min(y1, y2),
            Right = Math.Max(x1, x2) + 1,
            Bottom = Math.Max(y1, y2) + 1,
        };
        rect.Left = Math.Max(rect.Left, bounds.Left);
        rect.Top = Math.Max(rect.Top, bounds.Top);
        rect.Right = Math.Min(rect.Right, bounds.Right);
        rect.Bottom = Math.Min(rect.Bottom, bounds.Bottom);
        return rect;
    }

    private Rect ToDip(NativeMethods.RECT screen)
    {
        var bounds = Monitor.Bounds;
        return new Rect(
            (screen.Left - bounds.Left) / _scale,
            (screen.Top - bounds.Top) / _scale,
            screen.Width / _scale,
            screen.Height / _scale);
    }

    /// 同绳子窗口：跨缩放比例的屏后按目标屏物理像素重摆，否则定格画面被拉大（像放大镜）。
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Place();
        Dispatcher.BeginInvoke(Place, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Place()
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            Monitor.Bounds.Left,
            Monitor.Bounds.Top,
            Monitor.Bounds.Width,
            Monitor.Bounds.Height,
            NativeMethods.SwpShowWindow);
    }

    private void ApplyToolStyle()
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero) return;
        var ex = NativeMethods.GetExStyle(hwnd).ToInt64();
        ex |= NativeMethods.WsExToolWindow;
        ex &= ~NativeMethods.WsExTransparent;
        NativeMethods.SetExStyle(hwnd, new IntPtr(ex));
    }

    public void FocusForKeys()
    {
        Activate();
        var hwnd = Handle;
        if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
