using System.Text;

namespace Clothesline;

/// 前台窗口盖住整块屏幕时视为全屏，绳子不弹出。
internal static class FullScreen
{
    public static bool IsActive(MonitorDesc monitor)
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hwnd)) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId) return false;

        var name = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, name, name.Capacity);
        var cls = name.ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return false;
        var m = monitor.Bounds;
        const int tol = 8;
        return rect.Left <= m.Left + tol
            && rect.Top <= m.Top + tol
            && rect.Right >= m.Right - tol
            && rect.Bottom >= m.Bottom - tol;
    }
}
