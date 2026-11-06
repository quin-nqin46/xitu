using System.Runtime.InteropServices;
using System.Text;

namespace Clothesline;

/// 前台窗口盖住整块屏幕时视为全屏，绳子不弹出。判定规则见 FullScreenRule。
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

        if (!NativeMethods.GetWindowRect(hwnd, out var r)) return false;
        var m = monitor.Bounds;
        return FullScreenRule.IsFullScreen(
            new Box(r.Left, r.Top, r.Right, r.Bottom),
            new Box(m.Left, m.Top, m.Right, m.Bottom),
            GetStyle(hwnd));
    }

    private const int GwlStyle = -16;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    private static long GetStyle(IntPtr hwnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, GwlStyle).ToInt64() : (uint)GetWindowLong32(hwnd, GwlStyle);
}
