// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.Runtime.InteropServices;
using System.Text;

namespace Clothesline;

/// 截图前的顶层窗口，按 Z 序从顶到底。定格之后鼠标下面已经是覆盖窗口，边框以这份列表为准。
internal sealed class WindowList
{
    private const uint GaRoot = 2;
    private const int DwmExtendedFrame = 9;
    private const int DwmCloaked = 14;

    private readonly List<Entry> _topToBottom;

    private WindowList(List<Entry> topToBottom) => _topToBottom = topToBottom;

    public static WindowList Take(IReadOnlyCollection<IntPtr> exclude)
    {
        var handles = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            handles.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        var list = new List<Entry>();
        foreach (var hwnd in handles)
        {
            if (exclude.Contains(hwnd) || !Accept(hwnd)) continue;
            if (!TryBounds(hwnd, out var bounds)) continue;
            if (bounds.Width < 8 || bounds.Height < 8) continue;
            list.Add(new Entry(hwnd, bounds));
        }
        return new WindowList(list);
    }

    /// 指针下的窗口。先问 WindowFromPoint，落到自己的覆盖层时改用截图前的 Z 序。边框裁到 clip 里。
    public NativeMethods.RECT? Hit(int x, int y, NativeMethods.RECT clip, IReadOnlyCollection<IntPtr> excludeLive)
    {
        var point = new NativeMethods.POINT { X = x, Y = y };
        var hwnd = WindowFromPoint(point);
        if (hwnd != IntPtr.Zero) hwnd = GetAncestor(hwnd, GaRoot);
        if (hwnd != IntPtr.Zero && !excludeLive.Contains(hwnd))
        {
            var known = _topToBottom.FirstOrDefault(entry => entry.Hwnd == hwnd);
            if (known != null && Intersect(known.Bounds, clip, out var fromList)) return fromList;
        }

        foreach (var entry in _topToBottom)
        {
            if (excludeLive.Contains(entry.Hwnd) || !entry.Bounds.Contains(x, y)) continue;
            if (Intersect(entry.Bounds, clip, out var hit)) return hit;
        }
        return null;
    }

    private static bool Accept(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
        if (DwmGetInt(hwnd, DwmCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        var name = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, name, name.Capacity);
        return name.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    private static bool TryBounds(IntPtr hwnd, out NativeMethods.RECT bounds)
    {
        if (DwmGetRect(hwnd, DwmExtendedFrame, out bounds, Marshal.SizeOf<NativeMethods.RECT>()) == 0
            && bounds.Width > 0 && bounds.Height > 0)
            return true;
        return NativeMethods.GetWindowRect(hwnd, out bounds);
    }

    private static bool Intersect(NativeMethods.RECT a, NativeMethods.RECT b, out NativeMethods.RECT hit)
    {
        hit = new NativeMethods.RECT
        {
            Left = Math.Max(a.Left, b.Left),
            Top = Math.Max(a.Top, b.Top),
            Right = Math.Min(a.Right, b.Right),
            Bottom = Math.Min(a.Bottom, b.Bottom),
        };
        return hit.Width >= 2 && hit.Height >= 2;
    }

    private sealed record Entry(IntPtr Hwnd, NativeMethods.RECT Bounds);

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativeMethods.POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetRect(IntPtr hwnd, int attribute, out NativeMethods.RECT rect, int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetInt(IntPtr hwnd, int attribute, out int value, int size);
}
