//@ pragma UseQApplication
import QtQuick
import Quickshell
import Quickshell.Io

// 系图 Linux 版（Quickshell）独立运行的壳。启动：qs -p <本目录>
// 后台逻辑在 XituCore.qml；这里给每块屏建一根绳子（开发用的抽屉见 uiMode）。
// 抽屉若已经并进 Caelestia 外壳的 xitu 模块，就不必再起这个进程；这个壳仍用于绳子模式，以及不依赖 Caelestia 的独立运行。
ShellRoot {
    id: root

    XituCore {
        id: core
        followCaelestia: root.uiMode === "drawer"
        onPopRequested: path => {
            var w = root.focusedUi();
            if (!w) return;
            if (root.uiMode === "drawer") w.popNew(path);
            else w.peek();
        }
        onPeekRequested: { var w = root.focusedUi(); if (w) w.peek(); }
        onToggleRequested: { var w = root.focusedUi(); if (w) w.toggle(); }
        onQuitRequested: Qt.quit()
    }

    // ---- 界面模式：发行版只有绳子（和 Windows 一样）----
    // 抽屉（DrawerWindow）只留作开发用：设了环境变量 XITU_UI=drawer 才出现，settings.json 不再能打开它。
    readonly property string uiMode: Quickshell.env("XITU_UI") === "drawer" ? "drawer" : "rope"
    readonly property bool uiChecked: true

    function focusedIn(list) {
        var s = core.desktop.focusedScreen();
        var name = s ? s.name : "";
        for (var i = 0; i < list.length; i++)
            if (list[i].monitorName === name) return list[i];
        return list.length > 0 ? list[0] : null;
    }

    // 截图飞上绳子：交给框选起点所在那块屏的绳子；全屏或找不到屏时返回 false，由 core 直接挂
    function flyCapture(shot) {
        if (uiMode !== "rope") return false;
        var list = ropes.instances;
        for (var i = 0; i < list.length; i++) {
            var s = list[i].modelData;
            if (shot.x >= s.x && shot.x < s.x + s.width && shot.y >= s.y && shot.y < s.y + s.height)
                return list[i].flyIn(shot);
        }
        return false;
    }

    Component.onCompleted: core.flyHandler = flyCapture

    function focusedUi() { return focusedIn(uiMode === "drawer" ? drawers.instances : ropes.instances); }

    // ---- 微风：7–16 秒一次 ----
    Timer {
        id: breeze
        interval: 7000 + Math.random() * 9000
        running: true
        onTriggered: {
            var list = ropes.instances;
            for (var i = 0; i < list.length; i++) list[i].gust();
            interval = 7000 + Math.random() * 9000;
            restart();
        }
    }

    Variants {
        id: ropes
        // 抽屉模式下不建绳子窗口，绳子模式行为和以前完全一样
        model: root.uiChecked && root.uiMode === "rope" ? Quickshell.screens : []
        RopeWindow {
            shell: core
        }
    }

    Variants {
        id: drawers
        model: root.uiChecked && root.uiMode === "drawer" ? Quickshell.screens : []
        DrawerWindow {
            shell: core
        }
    }
}
