using System.Windows.Threading;

namespace Clothesline;

/// 每块屏幕一个绳子窗口，以及顶边停留 / 离开收起 / 热键。
internal sealed class ScreenHost : IDisposable
{
    private readonly Line _line;
    private readonly List<LineWindow> _windows = new();
    private readonly DispatcherTimer _timer;
    private DateTime? _hotSinceUtc;
    private DateTime? _awaySinceUtc;
    private bool _building;
    private bool _mouseWasDown;
    private bool _pressStartedOnLine;
    private static ScreenHost? _active;

    public static bool IsAnyRevealed => _active?.AnyRevealed ?? false;

    /// 已经滑出的绳子再多停一会儿，让回到绳上的动画看得见。
    public static void HoldRevealed(TimeSpan duration)
    {
        if (_active == null) return;
        foreach (var window in _active._windows)
        {
            if (window.IsRevealed) window.PeekUntilUtc = DateTime.UtcNow + duration;
        }
    }

    public ScreenHost(Line line)
    {
        _line = line;
        _active = this;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1.0 / 30.0), DispatcherPriority.Input, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
    }

    public bool AnyRevealed => _windows.Any(w => w.IsRevealed);

    public void Start()
    {
        Rebuild();
        _timer.Start();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Log.Info("绳子窗口 " + _windows.Count);
    }

    /// 菜单用：已经滑出就全部收起，否则在指针所在屏幕滑出并钉住。
    public void ToggleFromMenu()
    {
        if (AnyRevealed)
        {
            foreach (var window in _windows)
            {
                if (window.IsRevealed) window.SetRevealed(false);
            }
            Log.Info("菜单收起");
            return;
        }
        ShowUnderCursor(pinned: true);
    }

    public void ToggleUnderCursor()
    {
        var window = WindowUnderCursor();
        if (window == null) return;
        if (window.IsRevealed)
        {
            window.SetRevealed(false);
            Log.Info("热键收起");
            return;
        }
        ShowUnderCursor(pinned: true);
    }

    private void ShowUnderCursor(bool pinned)
    {
        var window = WindowUnderCursor();
        if (window == null) return;
        if (FullScreen.IsActive(window.Monitor))
        {
            Log.Info("全屏中，不弹出 " + window.Monitor.Device);
            return;
        }
        foreach (var other in _windows)
        {
            if (!ReferenceEquals(other, window) && other.IsRevealed) other.SetRevealed(false);
        }
        window.Pinned = pinned;
        window.SetRevealed(true);
    }

    public void PeekUnderCursor()
    {
        var window = WindowUnderCursor();
        if (window == null) return;
        if (FullScreen.IsActive(window.Monitor))
        {
            Log.Info("全屏中，挂图后不弹出 " + window.Monitor.Device);
            return;
        }
        window.PeekUntilUtc = DateTime.UtcNow + Layout.PeekDuration;
        window.SetRevealed(true);
    }

    /// 截图落在这块屏上：滑出绳子，好让飞行动画对准卡片。全屏时不弹出。
    public bool RevealOn(MonitorDesc monitor)
    {
        var window = _windows.FirstOrDefault(w => w.Monitor.Device == monitor.Device);
        if (window == null) return false;
        if (FullScreen.IsActive(window.Monitor))
        {
            Log.Info("全屏中，截图后不弹出 " + window.Monitor.Device);
            return false;
        }
        foreach (var other in _windows)
        {
            if (!ReferenceEquals(other, window) && other.IsRevealed) other.SetRevealed(false);
        }
        window.PeekUntilUtc = DateTime.UtcNow + Layout.PeekDuration;
        window.SetRevealed(true);
        return true;
    }

    public double DipWidth(MonitorDesc monitor)
    {
        var window = _windows.FirstOrDefault(w => w.Monitor.Device == monitor.Device);
        if (window == null) return monitor.Bounds.Width / monitor.Scale;
        return window.ActualWidth > 1 ? window.ActualWidth : window.Width;
    }

    public IReadOnlyList<IntPtr> LineHandles()
    {
        var handles = new List<IntPtr>();
        foreach (var window in _windows)
        {
            var handle = window.Handle;
            if (handle != IntPtr.Zero) handles.Add(handle);
        }
        return handles;
    }

    public void SetTopmost(bool on)
    {
        foreach (var window in _windows) window.SetTopmost(on);
    }

    public void Dispose()
    {
        _timer.Stop();
        if (ReferenceEquals(_active, this)) _active = null;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        foreach (var window in _windows)
        {
            window.Detach();
            window.AllowClose = true;
            window.Close();
        }
        _windows.Clear();
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(Rebuild);
    }

    private void Rebuild()
    {
        if (_building) return;
        _building = true;
        try
        {
            var revealed = _windows.Where(w => w.IsRevealed).Select(w => w.Monitor.Device).ToHashSet();
            foreach (var window in _windows)
            {
                window.Detach();
                window.AllowClose = true;
                window.Close();
            }
            _windows.Clear();

            var monitors = Monitors.All();
            if (monitors.Count == 0)
            {
                Log.Error("没有枚举到显示器");
                return;
            }

            foreach (var monitor in monitors)
            {
                var window = new LineWindow(_line, monitor);
                window.Show();
                window.Place(monitor);
                if (revealed.Contains(monitor.Device)) window.SetRevealed(true);
                else window.Hide();
                _windows.Add(window);
                Log.Info($"屏幕 {monitor.Device} {monitor.Bounds.Width}x{monitor.Bounds.Height} dpi {monitor.DpiX}");
            }
            UpdateCapacity();
        }
        finally
        {
            _building = false;
        }
    }

    private void UpdateCapacity()
    {
        if (_windows.Count == 0) return;
        // 每块屏的绳子都挂同一串，按最窄那块算，免得窄屏两头挂出屏幕（同 Linux 版 XituCore.capacity）
        _line.MaxItems = _windows.Min(w => w.Capacity);
    }

    private LineWindow? WindowUnderCursor()
    {
        if (_windows.Count == 0 || !NativeMethods.GetCursorPos(out var pt)) return _windows.FirstOrDefault();
        var monitor = Monitors.Under(_windows.Select(w => w.Monitor).ToList(), pt.X, pt.Y);
        return _windows.FirstOrDefault(w => w.Monitor.Device == monitor?.Device) ?? _windows[0];
    }

    private void Tick()
    {
        if (CaptureSession.IsOpen)
        {
            foreach (var window in _windows)
            {
                if (window.IsRevealed) window.SetRevealed(false);
                window.SetPassThrough(true);
            }
            _hotSinceUtc = null;
            _awaySinceUtc = null;
            return;
        }

        if (_windows.Count == 0 || !NativeMethods.GetCursorPos(out var pt)) return;
        var now = DateTime.UtcNow;

        var holding = GrabArea.Busy;
        foreach (var window in _windows)
        {
            if (window.IsRevealed && !holding && FullScreen.IsActive(window.Monitor))
            {
                Log.Info("全屏，收起 " + window.Monitor.Device);
                window.SetRevealed(false);
            }
        }

        var revealed = _windows.FirstOrDefault(w => w.IsRevealed);
        if (revealed == null)
        {
            _awaySinceUtc = null;
            var under = _windows.FirstOrDefault(w => w.Monitor.InTopBand(pt.X, pt.Y));
            if (under != null && !FullScreen.IsActive(under.Monitor))
            {
                _hotSinceUtc ??= now;
                if (now - _hotSinceUtc >= Layout.RevealDelay)
                {
                    _hotSinceUtc = null;
                    under.SetRevealed(true);
                }
            }
            else
            {
                _hotSinceUtc = null;
            }
            foreach (var window in _windows) window.SetPassThrough(true);
            _mouseWasDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VkLButton) & 0x8000) != 0;
            _pressStartedOnLine = false;
            return;
        }

        _hotSinceUtc = null;
        var inside = revealed.InZone(pt.X, pt.Y);
        if (inside && revealed.Pinned) revealed.Pinned = false;
        // 外部拖拽：按键是在绳子外（或绳子还没滑出时）按下的，带着东西进来，整条绳子都要能接住。
        var mouseDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VkLButton) & 0x8000) != 0;
        if (mouseDown && !_mouseWasDown) _pressStartedOnLine = inside;
        _mouseWasDown = mouseDown;
        var receiving = DropIn.IsHovering || (mouseDown && !holding && inside && !_pressStartedOnLine);
        var busy = revealed.Pinned || now < revealed.PeekUntilUtc || holding || DropIn.IsHovering;
        if (inside || busy)
        {
            _awaySinceUtc = null;
        }
        else
        {
            _awaySinceUtc ??= now;
            if (now - _awaySinceUtc >= Layout.RetractDelay)
            {
                _awaySinceUtc = null;
                revealed.SetRevealed(false);
            }
        }

        // 按下、拖动、右键菜单期间整条窗口都接收鼠标，避免移出照片后穿透把捕获弄丢。
        var overPhoto = revealed.IsRevealed && (holding || receiving || revealed.HitPhoto(pt.X, pt.Y));
        foreach (var window in _windows)
            window.SetPassThrough(!(ReferenceEquals(window, revealed) && overPhoto));
    }
}
