//@ pragma UseQApplication
import QtQuick
import Quickshell
import Quickshell.Wayland
import "layout.js" as L

// 绳子的离屏渲染（qs -p linux/tools/render-rope.qml 跑）。
// 复用仓库里真的 Rope.qml 和 Card.qml，喂 tools/out/fake-1..3.png 纯色假图；
// 假主题按 shared/themes/polaroid-blue.json 的内置默认值手写（accent-strong = #2C62DD）。
// 窗口可见性手法同 render-shell.qml：真实建窗但透明 + 无输入区 + 内容 opacity 0，
// grabToImage 对内部 Item 出全彩 PNG。
//
// 产出 tools/out/rope.png
ShellRoot {
    id: root

    readonly property string outDir: Qt.resolvedUrl("out").toString().replace("file://", "")

    function log(msg) { console.log("[xitu-render-rope] " + msg); }

    // ---- 假主题：Rope + Card 读到的字段，值取自 shared/themes/polaroid-blue.json ----
    QtObject {
        id: ropeTheme

        property int rev: 0

        // 绳
        property color ropeColor: "#2C62DD"
        property real ropeWidth: 3
        property color ropeShadow: Qt.rgba(20 / 255, 30 / 255, 60 / 255, 0.20)
        property real ropeShadowWidth: 4
        property real ropeShadowOffset: 2
        property string ropeTexture: "none"

        // 绳两端（圆钉）
        property string endStyle: "pin"
        property real endInset: 40
        property color pinColor: "#FFFFFF"
        property color pinRing: Qt.rgba(30 / 255, 40 / 255, 70 / 255, 0.20)
        property color dotColor: "#2C62DD"
        property real endRadius: 8
        property real dotRadius: 3.5

        // 夹子
        property color clipColor: "#2C62DD"
        property real clipWidth: 12
        property real clipHeight: 22
        property real clipRadius: 3
        property color clipBorder: "transparent"
        property bool clipBorderOn: false
        property bool highlightOn: true
        property color highlightColor: Qt.rgba(1, 1, 1, 0.55)
        property real highlightWidth: 2
        property real highlightHeight: 16
        property real highlightRadius: 1
        property real highlightShift: -2.5
        property real clipAbove: 10
        property real clipBelow: 12
        property bool clipShadow: true

        // 相框
        property color framePaper: "#FFFFFF"
        property real padLeft: 7
        property real padTop: 7
        property real padRight: 7
        property real padBottom: 22
        property real frameRadius: 4
        property real photoRadius: 2
    }

    // ---- 假 host：Card 需要的 RopeWindow 接口，交互函数全是 no-op ----
    QtObject {
        id: fakeHost

        property var shell: fakeShell
        property real ropeWidth: win.width
        property int layoutVersion: 0
        property var stage: stage

        function slotOf(id) {
            var ids = ["c1", "c2", "c3"];
            return Math.max(0, ids.indexOf(id));
        }
        function liveCount() { return 3; }
        function log(msg) { root.log(msg); }
        function beginMove(id) {}
        function endMove(commit) {}
        function updateMove(id, cx) { return Qt.point(0, 0); }
        function copyFile(path) {}
        function openFile(path) {}
        function discard(id) {}
        function setFavorite(path, on) {}
        function revealFile(path) {}
        function openGallery() {}
        function menuShown() {}
        function menuHidden() {}
    }

    QtObject {
        id: fakeShell
        property var theme: ropeTheme
    }

    // 三张卡片的假数据：静态倾角和真用起来一样是小角度
    readonly property var fakeCards: [
        { entryId: "c1", path: outDir + "/fake-1.png", tilt: -1.6 },
        { entryId: "c2", path: outDir + "/fake-2.png", tilt: 1.2 },
        { entryId: "c3", path: outDir + "/fake-3.png", tilt: -0.8 }
    ]

    PanelWindow {
        id: win
        visible: true
        color: "transparent"
        anchors.top: true
        anchors.right: true
        implicitWidth: 560
        implicitHeight: L.PanelHeight
        mask: Region {}

        WlrLayershell.layer: WlrLayer.Overlay
        WlrLayershell.namespace: "xitu-render"
        WlrLayershell.keyboardFocus: WlrKeyboardFocus.None

        // opacity:0 只作用于屏幕合成；对 stage 自己 grabToImage 不生效，出的是全彩图
        Item {
            anchors.fill: parent
            opacity: 0

            Item {
                id: stage
                width: win.width
                height: win.height

                Rope {
                    width: parent.width
                    height: L.PanelHeight
                    theme: ropeTheme
                }

                Repeater {
                    model: root.fakeCards

                    delegate: Card {
                        required property var modelData

                        entryId: modelData.entryId
                        path: modelData.path
                        tilt: modelData.tilt
                        gone: false
                        host: fakeHost
                        // 跳过入场弹簧：直接落在静止状态（透明度、摆角都到位）
                        drop: 0
                        swing: 0
                    }
                }
            }
        }
    }

    Timer {
        id: waitTimer
        // 等假图异步加载完（纯色小 PNG，1.2 秒足够）
        interval: 1200
        onTriggered: root.grabNow()
    }

    function grabNow() {
        if (!stage) {
            root.log("出图失败：stage 没加载出来");
            Qt.quit();
            return;
        }
        stage.grabToImage(function(result) {
            if (!result || !result.saveToFile(root.outDir + "/rope.png")) {
                root.log("出图失败 rope.png");
                Qt.quit();
                return;
            }
            root.log("已保存 rope.png");
            Qt.quit();
        });
    }

    Component.onCompleted: waitTimer.restart()
}
