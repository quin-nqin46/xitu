import QtQuick
import QtQuick.Effects
import Qt.labs.platform as Native
import Quickshell.Widgets
import "layout.js" as L

// 一张照片和顶部原木夹（PeggedView.cs）。倾斜是静止角，摆动叠在上面。
// 物理：位置、下落入场、摆动三组阻尼弹簧，参数照搬 Windows 版。
Item {
    id: card

    required property string entryId
    required property string path
    required property real tilt
    required property bool gone
    required property var host   // RopeWindow

    readonly property var theme: host.shell.theme
    // 相框上沿在绳子下方多少。沿用原来的 PinAbove / PinHeight，红绳才和改主题之前重合。
    readonly property real frameDrop: L.PinHeight - 12 - L.PinAbove
    readonly property real frameTop: theme.clipAbove + frameDrop
    // 编辑保存后 rev 加一，换个地址让 Image 重新读文件（本地文件的 ? 后面不影响读哪个文件）
    readonly property int rev: host.shell.thumbRev(path)
    readonly property url fileUrl: "file://" + path + (rev > 0 ? "?v=" + rev : "")
    // 收藏的文件在图库根下的「收藏」文件夹里（Library.IsFavorite）
    readonly property bool favorite: {
        var parts = path.split("/");
        return parts.length >= 2 && parts[parts.length - 2] === "收藏";
    }
    readonly property var photo: L.photoSize(img.implicitWidth, img.implicitHeight)
    readonly property real frameW: photo.w + theme.padLeft + theme.padRight
    readonly property real frameH: photo.h + theme.padTop + theme.padBottom

    width: L.CardWidth
    height: frameTop + frameH

    // ---- 物理状态 ----
    property real px: 0
    property real py: 0
    property real vx: 0
    property real vy: 0
    property real tx: 0
    property real ty: 0
    property bool placed: false

    property real drop: -L.ArriveDrop
    property real dropVel: 0
    property real swing: 16
    property real swingVel: 0
    property real swingK: 46
    property real swingC: 2.6
    property bool easing: false
    property real easeFrom: 0
    property real easeTo: 0
    property real easeT: 0

    property bool falling: false
    property real fallElapsed: 0
    property real fallOffset: 0
    property real fallSpin: 0
    property bool active: true

    readonly property var dropSpring: L.spring(L.ArriveResponse, L.ArriveDamping)
    readonly property var moveSpring: L.spring(L.PositionResponse, L.PositionDamping)

    // ---- 交互状态 ----
    property bool hover: hitArea.containsMouse || crossArea.containsMouse
    property bool pressed: false
    property bool lifted: false      // 沿绳子换位置
    property bool dragging: false    // 拖出一份给别的程序
    property bool copiedShown: false

    x: px
    y: py + drop + fallOffset
    rotation: tilt + swing + fallSpin
    transformOrigin: Item.Top
    opacity: img.status !== Image.Ready ? 0
           : falling ? Math.max(0, 1 - fallElapsed / L.FallSeconds)
           : Math.max(0, Math.min(1, 1 + drop / L.ArriveDrop))
    z: lifted ? 2 : 0

    // ---- 位置 ----
    readonly property int slot: host.layoutVersion, host.slotOf(entryId)
    readonly property int count: host.layoutVersion, host.liveCount()
    onSlotChanged: updateTarget(false)
    onCountChanged: updateTarget(false)
    Connections {
        target: card.host
        function onRopeWidthChanged() { card.updateTarget(false); }
    }
    Connections {
        target: card.theme
        function onRevChanged() { card.updateTarget(false); }
    }
    Component.onCompleted: updateTarget(true)

    onGoneChanged: if (gone) beginFall()

    function updateTarget(snap) {
        if (falling || lifted || slot < 0) return;
        var cx = L.slotX(slot, count, host.ropeWidth);
        setPlacement(cx - L.CardWidth / 2, L.ropeY(cx, host.ropeWidth) - theme.clipAbove, snap);
    }

    function setPlacement(left, top, snap) {
        tx = left;
        ty = top;
        if (snap || !placed) {
            px = left; py = top; vx = 0; vy = 0; placed = true;
            return;
        }
        if (Math.abs(px - left) > 0.4 || Math.abs(py - top) > 0.4) active = true;
    }

    function nudge(degrees) {
        if (falling) return;
        easing = true;
        easeFrom = swing;
        easeTo = degrees;
        easeT = 0;
        swingVel = 0;
        active = true;
    }

    // 拖出一份之后：照片从上方重新落回绳上，再摆几下
    function rehang() {
        if (falling) return;
        drop = -L.ArriveDrop; dropVel = 0;
        easing = false;
        swing = 10; swingVel = 0; swingK = 46; swingC = 2.6;
        active = true;
    }

    // 沿绳拖动时跟手，按移动量给一点惯性摆动
    function follow(left, top) {
        var dx = left - px;
        tx = left; ty = top; px = left; py = top; vx = 0; vy = 0;
        if (easing) { easing = false; swingK = 38; swingC = 2.4; }
        swingVel = Math.max(-90, Math.min(90, swingVel + dx * 3));
        active = true;
    }

    function beginFall() {
        if (falling) return;
        falling = true;
        fallElapsed = 0;
        active = true;
    }

    function showCopied() {
        copiedShown = true;
        copiedTimer.restart();
        nudge(3);
    }

    function stepSpring(value, velocity, target, k, c, dt) {
        var acc = -k * (value - target) - c * velocity;
        velocity += acc * dt;
        return [value + velocity * dt, velocity];
    }

    function step(dt) {
        if (dt <= 0) return;
        if (dt > 0.05) dt = 0.05;

        if (falling) {
            fallElapsed += dt;
            var t = Math.min(1, fallElapsed / L.FallSeconds);
            var eased = t * t * t;
            fallOffset = eased * L.FallDistance;
            fallSpin = (tilt * 7 + 20) * eased;
            if (t >= 1) active = false;
            return;
        }

        var r = stepSpring(px, vx, tx, moveSpring.k, moveSpring.c, dt); px = r[0]; vx = r[1];
        r = stepSpring(py, vy, ty, moveSpring.k, moveSpring.c, dt); py = r[0]; vy = r[1];
        r = stepSpring(drop, dropVel, 0, dropSpring.k, dropSpring.c, dt); drop = r[0]; dropVel = r[1];

        if (easing) {
            easeT += dt / L.NudgeOut;
            if (easeT >= 1) {
                swing = easeTo; swingVel = 0; easing = false; swingK = 38; swingC = 2.4;
            } else {
                var u = 1 - Math.pow(1 - easeT, 3);
                swing = easeFrom + (easeTo - easeFrom) * u;
            }
        } else {
            r = stepSpring(swing, swingVel, 0, swingK, swingC, dt); swing = r[0]; swingVel = r[1];
        }

        var still = Math.abs(px - tx) < 0.4 && Math.abs(vx) < 1.5
                 && Math.abs(drop) < 0.3 && Math.abs(dropVel) < 1.2
                 && !easing && Math.abs(swing) < 0.08 && Math.abs(swingVel) < 0.2;
        if (still) active = false;
    }

    FrameAnimation {
        running: card.active && card.visible
        onTriggered: card.step(frameTime)
    }

    Timer {
        id: copiedTimer
        interval: L.CopiedHint
        onTriggered: card.copiedShown = false
    }

    // ---- 外观 ----

    // 夹子（在卡片下面，卡片压住它下半截）
    Item {
        id: clip
        x: (L.CardWidth - card.theme.clipWidth) / 2
        y: 0
        width: card.theme.clipWidth
        height: card.theme.clipHeight

        RectangularShadow {
            visible: card.theme.clipShadow
            anchors.fill: clipBody
            offset.y: 2
            blur: 5
            radius: clipBody.radius
            color: Qt.rgba(27 / 255, 36 / 255, 64 / 255, 0.25)
        }

        Rectangle {
            id: clipBody
            anchors.fill: parent
            radius: card.theme.clipRadius
            color: card.theme.clipColor
            border.width: card.theme.clipBorderOn ? 1 : 0
            border.color: card.theme.clipBorder
        }

        Rectangle {
            visible: card.theme.highlightOn
            width: card.theme.highlightWidth
            height: Math.min(card.theme.highlightHeight, parent.height)
            radius: card.theme.highlightRadius
            color: card.theme.highlightColor
            x: (parent.width - width) / 2 + card.theme.highlightShift
            y: (parent.height - height) / 2
        }
    }

    Item {
        id: body
        x: (L.CardWidth - card.frameW) / 2
        y: card.frameTop + (card.lifted ? -5 : card.hover && !card.pressed && !card.dragging ? -2 : 0)
        width: card.frameW
        height: card.frameH
        transformOrigin: Item.Top
        scale: card.lifted ? L.LiftScale : card.pressed ? L.PressedScale : card.hover && !card.dragging ? L.HoverScale : 1
        opacity: card.dragging ? L.DragOpacity : 1

        Behavior on y { NumberAnimation { duration: 180; easing.type: Easing.OutQuad } }
        Behavior on scale { NumberAnimation { duration: card.pressed ? 450 : 180; easing.type: card.pressed ? Easing.InQuad : Easing.OutQuad } }

        RectangularShadow {
            anchors.fill: frame
            offset.y: card.lifted ? 10 : card.hover ? 8 : 6
            blur: 14
            radius: card.theme.frameRadius
            color: Qt.rgba(0, 0, 0, card.lifted || card.hover ? 0.45 : 0.35)
            Behavior on offset.y { NumberAnimation { duration: 180 } }
        }

        Rectangle {
            id: frame
            anchors.fill: parent
            radius: card.theme.frameRadius
            color: card.theme.framePaper
        }

        ClippingRectangle {
            x: card.theme.padLeft
            y: card.theme.padTop
            width: card.photo.w
            height: card.photo.h
            radius: card.theme.photoRadius
            color: "transparent"

            Image {
                id: img
                anchors.fill: parent
                source: card.fileUrl
                sourceSize.width: 480
                asynchronous: true
                cache: false
                fillMode: Image.Stretch
                smooth: true
                mipmap: true
            }
        }

        // 已复制
        Rectangle {
            anchors.horizontalCenter: parent.horizontalCenter
            y: parent.height + 10 + (card.copiedShown ? 0 : 4)
            width: copiedText.implicitWidth + 28
            height: 24
            radius: 12
            color: L.Red
            opacity: card.copiedShown ? 1 : 0
            visible: opacity > 0
            Behavior on opacity { NumberAnimation { duration: 160 } }
            Behavior on y { NumberAnimation { duration: 160 } }

            Text {
                id: copiedText
                anchors.centerIn: parent
                text: "已复制"
                color: "white"
                font.pixelSize: 12
                font.family: "HarmonyOS Sans SC"
            }
        }

        MouseArea {
            id: hitArea
            anchors.fill: parent
            hoverEnabled: true
            acceptedButtons: Qt.LeftButton | Qt.RightButton
            cursorShape: card.lifted ? Qt.ClosedHandCursor : Qt.PointingHandCursor

            property point pressAt
            property bool moved: false
            property real lastClick: 0
            property bool held: false

            onPressed: mouse => {
                if (mouse.button === Qt.RightButton) {
                    cardMenu.open();
                    return;
                }
                pressAt = Qt.point(mouse.x, mouse.y);
                moved = false;
                held = false;
                card.pressed = true;
                holdTimer.restart();
            }

            onPositionChanged: mouse => {
                if (!pressed || card.dragging || held) return;
                var dx = mouse.x - pressAt.x;
                var dy = mouse.y - pressAt.y;
                if (!moved && Math.hypot(dx, dy) < 6) return;
                if (!moved) {
                    moved = true;
                    card.pressed = false;
                    clickTimer.stop();
                    holdTimer.stop();
                    // 一开始就往下拖：拖出一份给别的程序；横着拖：沿绳换位置
                    if (dy > 12 && Math.abs(dy) > Math.abs(dx)) {
                        card.startExternalDrag();
                        return;
                    }
                    card.lifted = true;
                    card.host.beginMove(card.entryId);
                }
                if (card.lifted) {
                    var p = mapToItem(card.host.stage, mouse.x, mouse.y);
                    // 拖出绳子区域：改为拖出一份
                    if (p.y > L.PanelHeight + 20) {
                        card.lifted = false;
                        card.host.endMove(false);
                        card.startExternalDrag();
                        return;
                    }
                    var cx = p.x - (pressAt.x - width / 2);
                    var to = card.host.updateMove(card.entryId, cx);
                    card.follow(to.x, to.y);
                }
            }

            onReleased: {
                card.pressed = false;
                holdTimer.stop();
                if (held) { held = false; return; }   // 长按已经打开编辑器，不再算单击
                if (card.lifted) {
                    card.lifted = false;
                    card.host.endMove(true);
                    return;
                }
                if (moved) return;
                var now = Date.now();
                if (now - lastClick < L.ClickDelay) {
                    // 双击：只打开，不复制
                    clickTimer.stop();
                    lastClick = 0;
                    card.host.openFile(card.path);
                    return;
                }
                lastClick = now;
                clickTimer.restart();
            }

            onCanceled: {
                card.pressed = false;
                holdTimer.stop();
                held = false;
                if (card.lifted) { card.lifted = false; card.host.endMove(false); }
            }
        }

        // 长按编辑，同 Windows Layout.HoldToEdit
        Timer {
            id: holdTimer
            interval: L.HoldToEdit
            onTriggered: {
                hitArea.held = true;
                card.pressed = false;
                clickTimer.stop();
                card.host.editFile(card.path);
            }
        }

        Timer {
            id: clickTimer
            interval: L.ClickDelay
            onTriggered: {
                card.host.copyFile(card.path);
                card.showCopied();
            }
        }

        // 取下（悬停时左上角红叉）
        Rectangle {
            id: cross
            x: -8
            y: -8
            width: 22
            height: 22
            radius: 11
            color: L.Red
            readonly property bool shown: card.hover && !card.dragging && !card.pressed && !card.falling && !card.lifted
            opacity: shown ? 1 : 0
            scale: shown ? 1 : 0.6
            visible: opacity > 0
            Behavior on opacity { NumberAnimation { duration: 120 } }
            Behavior on scale { NumberAnimation { duration: 120 } }

            Text {
                anchors.centerIn: parent
                anchors.verticalCenterOffset: -1
                text: "×"
                color: "white"
                font.pixelSize: 15
                font.bold: true
            }

            MouseArea {
                id: crossArea
                anchors.fill: parent
                anchors.margins: -3
                hoverEnabled: true
                enabled: cross.shown
                cursorShape: Qt.PointingHandCursor
                onClicked: card.host.discard(card.entryId)
            }
        }

        // 收藏角标（PeggedView：右上角，白描边，不接收鼠标；收藏从右键菜单进）
        Canvas {
            visible: card.favorite
            x: parent.width - 10
            y: -7
            width: 17
            height: 17
            z: 2
            onPaint: {
                var ctx = getContext("2d");
                ctx.reset();
                var pts = [[12, 1], [14.586, 8.44], [22.462, 8.601], [16.185, 13.36], [18.466, 20.899],
                           [12, 16.4], [5.534, 20.899], [7.815, 13.36], [1.538, 8.601], [9.414, 8.44]];
                function star(dx, dy, fill, stroke, sw) {
                    ctx.save();
                    ctx.translate(dx, dy);
                    ctx.scale(width / 24, height / 24);
                    ctx.beginPath();
                    ctx.moveTo(pts[0][0], pts[0][1]);
                    for (var i = 1; i < pts.length; i++) ctx.lineTo(pts[i][0], pts[i][1]);
                    ctx.closePath();
                    ctx.fillStyle = fill;
                    ctx.fill();
                    if (stroke !== "") {
                        ctx.strokeStyle = stroke;
                        ctx.lineWidth = sw;
                        ctx.lineJoin = "round";
                        ctx.stroke();
                    }
                    ctx.restore();
                }
                ctx.globalAlpha = 0.18;
                star(0, 1, "black", "black", 2.4 * 24 / width);
                ctx.globalAlpha = 1;
                star(0, 0, L.Red, "white", 1.8 * 24 / width);
            }
            Component.onCompleted: requestPaint()
        }
    }

    Native.Menu {
        id: cardMenu
        onAboutToShow: card.host.menuShown()
        onAboutToHide: card.host.menuHidden()

        Native.MenuItem { text: "打开图库"; onTriggered: card.host.openGallery() }
        Native.MenuSeparator {}
        Native.MenuItem { text: "复制"; onTriggered: card.host.copyFile(card.path) }
        Native.MenuItem { text: "打开"; onTriggered: card.host.openFile(card.path) }
        Native.MenuItem { text: "编辑"; onTriggered: card.host.editFile(card.path) }
        Native.MenuItem {
            text: card.favorite ? "取消收藏" : "收藏"
            onTriggered: card.host.setFavorite(card.path, !card.favorite)
        }
        Native.MenuItem { text: "在文件管理器中显示"; onTriggered: card.host.revealFile(card.path) }
        Native.MenuSeparator {}
        Native.MenuItem { text: "取下"; onTriggered: card.host.discard(card.entryId) }
    }

    // ---- 拖出一份：给别的程序一个文件（只复制，照片留在绳上） ----
    function startExternalDrag() {
        dragging = true;
        dragProxy.Drag.active = true;
    }

    Item {
        id: dragProxy
        width: 1
        height: 1
        Drag.dragType: Drag.Automatic
        Drag.supportedActions: Qt.CopyAction
        Drag.proposedAction: Qt.CopyAction
        Drag.mimeData: ({ "text/uri-list": card.fileUrl + "\r\n" })
        Drag.imageSource: card.fileUrl
        Drag.imageSourceSize: Qt.size(card.photo.w, card.photo.h)
        Drag.onDragFinished: dropAction => {
            dragProxy.Drag.active = false;
            card.dragging = false;
            card.rehang();
        }
    }
}
