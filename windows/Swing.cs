using System.Windows.Media;
using System.Windows.Threading;

namespace Clothesline;

/// 逐帧阻尼弹簧。没有照片在动、绳子又收起时不挂 Rendering。
internal static class Swing
{
    private static readonly List<PeggedView> Live = new();
    private static readonly List<PeggedView> Active = new();
    private static bool _hooked;
    private static double _lastTime = double.NaN;
    private static DispatcherTimer? _breeze;
    private static int _hosts;

    public static void HostCreated()
    {
        _hosts++;
        if (_breeze != null) return;
        ArmBreeze();
    }

    public static void HostReleased()
    {
        if (_hosts > 0) _hosts--;
        if (_hosts > 0) return;
        _breeze?.Stop();
        _breeze = null;
    }

    public static void Track(PeggedView view)
    {
        if (!Live.Contains(view)) Live.Add(view);
    }

    public static void Untrack(PeggedView view)
    {
        Live.Remove(view);
        Active.Remove(view);
        StopHookIfIdle();
    }

    public static void Wake(PeggedView view)
    {
        if (!Active.Contains(view)) Active.Add(view);
        if (_hooked) return;
        CompositionTarget.Rendering += OnRendering;
        _hooked = true;
        _lastTime = double.NaN;
    }

    public static (double Stiffness, double Damping) FromResponse(double response, double dampingFraction)
    {
        var omega = 2 * Math.PI / response;
        return (omega * omega, 2 * dampingFraction * omega);
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args) return;
        var t = args.RenderingTime.TotalSeconds;
        if (!double.IsNaN(_lastTime) && t == _lastTime) return;
        var dt = double.IsNaN(_lastTime) ? 1.0 / 60 : t - _lastTime;
        _lastTime = t;
        if (dt <= 0) return;
        if (dt > 0.05) dt = 0.05;

        // Step 里照片可能播完下落、把自己 Untrack 掉，所以遍历快照，不按下标删。
        foreach (var view in Active.ToArray())
        {
            if (!view.Step(dt)) Active.Remove(view);
        }
        StopHookIfIdle();
    }

    private static void StopHookIfIdle()
    {
        if (Active.Count > 0 || !_hooked) return;
        CompositionTarget.Rendering -= OnRendering;
        _hooked = false;
        _lastTime = double.NaN;
    }

    private static void ArmBreeze()
    {
        var wait = 7 + Random.Shared.NextDouble() * 9;
        _breeze = new DispatcherTimer { Interval = TimeSpan.FromSeconds(wait) };
        _breeze.Tick += (_, _) =>
        {
            _breeze?.Stop();
            _breeze = null;
            Gust();
            if (_hosts > 0) ArmBreeze();
        };
        _breeze.Start();
    }

    /// 收起时不推动，否则逐帧回调会在看不见的时候空转。
    private static void Gust()
    {
        if (!ScreenHost.IsAnyRevealed || GrabArea.Busy) return;
        foreach (var view in Live.ToArray())
        {
            if (view.IsFalling) continue;
            var delay = Random.Shared.NextDouble() * 0.35;
            var degrees = 1.6 + Random.Shared.NextDouble() * (3.4 - 1.6);
            if (delay < 0.02)
            {
                view.Nudge(degrees);
                continue;
            }
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (!view.IsFalling && ScreenHost.IsAnyRevealed) view.Nudge(degrees);
            };
            timer.Start();
        }
    }
}
