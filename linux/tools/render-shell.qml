//@ pragma UseQApplication
import QtQuick
import Quickshell
import Quickshell.Io
import Quickshell.Wayland
import "layout.js" as L

// 抽屉的离屏渲染（qs -p linux/tools/render-shell.qml 跑，见 render-offscreen.sh）。
// visible:false 时 quickshell 不建窗口，grabToImage 会报「item is not attached to a window」，
// 所以窗口要真实存在，但整面透明 + 无输入区 + 内容 opacity 0：屏幕上什么都不显示，
// 出图用 grabToImage 直接写 PNG。
//
// 产出 tools/out/drawer.png          —— 常规出图（读不到配色文件时自动用内置默认色）
//      tools/out/drawer-default.png —— XITU_RENDER_DEFAULT=1 时的出图文件名
//      tools/out/drawer-drop.png    —— 外部拖入悬停提示（XITU_RENDER_DROP=1）
//
// 假数据：tools/out/fake-1..5.png（render-offscreen.sh 用 Pillow 生成的纯色图）。
ShellRoot {
    id: root

    readonly property string outDir: Qt.resolvedUrl("out").toString().replace("file://", "")
    readonly property bool dropPass: Quickshell.env("XITU_RENDER_DROP") === "1"
    readonly property bool defaultsPass: Quickshell.env("XITU_RENDER_DEFAULT") === "1"

    // ---- 假 shell：只提供 DrawerPanel 用到的接口 ----
    QtObject {
        id: fakeShell

        property var theme: fakeTheme
        property var line: fakeLine
        function log(msg) { console.log("[xitu-render] " + msg); }
        function copyFile(path) { root.log("copy " + path); }
        function openFile(path) { root.log("open " + path); }
        function revealFile(path) { root.log("reveal " + path); }
        function discard(id) { root.log("discard " + id); }
        function setFavorite(path, on) { root.log("favorite " + path + " " + on); }
        function openGallery() { root.log("gallery"); }
    }

    // 假主题：DrawerPanel 只读这 6 个抽屉配色；读不到配色文件时就用这里的内置默认色
    QtObject {
        id: fakeTheme
        property color drawerSurface: "#0c0e12"
        property color drawerSurfaceHigh: "#161a1f"
        property color drawerFg: "#e1e5ef"
        property color drawerFgDim: "#a7abb4"
        property color drawerPrimary: "#afc9eb"
        property color drawerOutline: "#434850"
    }

    ListModel { id: fakeLine; objectName: "fakeLine" }

    // 从状态目录读配色（XDG_STATE_HOME 下的 caelestia/scheme.json）；读不到就保持内置默认色
    FileView {
        id: schemeFile
        path: (Quickshell.env("XDG_STATE_HOME") || (Quickshell.env("HOME") + "/.local/state")) + "/caelestia/scheme.json"
        blockLoading: true
        printErrors: false
    }

    Component.onCompleted: {
        // 最新在前：fake-5 先挂（最老），fake-1 最后挂（最新 → 大图）
        for (var i = 5; i >= 1; i--)
            fakeLine.append({
                entryId: "e" + i,
                path: root.outDir + "/fake-" + i + ".png",
                tilt: 0,
                gone: false
            });
        var body = "";
        try { body = schemeFile.text(); } catch (e) { body = ""; }
        try {
            var c = JSON.parse(body).colours;
            fakeTheme.drawerSurface = "#" + c.surface;
            fakeTheme.drawerSurfaceHigh = "#" + c.surfaceContainer;
            fakeTheme.drawerFg = "#" + c.onSurface;
            fakeTheme.drawerFgDim = "#" + c.onSurfaceVariant;
            fakeTheme.drawerPrimary = "#" + c.primary;
            fakeTheme.drawerOutline = "#" + c.outlineVariant;
            root.log("配色文件读完，按配色出图");
        } catch (e) {
            root.log("读不到配色文件，按内置默认色出图");
        }
        waitTimer.restart();
    }

    function log(msg) { console.log("[xitu-render] " + msg); }

    Timer {
        id: waitTimer
        // 等小图异步加载完（纯色小 PNG，1.2 秒足够）
        interval: 1200
        onTriggered: root.grabNow()
    }

    function grabNow() {
        var name = root.dropPass ? "drawer-drop.png" : (root.defaultsPass ? "drawer-default.png" : "drawer.png");
        if (!panel) {
            root.log("出图失败：DrawerPanel 没加载出来");
            Qt.quit();
            return;
        }
        panel.grabToImage(function(result) {
            if (!result || !result.saveToFile(root.outDir + "/" + name)) {
                root.log("出图失败 " + name);
                Qt.quit();
                return;
            }
            root.log("已保存 " + name);
            Qt.quit();
        });
    }

    PanelWindow {
        id: win
        // 必须真实建窗 grabToImage 才能用；透明底 + opacity 0 内容 + 空 mask，屏幕上不可见
        visible: true
        color: "transparent"
        anchors.top: true
        anchors.right: true
        implicitWidth: L.DrawerW + L.DrawerConcave
        implicitHeight: L.DrawerH + L.DrawerConcave
        mask: Region {}

        WlrLayershell.layer: WlrLayer.Overlay
        WlrLayershell.namespace: "xitu-render"
        WlrLayershell.keyboardFocus: WlrKeyboardFocus.None

        // opacity:0 只作用于屏幕合成；对 panel 自己 grabToImage 不生效，出的是全彩图
        Item {
            anchors.fill: parent
            opacity: 0

            // DrawerPanel.qml 是 render-offscreen.sh 运行时 ln -sfn 指向上一级的软链
            //（软链不进库），quickshell 的配置扫描出不了 tools 根目录，这样渲染用的就是真实组件。
            DrawerPanel {
                id: panel
                anchors.fill: parent
                shell: fakeShell
                dropHover: root.dropPass
            }
        }
    }
}
