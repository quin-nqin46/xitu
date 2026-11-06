using Xunit;

namespace Clothesline.Tests;

/// 数据按 Windows 实际行为构造：
/// - 最大化窗口的 GetWindowRect 会向四周溢出一圈边框（100% 缩放约 8px，150% 约 11px），
///   底边停在工作区底边 + 边框。
/// - 任务栏自动隐藏时，工作区 = 整块屏幕。
public class FullScreenRuleTests
{
    // 窗口样式
    private const long Visible = 0x10000000;
    private const long Popup = 0x80000000;
    private const long ThickFrame = 0x00040000;
    private const long OverlappedWindow = 0x00CF0000; // 标题栏 + 系统菜单 + 粗边框 + 最小化/最大化按钮
    private const long Maximize = FullScreenRule.WsMaximize;

    private const long NormalMaximized = OverlappedWindow | Maximize | Visible;
    private const long NormalRestored = OverlappedWindow | Visible;
    private const long BorderlessPopup = Popup | Visible;
    // Chrome/Edge 在最大化状态下按 F11：去掉标题栏和粗边框，但保留最大化标记
    private const long BrowserF11 = (OverlappedWindow & ~(FullScreenRule.WsCaption | ThickFrame)) | Maximize | Visible;

    private static readonly ScreenRect Fhd = new(0, 0, 1920, 1080);
    private static readonly ScreenRect Qhd2 = new(1920, 0, 4480, 1440); // 右边第二块屏，150% 缩放

    /// 最大化窗口：工作区四周各溢出 frame 像素。
    private static ScreenRect Maximized(ScreenRect work, int frame) =>
        new(work.Left - frame, work.Top - frame, work.Right + frame, work.Bottom + frame);

    // ---- 复现：只看矩形会误判 ----

    [Fact]
    public void Geometry_alone_misjudges_maximized_window_when_taskbar_autohides()
    {
        // 自动隐藏：工作区 = 整屏，最大化窗口溢出后盖住整屏
        var window = Maximized(Fhd, 8);
        Assert.True(FullScreenRule.Covers(window, Fhd)); // 旧逻辑只做这一步 → 判成全屏
    }

    // ---- 普通窗口：绳子应该能弹出 ----

    [Fact]
    public void Maximized_with_visible_bottom_taskbar_is_not_fullscreen()
    {
        var work = Fhd with { Bottom = 1080 - 48 };
        Assert.False(FullScreenRule.IsFullScreen(Maximized(work, 8), Fhd, NormalMaximized));
    }

    [Theory]
    [InlineData(8)]   // 100%
    [InlineData(11)]  // 150%
    [InlineData(14)]  // 200%
    public void Maximized_with_autohide_taskbar_is_not_fullscreen(int frame)
    {
        Assert.False(FullScreenRule.IsFullScreen(Maximized(Fhd, frame), Fhd, NormalMaximized));
    }

    [Fact]
    public void Autohide_reserving_two_pixels_is_not_fullscreen()
    {
        // 部分系统/第三方任务栏自动隐藏时仍保留 1~2px
        var work = Fhd with { Bottom = 1078 };
        Assert.False(FullScreenRule.IsFullScreen(Maximized(work, 8), Fhd, NormalMaximized));
    }

    [Fact]
    public void Maximized_on_second_monitor_with_autohide_is_not_fullscreen()
    {
        Assert.False(FullScreenRule.IsFullScreen(Maximized(Qhd2, 11), Qhd2, NormalMaximized));
    }

    [Fact]
    public void Small_window_is_not_fullscreen()
    {
        Assert.False(FullScreenRule.IsFullScreen(new ScreenRect(100, 100, 900, 700), Fhd, NormalRestored));
    }

    [Fact]
    public void Fullscreen_window_on_other_monitor_does_not_count_here()
    {
        Assert.False(FullScreenRule.IsFullScreen(Qhd2, Fhd, BorderlessPopup));
    }

    // ---- 真全屏：绳子不应弹出 ----

    [Fact]
    public void Borderless_fullscreen_game_or_player_is_fullscreen()
    {
        Assert.True(FullScreenRule.IsFullScreen(Fhd, Fhd, BorderlessPopup));
    }

    [Fact]
    public void Browser_f11_from_maximized_is_fullscreen()
    {
        Assert.True(FullScreenRule.IsFullScreen(Fhd, Fhd, BrowserF11));
    }

    [Fact]
    public void Borderless_fullscreen_on_second_monitor_is_fullscreen()
    {
        Assert.True(FullScreenRule.IsFullScreen(Qhd2, Qhd2, BorderlessPopup));
    }

    [Fact]
    public void Fullscreen_with_few_pixels_margin_is_still_fullscreen()
    {
        var window = new ScreenRect(4, 4, 1916, 1076);
        Assert.True(FullScreenRule.IsFullScreen(window, Fhd, BorderlessPopup));
    }

    [Fact]
    public void Borderless_maximized_window_counts_as_fullscreen()
    {
        // 无标题栏的最大化窗口（如 WindowStyle=None 的 WPF 程序、部分无边框游戏）盖住整屏，按全屏处理
        Assert.True(FullScreenRule.IsFullScreen(Fhd, Fhd, Popup | Maximize | Visible));
    }

    [Fact]
    public void Restored_captioned_window_stretched_over_screen_is_fullscreen()
    {
        // 未最大化但手动拉满整屏的窗口：保持原行为，仍按全屏处理
        Assert.True(FullScreenRule.IsFullScreen(new ScreenRect(-2, -2, 1922, 1082), Fhd, NormalRestored));
    }
}
