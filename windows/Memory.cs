using System.Windows.Threading;

namespace Clothesline;

/// 截图定格图、图库缩略图这类大位图的原生内存挂在托管对象上，而程序平时托管分配很少，
/// 垃圾回收迟迟不跑，用完的几十上百 MB 会一直留着（实测截图取消后 60 秒仍不回落）。
/// 用完这些大东西后稍等一下主动回收一次。
internal static class Memory
{
    private static DispatcherTimer? _pending;

    public static void ReleaseSoon()
    {
        if (_pending != null) return;
        _pending = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(2) };
        _pending.Tick += (_, _) =>
        {
            _pending.Stop();
            _pending = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        };
        _pending.Start();
    }
}
