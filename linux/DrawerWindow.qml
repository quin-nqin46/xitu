import QtQuick
import Quickshell
import Quickshell.Wayland
import Quickshell.Hyprland
import "layout.js" as L

// 一块屏上的抽屉（drawer 界面）。从顶边右上角「长出来」：
// 窗口贴着顶边框和右边框的内沿（BorderInset），底板右缘、顶缘都贴到窗口边缘，
// 左侧和下方多出 DrawerConcave 的空间画内凹圆角。
// 平时整块点击穿透，只有感应区和展开后的抽屉本体接收鼠标。
PanelWindow {
    id: win

    required property var modelData
    required property var shell      // shell.qml 根对象

    readonly property real conc: L.DrawerConcave
    readonly property real winW: L.DrawerW + conc
    readonly property real winH: L.DrawerH + conc

    screen: modelData
    anchors.top: true
    anchors.right: true
    margins {
        top: L.BorderInset
        right: L.BorderInset
    }
    implicitWidth: winW
    implicitHeight: winH
    // Normal + exclusiveZone 0：不占位，但被放在 Caelestia 边框（保留区）内侧
    exclusionMode: ExclusionMode.Normal
    exclusiveZone: 0
    color: "transparent"
    focusable: false
    surfaceFormat.opaque: false
    visible: revealed || stage.y > -winH + 1

    WlrLayershell.layer: WlrLayer.Overlay
    WlrLayershell.namespace: "xitu-drawer"
    WlrLayershell.keyboardFocus: WlrKeyboardFocus.None

    readonly property string monitorName: modelData ? modelData.name : ""
    property alias stage: stage
    property alias panel: drawerPanel
    property bool revealed: false
    property bool pinned: false        // 快捷键打开：等鼠标进来再离开才收
    property bool everHovered: false
    property bool externalDrag: false  // 别的程序拖着东西悬在抽屉或顶边感应带上
    property bool dropHold: false      // 刚拖入，先别收，让新图入场动画播完

    // ---- 展开 / 收起 ----
    // 真全屏（Hyprland 模式 2，看视频、玩游戏）不展开，判断和 RopeWindow 相同。
    function fullscreenHere() {
        var mon = Hyprland.monitorFor(win.modelData);
        var ws = mon ? mon.activeWorkspace : null;
        if (!ws || !ws.hasFullscreen) return false;
        var tops = ws.toplevels ? ws.toplevels.values : [];
        for (var i = 0; i < tops.length; i++) {
            var o = tops[i].lastIpcObject;
            if (o && o.fullscreen === 2) return true;
        }
        return false;
    }

    property bool pendingPin: false
    Timer {
        id: fullscreenCheck
        interval: 150
        onTriggered: if (!win.fullscreenHere()) win.doReveal(win.pendingPin)
    }

    function reveal(pin) {
        var mon = Hyprland.monitorFor(win.modelData);
        var ws = mon ? mon.activeWorkspace : null;
        if (ws && ws.hasFullscreen && !revealed) {
            pendingPin = !!pin;
            Hyprland.refreshToplevels();
            fullscreenCheck.restart();
            return;
        }
        doReveal(pin);
    }

    function doReveal(pin) {
        pinned = !!pin;
        everHovered = false;
        retractTimer.stop();
        if (!revealed) revealed = true;
    }

    function retract() {
        revealed = false;
        pinned = false;
        peekTimer.stop();
        retractTimer.stop();
    }

    function toggle() { if (revealed) retract(); else reveal(true); }

    function peek() {
        if (revealed) return;
        reveal(false);
        peekTimer.restart();
    }

    // 新图到达：展开 1.5 秒，新图在大图位置做入场动画；鼠标在里面就不收
    function popNew(path) {
        flashTimer.restart();
        drawerPanel.flashPath = path;
        reveal(false);
        peekTimer.restart();
    }

    Timer {
        id: peekTimer
        interval: L.DrawerPeekMs
        onTriggered: {
            if (drawerPanel.menuOpen > 0) return;
            win.dropHold = false;
            if (!panelHover.hovered && !win.externalDrag && !drawerPanel.ownDragging) win.retract();
        }
    }
    Timer {
        id: retractTimer
        interval: L.DrawerRetractDelay
        onTriggered: {
            if (drawerPanel.menuOpen > 0) return;
            if (win.dropHold || win.externalDrag) return;
            if (!panelHover.hovered && !drawerPanel.ownDragging) win.retract();
        }
    }
    // 从顶边感应带拖到抽屉要穿过一小段间隙；离开一小段时间再收。
    Timer {
        id: dragGrace
        interval: L.DrawerRetractDelay
        onTriggered: {
            if (edgeDrop.containsDrag || drawerDrop.containsDrag) return;
            win.externalDrag = false;
            if (win.dropHold && !peekTimer.running) peekTimer.restart();
            win.maybeRetract();
        }
    }
    Timer {
        id: flashTimer
        interval: L.DrawerPeekMs + 400
        onTriggered: drawerPanel.flashPath = ""
    }

    function noteDrag(active) {
        if (active) {
            if (!externalDrag && !drawerPanel.ownDragging) shell.log("有东西拖到抽屉上");
            externalDrag = true;
            dragGrace.stop();
            retractTimer.stop();
            peekTimer.stop();
        } else {
            dragGrace.restart();
        }
    }

    function maybeRetract() {
        if (drawerPanel.menuOpen > 0) return;
        if (dropHold || externalDrag || panelHover.hovered || drawerPanel.ownDragging) return;
        if (!revealed || (pinned && !everHovered)) return;
        retractTimer.restart();
    }

    // 拖入成功后保持展开一会儿，等 hang → popNew 的入场动画。快捷键钉住的不另计时。
    function holdOpen() {
        externalDrag = false;
        dragGrace.stop();
        retractTimer.stop();
        if (!revealed) reveal(false);
        if (!pinned) {
            dropHold = true;
            peekTimer.restart();
        }
    }

    function formatsOf(drop) {
        if (!drop || !drop.formats) return "";
        var out = [];
        for (var i = 0; i < drop.formats.length; i++) out.push(String(drop.formats[i]));
        return out.join(", ");
    }

    function takeDrop(drop) {
        if (drop && drop.acceptProposedAction) drop.acceptProposedAction();
        if (drawerPanel.ownDragging) {
            noteDrag(false);
            return;
        }
        var urls = L.dropUrls(drop, shell.log);
        var b64 = L.dropImageB64(drop, shell.log);
        if (shell.ingestDrop(urls, b64)) holdOpen();
        else shell.log("拖进来的内容里没有可用的图片，格式：" + formatsOf(drop));
        noteDrag(false);
    }

    // 接收鼠标的范围：展开时抽屉本体，收起时为零。拖入期间放宽到整窗。
    mask: Region { item: hit }

    Item {
        id: hit
        readonly property bool wide: win.externalDrag || drawerPanel.ownDragging
        x: win.revealed ? (wide ? 0 : win.conc) : 0
        y: 0
        width: win.revealed ? (wide ? win.winW : win.winW - win.conc) : 0
        height: win.revealed ? (wide ? win.winH : win.winH - win.conc) : 0
    }

    DropArea {
        id: drawerDrop
        anchors.fill: parent
        onEntered: win.noteDrag(true)
        onExited: win.noteDrag(false)
        onDropped: drop => win.takeDrop(drop)

    Item {
        id: stage
        width: win.width
        height: win.height
        y: win.revealed ? 0 : -winH
        Behavior on y {
            NumberAnimation {
                duration: win.revealed ? L.DrawerRevealMs : L.DrawerHideMs
                // 出场带一点回弹，进场直接收上去
                easing.type: win.revealed ? Easing.OutBack : Easing.InCubic
                easing.overshoot: 0.8
            }
        }

        HoverHandler {
            id: panelHover
            onHoveredChanged: {
                if (hovered) {
                    win.everHovered = true;
                    retractTimer.stop();
                    peekTimer.stop();
                } else if (drawerPanel.menuOpen > 0) {
                    return;
                } else if (win.externalDrag) {
                    return;
                } else if (win.revealed && (!win.pinned || win.everHovered)) {
                    win.dropHold = false;
                    retractTimer.restart();
                }
            }
        }

        DrawerPanel {
            id: drawerPanel
            anchors.fill: parent
            shell: win.shell
            dropHover: win.externalDrag && !ownDragging
        }
    }
    }

    // ---- 顶边感应区：顶边右段一条 4 像素窄带，鼠标停 0.25 秒展开 ----
    PanelWindow {
        id: edge
        screen: win.modelData
        anchors.top: true
        anchors.right: true
        implicitWidth: L.DrawerSensorW
        implicitHeight: L.DrawerSensorH
        exclusionMode: ExclusionMode.Ignore
        color: "transparent"
        focusable: false
        WlrLayershell.layer: WlrLayer.Overlay
        WlrLayershell.namespace: "xitu-drawer-edge"
        WlrLayershell.keyboardFocus: WlrKeyboardFocus.None

        // 拖着东西停在这条窄带上，和鼠标悬停一样 0.25 秒展开。
        // 悬停挂在 DropArea 上，免得再盖一层把拖放挡住。Wayland 拖放期间 HoverHandler 不触发。
        DropArea {
            id: edgeDrop
            anchors.fill: parent
            onEntered: {
                win.noteDrag(true);
                edgeTimer.restart();
                retractTimer.stop();
            }
            onExited: {
                edgeTimer.stop();
                win.noteDrag(false);
            }
            onDropped: drop => win.takeDrop(drop)

            HoverHandler {
                id: edgeHover
                onHoveredChanged: {
                    if (hovered) {
                        edgeTimer.restart();
                        retractTimer.stop();
                    } else {
                        edgeTimer.stop();
                        if (drawerPanel.menuOpen > 0) return;
                        if (win.externalDrag) return;
                        // 从顶边往抽屉移动要跨过一小段间隙；0.5 秒内没到就收起
                        if (win.revealed && !win.pinned && !panelHover.hovered) retractTimer.restart();
                    }
                }
            }
        }

        Timer {
            id: edgeTimer
            interval: L.DrawerRevealDelay
            onTriggered: if (edgeHover.hovered || edgeDrop.containsDrag) win.reveal(false)
        }
    }
}
