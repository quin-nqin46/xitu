namespace Clothesline;

/// 屏幕坐标里的矩形（右、下不含）。和 NativeMethods.RECT 一样的布局，但不依赖 Win32，方便单测。
internal readonly record struct Box(int Left, int Top, int Right, int Bottom);

/// 全屏判定的纯逻辑。FullScreen 负责取前台窗口的数据，这里只做判断。
internal static class FullScreenRule
{
    public const long WsMaximize = 0x01000000;
    public const long WsCaption = 0x00C00000; // WS_BORDER | WS_DLGFRAME

    /// 窗口可以比屏幕略小几像素仍算盖住（有些游戏/播放器留边）。
    public const int Tolerance = 8;

    /// 窗口矩形是否盖住整块屏幕。
    public static bool Covers(Box window, Box monitor) =>
        window.Left <= monitor.Left + Tolerance
        && window.Top <= monitor.Top + Tolerance
        && window.Right >= monitor.Right - Tolerance
        && window.Bottom >= monitor.Bottom - Tolerance;

    /// 前台窗口是否在这块屏幕上全屏。
    ///
    /// 只看矩形不够：任务栏设为自动隐藏时，工作区等于整块屏幕，
    /// 普通最大化窗口（还带边框溢出几像素）也会盖住整块屏幕。
    /// 真正的全屏（游戏、F11、播放器）都会去掉标题栏，
    /// 所以“最大化且带标题栏”的窗口一律不算全屏。
    public static bool IsFullScreen(Box window, Box monitor, long style)
    {
        if (!Covers(window, monitor)) return false;
        var maximized = (style & WsMaximize) != 0;
        var captioned = (style & WsCaption) == WsCaption;
        return !(maximized && captioned);
    }
}
