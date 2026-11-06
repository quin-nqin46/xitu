import QtQuick
import Quickshell
import Quickshell.Wayland

// 跟桌面（合成器）打交道的两件事：哪块屏是聚焦屏、某块屏上是不是有真全屏的窗口。
// 按当前会话挑一个后端，别处只调这里，不直接碰 Hyprland / Sway 的接口：
//   hyprland  Quickshell.Hyprland（HYPRLAND_INSTANCE_SIGNATURE）
//   sway      聚焦屏走 Quickshell.I3（SWAYSOCK），全屏走 toplevel 协议
//   toplevel  wlr-foreign-toplevel（niri、river、labwc、Wayfire 等）：活动窗口所在的屏、它是否全屏
//   none      合成器不给窗口信息（KDE 等）：聚焦屏用鼠标最近碰过的屏，不判断全屏
// 后端文件只在对应会话里加载，别的桌面上不会去连 Hyprland / Sway 的套接字。
Item {
    id: desk
    visible: false

    readonly property bool isHyprland: (Quickshell.env("HYPRLAND_INSTANCE_SIGNATURE") || "") !== ""
    readonly property bool isSway: !isHyprland && (Quickshell.env("SWAYSOCK") || "") !== ""

    // 启动日志里记一笔用的是哪个后端；other 是 toplevel 协议，合成器不支持时退到鼠标最近碰过的屏
    readonly property string kind: isHyprland ? "hyprland" : isSway ? "sway" : "other"

    // 鼠标最近停过的屏（顶边感应条、绳子、图库都会报）。后端答不上聚焦屏时用它。
    property string lastScreenName: ""
    function noteScreen(screen) { if (screen && screen.name) lastScreenName = screen.name; }

    Loader {
        id: backend
        active: desk.isHyprland || desk.isSway
        source: desk.isHyprland ? "DesktopHyprland.qml" : desk.isSway ? "DesktopSway.qml" : ""
    }

    function screenNamed(name) {
        var list = Quickshell.screens;
        for (var i = 0; i < list.length; i++)
            if (list[i].name === name) return list[i];
        return null;
    }

    function onScreen(top, screen) {
        if (!top || !screen) return false;
        var list = top.screens || [];
        for (var i = 0; i < list.length; i++)
            if (list[i] && list[i].name === screen.name) return true;
        return false;
    }

    // 聚焦屏的名字，答不上返回 ""
    function focusedName() {
        if (backend.item) {
            var n = backend.item.focusedName();
            if (n) return n;
        }
        var top = ToplevelManager.activeToplevel;
        if (top && top.screens && top.screens.length > 0 && top.screens[0])
            return top.screens[0].name;
        return "";
    }

    function focusedScreen() {
        var s = screenNamed(focusedName()) || screenNamed(lastScreenName);
        if (s) return s;
        return Quickshell.screens.length > 0 ? Quickshell.screens[0] : null;
    }

    // 这块屏上的活动窗口是不是全屏。foreign-toplevel 不说窗口在哪个工作区，只认活动窗口才不会被藏起来的全屏窗口误判。
    function toplevelFullscreen(screen) {
        var top = ToplevelManager.activeToplevel;
        return !!(top && top.fullscreen && onScreen(top, screen));
    }

    // 粗判：可能有全屏，需要 refresh() 后等一会儿再用 fullscreenOn 确认
    function maybeFullscreen(screen) {
        if (backend.item && backend.item.maybeFullscreen) return backend.item.maybeFullscreen(screen);
        return toplevelFullscreen(screen);
    }
    function refresh() {
        if (backend.item && backend.item.refresh) backend.item.refresh();
    }
    // 真全屏（看视频、玩游戏）才算；最大化不算
    function fullscreenOn(screen) {
        if (backend.item && backend.item.fullscreenOn) return backend.item.fullscreenOn(screen);
        return toplevelFullscreen(screen);
    }
}
