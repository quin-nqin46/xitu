import QtQuick
import Quickshell.Hyprland

// Desktop.qml 的 Hyprland 后端，只在 Hyprland 会话里加载。
QtObject {
    function focusedName() { return Hyprland.focusedMonitor ? Hyprland.focusedMonitor.name : ""; }

    function maybeFullscreen(screen) {
        var mon = Hyprland.monitorFor(screen);
        var ws = mon ? mon.activeWorkspace : null;
        return !!(ws && ws.hasFullscreen);
    }

    // 工作区里有全屏或最大化窗口时，toplevel 的 lastIpcObject 可能是旧的，先刷新再判断
    function refresh() { Hyprland.refreshToplevels(); }

    // 只有真全屏（fullscreen 模式 2）才算。hasFullscreen 把最大化（模式 1）也算进去，那样最大化窗口的屏上就唤不出绳子。
    function fullscreenOn(screen) {
        var mon = Hyprland.monitorFor(screen);
        var ws = mon ? mon.activeWorkspace : null;
        if (!ws || !ws.hasFullscreen) return false;
        var tops = ws.toplevels ? ws.toplevels.values : [];
        for (var i = 0; i < tops.length; i++) {
            var o = tops[i].lastIpcObject;
            if (o && o.fullscreen === 2) return true;
        }
        return false;
    }
}
