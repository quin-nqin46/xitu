import QtQuick
import Qt.labs.platform as Native
import Quickshell
import Quickshell.Wayland
import "layout.js" as L
import "fonts.js" as F

// 一块屏上的绳子（LineWindow.cs + LineView.cs）。挂在顶栏下面：
// layer-shell 窗口不占位（exclusiveZone 0），合成器自动把它放到顶栏的保留区下方。
// 平时整块点击穿透，只有展开后照片那一段接收鼠标。
PanelWindow {
    id: win

    required property var modelData
    required property var shell      // shell.qml 根对象

    screen: modelData
    anchors.top: true
    anchors.left: true
    anchors.right: true
    implicitHeight: L.PanelHeight + L.FallDistance + 40
    exclusionMode: ExclusionMode.Normal
    exclusiveZone: 0
    color: "transparent"
    focusable: false
    surfaceFormat.opaque: false
    visible: revealed || stage.y > -L.PanelHeight - 19 || shell.fallingCount > 0

    WlrLayershell.layer: WlrLayer.Overlay
    WlrLayershell.namespace: "xitu-rope"
    WlrLayershell.keyboardFocus: WlrKeyboardFocus.None

    readonly property string monitorName: modelData ? modelData.name : ""
    readonly property real ropeWidth: width
    property alias stage: stage
    property bool revealed: false
    property bool pinned: false        // 快捷键打开：等鼠标进来再离开才收
    property bool everHovered: false
    property bool externalDrag: false  // 别的程序拖着东西悬在绳子或顶边上
    property bool dropHold: false      // 刚拖入，先别收，让照片落下来
    property int menuOpen: 0           // 右键菜单（含子菜单）还开着的层数。开着时不许收起
    property var themeItems: []

    // ---- 位置计算（Card 通过 host 调用） ----
    property int localVersion: 0
    readonly property int layoutVersion: shell.version + localVersion
    property string movingId: ""
    property int movingOrigSlot: -1
    property int movingIndex: -1

    function liveCount() {
        var n = 0;
        for (var i = 0; i < shell.line.count; i++)
            if (!shell.line.get(i).gone) n++;
        return n;
    }

    function baseSlot(id) {
        var n = 0;
        for (var i = 0; i < shell.line.count; i++) {
            var e = shell.line.get(i);
            if (e.gone) continue;
            if (e.entryId === id) return n;
            n++;
        }
        return -1;
    }

    function slotOf(id) {
        var base = baseSlot(id);
        if (base < 0 || movingId === "" || id === movingId) return base;
        var other = base - (base > movingOrigSlot ? 1 : 0);
        return other < movingIndex ? other : other + 1;
    }

    function beginMove(id) {
        movingId = id;
        movingOrigSlot = baseSlot(id);
        movingIndex = movingOrigSlot;
        localVersion++;
    }

    // cx：照片中心在 stage 里的横坐标。返回被拖照片应跟到的位置。
    function updateMove(id, cx) {
        var count = liveCount();
        var first = L.slotX(0, count, ropeWidth);
        var last = L.slotX(count - 1, count, ropeWidth);
        var index = Math.max(0, Math.min(count - 1, Math.round((cx - first) / L.Spacing)));
        if (index !== movingIndex) { movingIndex = index; localVersion++; }
        cx = Math.max(first - L.Spacing * 0.6, Math.min(last + L.Spacing * 0.6, cx));
        return Qt.point(cx - L.CardWidth / 2, L.ropeY(cx, ropeWidth) - shell.theme.clipAbove);
    }

    function endMove(commit) {
        var id = movingId;
        var to = movingIndex;
        movingId = "";
        movingOrigSlot = -1;
        movingIndex = -1;
        if (commit && id !== "") shell.moveEntry(id, to);
        localVersion++;
    }

    function copyFile(path) { shell.copyFile(path); }
    function openFile(path) { shell.openFile(path); }
    function editFile(path) { shell.editFile(path); }
    function saveAs(path) { shell.saveAs(path); }
    function discard(id) { shell.discard(id); }
    function setFavorite(path, on) { shell.setFavorite(path, on); }
    function revealFile(path) { shell.revealFile(path); }
    function openLibrary() { shell.openLibrary(); }
    // 图库盖在屏幕中间，绳子留着会叠上去，所以打开后马上收起。
    function openGallery() {
        shell.showGallery(modelData);
        retract();
    }
    function setTheme(id) { shell.theme.setTheme(id); }

    // 四个内置主题写在菜单里。用户目录里多出来的主题再补到子菜单末尾。
    function syncThemes() {
        var old = themeItems;
        var known = {
            "polaroid-blue": true, "red-rope": true, "graphite": true, "linen": true
        };
        var made = [];
        var lines = shell.theme.themeLines.split("\n");
        for (var i = 0; i < lines.length; i++) {
            var line = lines[i].trim();
            if (line === "") continue;
            var sp = line.indexOf(" ");
            var tid = sp < 0 ? line : line.substring(0, sp);
            if (known[tid]) continue;
            var name = sp < 0 ? line : line.substring(sp + 1);
            var it = themeItemComp.createObject(win, { text: name, tid: tid });
            if (!it) continue;
            it.checked = tid === shell.theme.themeId;
            themeMenu.addItem(it);
            made.push(it);
        }
        themeItems = made;
        for (var j = 0; j < old.length; j++) {
            themeMenu.removeItem(old[j]);
            old[j].destroy();
        }
    }

    // 菜单弹窗在绳子窗口外面，指针移进去会被当成离开照片。计数挡住所有收起路径。
    function menuShown() {
        menuOpen++;
        retractTimer.stop();
        peekTimer.stop();
    }

    function menuHidden() {
        menuOpen = Math.max(0, menuOpen - 1);
        if (menuOpen > 0 || !revealed) return;
        if (hover.hovered) return;
        if (pinned && !everHovered) return;
        dropHold = false;
        retractTimer.restart();
    }

    // ---- 展开 / 收起 ----
    // 只有真全屏（看视频、玩游戏）才不展开；最大化不算。判断按桌面分后端，见 Desktop.qml。
    function fullscreenHere() { return shell.desktop.fullscreenOn(win.modelData); }

    // 可能有全屏时先让后端刷新（Hyprland 的窗口状态可能是旧的），150ms 后再确认
    property bool pendingPin: false
    Timer {
        id: fullscreenCheck
        interval: 150
        onTriggered: if (!win.fullscreenHere()) win.doReveal(win.pendingPin)
    }

    function reveal(pin) {
        if (!revealed && shell.desktop.maybeFullscreen(win.modelData)) {
            pendingPin = !!pin;
            shell.desktop.refresh();
            fullscreenCheck.restart();
            return;
        }
        doReveal(pin);
    }

    function doReveal(pin) {
        pinned = !!pin;
        everHovered = false;
        retractTimer.stop();
        if (!revealed) {
            revealed = true;
            swayTimer.restart();
        }
    }

    function retract() {
        revealed = false;
        pinned = false;
        menuOpen = 0;
        peekTimer.stop();
        retractTimer.stop();
    }

    function toggle() { if (revealed) retract(); else reveal(true); }

    // ---- 截图飞上绳子（同 Windows CaptureFlight）：从框选处飞到新卡片的位置，落下后再挂上 ----
    // 全屏时不飞也不弹（同 Windows），返回 false 让 core 直接挂
    function flyIn(shot) {
        if (fullscreenHere()) return false;
        // 上一张还在飞：先让它落地挂上
        if (flyAnim.running) { flyAnim.stop(); flight.land(); }
        var s = win.modelData;
        var count = liveCount();
        var n = Math.min(count + 1, shell.capacity);
        var index = n - 1;
        var tx = L.slotX(index, n, win.width);
        var photo = L.photoSize(shot.w, shot.h);
        var fw = photo.w + shell.theme.padLeft + shell.theme.padRight;
        var fh = photo.h + shell.theme.padTop + shell.theme.padBottom;
        flight.path = shot.path;
        flight.fromX = shot.x - s.x;
        flight.fromY = shot.y - s.y;
        flight.fromW = shot.w;
        flight.fromH = shot.h;
        flight.toX = tx - fw / 2;
        flight.toY = L.ropeY(tx, win.width) + L.PinHeight - 12 - L.PinAbove;
        flight.toW = fw;
        flight.toH = fh;
        flight.tilt = Math.random() * 5 - 2.5;
        peek();
        flight.start();
        return true;
    }

    function peek() {
        if (revealed) return;
        reveal(false);
        peekTimer.restart();
    }

    function sway() {
        for (var i = 0; i < cards.count; i++) {
            var c = cards.itemAt(i);
            if (!c || c.gone) continue;
            var deg = (1.2 + Math.random() * 1.2) * (Math.random() < 0.5 ? -1 : 1);
            Qt.callLater(c.nudge, deg);
        }
    }

    function gust() {
        if (!revealed || movingId !== "") return;
        for (var i = 0; i < cards.count; i++) {
            var c = cards.itemAt(i);
            if (c && !c.gone) c.nudge(1.6 + Math.random() * 1.8);
        }
    }

    Timer { id: swayTimer; interval: L.RevealMs * 0.6; onTriggered: win.sway() }
    Timer {
        id: peekTimer
        interval: L.PeekDuration
        onTriggered: {
            if (win.menuOpen > 0) return;
            win.dropHold = false;
            if (!hover.hovered && win.movingId === "" && !win.externalDrag && !win.anyDragging()) win.retract();
        }
    }
    Timer {
        id: retractTimer
        interval: L.RetractDelay
        onTriggered: {
            if (win.menuOpen > 0) return;
            if (win.dropHold || win.externalDrag) return;
            if (!hover.hovered && win.movingId === "" && !win.anyDragging()) win.retract();
        }
    }
    // 从顶边拖到绳子要穿过顶栏，这会儿两边都不含拖放。离开一小段时间再收。
    Timer {
        id: dragGrace
        interval: L.RetractDelay
        onTriggered: {
            if (edgeDrop.containsDrag || ropeDrop.containsDrag) return;
            win.externalDrag = false;
            if (win.dropHold && !peekTimer.running) peekTimer.restart();
            win.maybeRetract();
        }
    }

    function anyDragging() {
        for (var i = 0; i < cards.count; i++) {
            var c = cards.itemAt(i);
            if (c && c.dragging) return true;
        }
        return false;
    }

    function noteDrag(active) {
        if (active) {
            if (!externalDrag && !anyDragging()) shell.log("有东西拖到绳子上");
            externalDrag = true;
            dragGrace.stop();
            retractTimer.stop();
            peekTimer.stop();
        } else {
            dragGrace.restart();
        }
    }

    function maybeRetract() {
        if (menuOpen > 0) return;
        if (dropHold || externalDrag || hover.hovered || movingId !== "" || anyDragging()) return;
        if (!revealed || (pinned && !everHovered)) return;
        retractTimer.restart();
    }

    // 拖入成功后保持展开一会儿（Windows 版 HoldRevealed）。快捷键钉住的不另计时。
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

    function urlsOf(drop) {
        var out = [];
        if (drop.hasUrls && drop.urls) {
            for (var i = 0; i < drop.urls.length; i++) out.push(String(drop.urls[i]));
        }
        try {
            if (out.length === 0 && drop.formats && drop.getDataAsString) {
                for (var j = 0; j < drop.formats.length; j++) {
                    var f = String(drop.formats[j]).toLowerCase();
                    if (f !== "text/uri-list" && f !== "text/x-moz-url" && f !== "text/plain") continue;
                    var text = drop.getDataAsString(String(drop.formats[j]));
                    if (!text) continue;
                    if (f === "text/x-moz-url") {
                        var first = text.split("\n")[0].replace(/\0/g, "").trim();
                        if (first !== "") out.push(first);
                    } else {
                        var lines = text.split(/\r?\n/);
                        for (var k = 0; k < lines.length; k++) {
                            var line = lines[k].trim();
                            if (line !== "" && line.charAt(0) !== "#") out.push(line);
                        }
                    }
                }
            }
        } catch (e) { shell.log("读拖放地址失败 " + e); }
        if (out.length === 0 && drop.hasText && drop.text) {
            var t = String(drop.text).trim();
            if (/^(file:|https?:|data:image\/)/i.test(t)) out.push(t);
        }
        return out;
    }

    function imageB64(drop) {
        if (!drop.formats || !drop.getDataAsArrayBuffer) return "";
        var prefer = ["image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp", "image/bmp", "image/tiff"];
        var found = "";
        var i;
        for (i = 0; i < drop.formats.length; i++) {
            if (prefer.indexOf(String(drop.formats[i]).toLowerCase()) >= 0) {
                found = String(drop.formats[i]);
                break;
            }
        }
        if (found === "") {
            for (i = 0; i < drop.formats.length; i++) {
                var g = String(drop.formats[i]).toLowerCase();
                if (g.indexOf("image/") === 0) { found = String(drop.formats[i]); break; }
            }
        }
        if (found === "") return "";
        try {
            var buf = drop.getDataAsArrayBuffer(found);
            if (!buf || !(buf.byteLength > 0)) return "";
            if (buf.byteLength > 30 * 1024 * 1024) {
                shell.log("拖入图片超过 30 MB");
                return "";
            }
            return L.base64(buf);
        } catch (e) {
            shell.log("读拖放图像失败 " + e);
            return "";
        }
    }

    function takeDrop(drop) {
        if (drop && drop.acceptProposedAction) drop.acceptProposedAction();
        var urls = urlsOf(drop);
        var b64 = imageB64(drop);
        if (shell.ingestDrop(urls, b64)) holdOpen();
        else shell.log("拖进来的内容里没有可用的图片，格式：" + formatsOf(drop));
        noteDrag(false);
    }

    // ---- 接收鼠标的区域 ----
    mask: Region { item: hitBox }

    Item {
        id: hitBox
        readonly property int count: win.layoutVersion, win.liveCount()
        readonly property real leftEdge: count > 0 ? L.slotX(0, count, win.ropeWidth) - L.CardWidth / 2 - 14 : win.ropeWidth / 2 - 160
        readonly property real rightEdge: count > 0 ? L.slotX(count - 1, count, win.ropeWidth) + L.CardWidth / 2 + 14 : win.ropeWidth / 2 + 160
        readonly property bool wide: win.movingId !== "" || win.anyDragging() || win.externalDrag
        x: !win.revealed ? 0 : wide ? 0 : leftEdge
        y: 0
        width: !win.revealed ? 0 : wide ? win.width : rightEdge - leftEdge
        height: !win.revealed ? 0 : wide ? win.height : L.PanelHeight
    }

    PanelWindow {
        id: flight
        property string path: ""        // 还没挂上的那张；落地后清空
        property string imagePath: ""   // 飞行和淡出时显示的图
        property real fromX: 0
        property real fromY: 0
        property real fromW: 1
        property real fromH: 1
        property real toX: 0
        property real toY: 0
        property real toW: 1
        property real toH: 1
        property real tilt: 0
        property real t: 0

        function start() {
            t = 0;
            imagePath = path;
            flyLayer.opacity = 1;
            visible = true;
            flyAnim.restart();
        }
        function land() {
            if (path === "") return;
            var p = path;
            path = "";
            win.shell.hang(p, false);
            fadeAnim.restart();
        }
        function ease(x) { return x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2; }
        function smooth(x, a, b) { var u = Math.max(0, Math.min(1, (x - a) / (b - a))); return u * u * (3 - 2 * u); }
        function lerp(a, b, k) { return a + (b - a) * k; }

        readonly property real k: ease(t)
        readonly property real chrome: smooth(k, 0.35, 1)

        screen: win.modelData
        visible: false
        color: "transparent"
        anchors.top: true
        anchors.left: true
        anchors.right: true
        anchors.bottom: true
        exclusionMode: ExclusionMode.Ignore
        focusable: false
        mask: Region {}
        WlrLayershell.layer: WlrLayer.Overlay
        WlrLayershell.namespace: "xitu-flight"
        WlrLayershell.keyboardFocus: WlrKeyboardFocus.None

        NumberAnimation {
            id: flyAnim
            target: flight
            property: "t"
            from: 0
            to: 1
            duration: L.FlightSeconds * 1000
            onFinished: flight.land()
        }
        NumberAnimation {
            id: fadeAnim
            target: flyLayer
            property: "opacity"
            from: 1
            to: 0
            duration: 160
            onFinished: { flight.visible = false; flight.imagePath = ""; }
        }

        Item {
            id: flyLayer
            anchors.fill: parent

        Rectangle {
            readonly property real w: flight.lerp(flight.fromW, flight.toW, flight.k)
            readonly property real h: flight.lerp(flight.fromH, flight.toH, flight.k)
            x: flight.lerp(flight.fromX + flight.fromW / 2, flight.toX + flight.toW / 2, flight.k) - w / 2
            y: flight.lerp(flight.fromY, flight.toY, flight.k) - Math.sin(Math.PI * flight.k) * L.FlightArc
            width: Math.max(1, w)
            height: Math.max(1, h)
            radius: win.shell.theme.frameRadius * flight.chrome
            color: Qt.rgba(win.shell.theme.framePaper.r, win.shell.theme.framePaper.g, win.shell.theme.framePaper.b, flight.chrome)
            transformOrigin: Item.Top
            rotation: flight.tilt * flight.k

            Image {
                x: win.shell.theme.padLeft * flight.chrome
                y: win.shell.theme.padTop * flight.chrome
                width: parent.width - (win.shell.theme.padLeft + win.shell.theme.padRight) * flight.chrome
                height: parent.height - (win.shell.theme.padTop + win.shell.theme.padBottom) * flight.chrome
                source: flight.imagePath !== "" ? "file://" + flight.imagePath : ""
                sourceSize.width: 960
                asynchronous: false
                fillMode: Image.Stretch
                smooth: true
            }
        }
        }
    }

    // 顶边感应：贴着屏幕最上沿的一条 2 像素窄带（盖在顶栏上沿，顶栏的胶囊离顶边还有十来像素，不影响点击）。
    // 只在中间挂照片的那一段（L.triggerSpan，同 Windows TriggerZone），两边的工作区按钮、托盘、标题栏碰了不会展开。
    // 指针在这里停 0.3 秒就展开。
    PanelWindow {
        id: edge
        readonly property var span: L.triggerSpan(win.layoutVersion >= 0 ? win.liveCount() : 0, win.modelData.width)
        screen: win.modelData
        anchors.top: true
        anchors.left: true
        margins.left: Math.round(span.left)
        implicitWidth: Math.max(1, Math.round(span.right - span.left))
        implicitHeight: 2
        exclusionMode: ExclusionMode.Ignore
        color: "transparent"
        focusable: false
        WlrLayershell.layer: WlrLayer.Overlay
        WlrLayershell.namespace: "xitu-edge"
        WlrLayershell.keyboardFocus: WlrKeyboardFocus.None

        // 拖着东西停在这条 2 像素上，和鼠标悬停一样 0.3 秒展开。
        // 悬停挂在 DropArea 上，免得再盖一层 MouseArea 把拖放挡住。
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
                        win.shell.desktop.noteScreen(win.modelData);
                        edgeTimer.restart();
                        retractTimer.stop();
                    } else {
                        edgeTimer.stop();
                        if (win.menuOpen > 0) return;
                        // 从顶边往下走到绳子要穿过顶栏；0.5 秒内没走到照片上就收起
                        if (win.revealed && !win.pinned && !hover.hovered) retractTimer.restart();
                    }
                }
            }
        }

        Timer {
            id: edgeTimer
            interval: L.RevealDelay
            onTriggered: if (edgeHover.hovered || edgeDrop.containsDrag) win.reveal(false)
        }
    }

    // 包住照片：拖放顺着父项往上找，才能落在照片上。输入区域仍由 hitBox 决定，
    // 拖入期间 hitBox 拉到整窗，平时只有照片那一段。
    DropArea {
        id: ropeDrop
        anchors.fill: parent
        onEntered: win.noteDrag(true)
        onExited: win.noteDrag(false)
        onDropped: drop => win.takeDrop(drop)

    Item {
        id: stage
        width: win.width
        height: win.height
        y: win.revealed ? 0 : -L.PanelHeight - 20
        Behavior on y {
            NumberAnimation {
                duration: win.revealed ? L.RevealMs : L.HideMs
                easing.type: win.revealed ? Easing.OutCubic : Easing.InCubic
            }
        }

        // 挂在 stage（照片的父项）上：鼠标在照片上也算悬停。mask 之外收不到事件，等于只管照片那一段。
        HoverHandler {
            id: hover
            onHoveredChanged: {
                if (hovered) {
                    win.shell.desktop.noteScreen(win.modelData);
                    win.everHovered = true;
                    retractTimer.stop();
                    peekTimer.stop();
                } else if (win.menuOpen > 0) {
                    return;
                } else if (win.revealed && (!win.pinned || win.everHovered)) {
                    // 指针已经到过照片上，拖入后的停留到此为止，离开就按平时收起
                    win.dropHold = false;
                    retractTimer.restart();
                }
            }
        }

        Rope {
            width: parent.width
            height: L.PanelHeight
            theme: win.shell.theme
        }

        // 绳上没照片时的提示
        Rectangle {
            visible: hitBox.count === 0
            anchors.horizontalCenter: parent.horizontalCenter
            y: L.ropeY(win.ropeWidth / 2, win.ropeWidth) + 36
            width: hintText.implicitWidth + 24
            height: 28
            radius: 14
            color: win.shell.theme.hintBg
            border.width: win.shell.theme.hintBorderOn ? 1 : 0
            border.color: win.shell.theme.hintBorder

            Text {
                id: hintText
                anchors.centerIn: parent
                text: "截图会挂在这里"
                color: win.shell.theme.hintFg
                font.pixelSize: 12
                font.family: F.ui
            }
        }

        // 照片之间、绳子露出来的那段、空绳提示。照片自己的 MouseArea 在上面，右键仍走照片菜单。
        // 收得到事件的范围就是外面的 hitBox，不把 mask 拉成整窗。
        MouseArea {
            id: blankHit
            anchors.fill: parent
            z: -1
            acceptedButtons: Qt.RightButton
            onPressed: mouse => {
                if (mouse.button === Qt.RightButton) blankMenu.open();
            }
        }

        Repeater {
            id: cards
            model: win.shell.line

            delegate: Card {
                host: win
            }
        }
    }
    }

    Native.Menu {
        id: blankMenu
        onAboutToShow: win.menuShown()
        onAboutToHide: win.menuHidden()

        Native.MenuItem { text: "打开图库"; onTriggered: win.openGallery() }
        Native.MenuItem { text: "打开图库文件夹"; onTriggered: win.openLibrary() }
        Native.MenuItem { text: "设置…"; onTriggered: win.shell.openSettings() }
        Native.MenuItem { text: "清空绳子"; enabled: win.shell.version >= 0 && win.shell.livePaths().length > 0; onTriggered: win.shell.clear() }
        Native.Menu {
            id: themeMenu
            title: "换主题"
            onAboutToShow: {
                var items = win.themeItems;
                for (var i = 0; i < items.length; i++)
                    items[i].checked = items[i].tid === win.shell.theme.themeId;
                win.menuShown();
            }
            onAboutToHide: win.menuHidden()

            Native.MenuItem {
                text: "主题蓝绳 + 拍立得"
                checkable: true
                checked: win.shell.theme.themeId === "polaroid-blue"
                onTriggered: win.setTheme("polaroid-blue")
            }
            Native.MenuItem {
                text: "红绳"
                checkable: true
                checked: win.shell.theme.themeId === "red-rope"
                onTriggered: win.setTheme("red-rope")
            }
            Native.MenuItem {
                text: "石墨细线"
                checkable: true
                checked: win.shell.theme.themeId === "graphite"
                onTriggered: win.setTheme("graphite")
            }
            Native.MenuItem {
                text: "柔和麻绳"
                checkable: true
                checked: win.shell.theme.themeId === "linen"
                onTriggered: win.setTheme("linen")
            }
        }
    }

    Component {
        id: themeItemComp
        Native.MenuItem {
            property string tid: ""
            checkable: true
            onTriggered: win.shell.theme.setTheme(tid)
        }
    }

    Connections {
        target: win.shell.theme
        function onThemeLinesChanged() { win.syncThemes(); }
    }
    Component.onCompleted: win.syncThemes()
}
