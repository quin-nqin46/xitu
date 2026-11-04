// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.Runtime.InteropServices;
using System.Text;

namespace Clothesline;

internal static class NativeMethods
{
    public const int GwlExStyle = -20;
    public const int WsExTransparent = 0x00000020;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;

    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;
    public const uint VkT = 0x54;
    public const uint VkSnapshot = 0x2C;
    public const int WmHotkey = 0x0312;

    public const uint MonitorDefaultToNearest = 2;

    public static readonly IntPtr HwndTopmost = new(-1);
    public static readonly IntPtr HwndNotTopmost = new(-2);
    public static readonly IntPtr HwndMessage = new(-3);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    public const int VkLButton = 0x01;

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    public const int WmClipboardUpdate = 0x031D;
    public const uint CfBitmap = 2;
    public const uint CfDib = 8;
    public const uint CfDibV5 = 17;
    public const uint CfHdrop = 15;
    public const uint CfText = 1;
    public const uint CfUnicodeText = 13;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterClipboardFormat(string name);

    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    public static IntPtr GetExStyle(IntPtr hwnd)
    {
        if (IntPtr.Size == 8) return GetWindowLongPtr64(hwnd, GwlExStyle);
        return new IntPtr(GetWindowLong32(hwnd, GwlExStyle));
    }

    public static void SetExStyle(IntPtr hwnd, IntPtr value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GwlExStyle, value);
        else SetWindowLong32(hwnd, GwlExStyle, value.ToInt32());
    }
}
