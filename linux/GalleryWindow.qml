import QtQuick
import QtQuick.Controls
import QtQuick.Effects
import Quickshell
import Quickshell.Io
import Quickshell.Wayland
import "fonts.js" as F

// 图库内容照 GalleryWindow.cs。承载是聚焦屏上的 layer-shell 浮层：
// 同一个窗口铺满整屏，面板以外的透明区域接收点击就关。namespace xitu-gallery。
PanelWindow {
    id: win

    required property var shell

    property var hostScreen: Quickshell.screens.length > 0 ? Quickshell.screens[0] : null
    screen: hostScreen
    visible: false
    color: "transparent"
    aboveWindows: true
    focusable: visible
    exclusiveZone: -1
    exclusionMode: ExclusionMode.Ignore
    surfaceFormat.opaque: false

    anchors.top: true
    anchors.left: true
    anchors.right: true
    anchors.bottom: true

    WlrLayershell.layer: WlrLayer.Overlay
    WlrLayershell.namespace: "xitu-gallery"
    // Exclusive：一映射就拿到键盘，打开后立刻能按 Esc。隐藏时改回 None，焦点还给原来的窗口。
    // OnDemand 要等点到面板才有键盘焦点，刚打开就按 Esc 会落到下面的程序。
    WlrLayershell.keyboardFocus: visible ? WlrKeyboardFocus.Exclusive : WlrKeyboardFocus.None

    property bool open: false
    property bool suppressHide: false
    property bool inputTight: false
    property bool ignoreOutside: false
    property real panelScale: 0.96
    property real panelOpacity: 0
    property bool menuOpen: false
    property real menuX: 8
    property real menuY: 8
    property bool confirmOpen: false
    property string confirmText: ""

    readonly property real panelW: {
        var sw = screen ? screen.width : 1280;
        var w = Math.max(720, sw * 0.62);
        return Math.min(w, Math.max(1, sw));
    }
    readonly property real panelH: {
        var sh = screen ? screen.height : 800;
        var h = Math.max(480, sh * 0.72);
        return Math.min(h, Math.max(1, sh));
    }

    mask: Region { item: win.inputTight ? panel : scrim }

    property var items: []
    property var rows: []
    property var order: []
    property var selected: ({})
    property int selectionRev: 0
    property string anchor: ""
    property string filterKey: "all"
    property int colCount: 1
    property int scanRev: 0
    property bool loaded: false
    property bool scanAgain: false
    property real savedY: 0
    property string retention: "30 天"
    property int occupied: 0
    property string capture: "Ctrl+Shift+A"
    property string favLabel: "收藏"
    property int menuCount: 0
    property var pendingDelete: []

    readonly property var filterList: [
        { "key": "all", "label": "全部" },
        { "key": "capture", "label": "截图" },
        { "key": "clip", "label": "剪贴板" },
        { "key": "drop", "label": "拖入" },
        { "key": "file", "label": "文件" },
        { "key": "fav", "label": "收藏" },
    ]
    readonly property int filterRowH: 36

    function focusedScreen() {
        return shell.desktop.focusedScreen();
    }

    // 没开就在聚焦屏打开；同一块屏上已开就关。开在别的屏上时，卸掉旧的再在聚焦屏打开。
    function toggle() {
        var s = focusedScreen();
        if (!s) return;
        var same = hostScreen && hostScreen.name === s.name;
        if (open && same) {
            requestClose("toggle");
            return;
        }
        if (visible && !same) {
            suppressHide = true;
            closeAnim.stop();
            openAnim.stop();
            suppressHide = false;
            visible = false;
            open = false;
        }
        hostScreen = s;
        reveal();
    }

    // 只打开：已在这块屏上开着就留着，不切换成关闭。右键菜单走这里。
    function showOn(screen) {
        var s = screen || focusedScreen();
        if (!s) return;
        var same = hostScreen && hostScreen.name === s.name;
        if (open && same) return;
        if (visible && !same) {
            suppressHide = true;
            closeAnim.stop();
            openAnim.stop();
            suppressHide = false;
            visible = false;
            open = false;
        }
        hostScreen = s;
        reveal();
    }

    function reveal() {
        suppressHide = true;
        closeAnim.stop();
        openAnim.stop();
        suppressHide = false;
        open = true;
        menuOpen = false;
        confirmOpen = false;
        inputTight = false;
        ignoreOutside = false;
        panelScale = 0.96;
        panelOpacity = 0;
        visible = true;
        openAnim.restart();
        reload();
        Qt.callLater(function() { page.forceActiveFocus(); });
        shell.log("图库打开于 " + (hostScreen ? hostScreen.name : "?"));
    }

    property string closeWhy: ""
    function requestClose(why) {
        if (!visible || !open) return;
        closeWhy = why || "";
        open = false;
        menuOpen = false;
        confirmOpen = false;
        openAnim.stop();
        closeAnim.restart();
    }

    function finishHide() {
        if (open || !visible) return;
        visible = false;
        inputTight = false;
        menuOpen = false;
        confirmOpen = false;
        panelScale = 0.96;
        panelOpacity = 0;
        shell.log("图库关闭" + (closeWhy !== "" ? " " + closeWhy : ""));
        closeWhy = "";
    }

    property real escAt: 0
    function handleEsc() {
        if (!visible) return;
        var now = Date.now();
        if (now - escAt < 80) return;
        escAt = now;
        if (menuOpen) { menuOpen = false; return; }
        if (confirmOpen) { confirmOpen = false; return; }
        requestClose("esc");
    }

    function endDrag() {
        dragProxy.Drag.active = false;
        inputTight = false;
        ignoreOutside = true;
        outsideGuard.restart();
    }

    function reload() {
        savedY = listView.contentY;
        if (scanProc.running) {
            scanAgain = true;
            return;
        }
        scanProc.running = true;
    }

    function fileUrl(path) {
        return "file://" + encodeURIComponent(path).replace(/%2F/g, "/");
    }

    function formatSize(bytes) {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return Math.round(bytes / 1024) + " KB";
        return Math.round(bytes / (1024 * 1024)) + " MB";
    }

    function sourceOf(key) {
        if (key === "capture") return "截图";
        if (key === "clip") return "剪贴板";
        if (key === "drop") return "拖入";
        if (key === "file") return "文件";
        return "";
    }

    function countOf(key) {
        scanRev;
        var list = items;
        if (key === "all") return list.length;
        var n = 0;
        var src = sourceOf(key);
        for (var i = 0; i < list.length; i++) {
            if (key === "fav") {
                if (list[i].favorite) n++;
            } else if (list[i].source === src) n++;
        }
        return n;
    }

    function filterIndex() {
        for (var i = 0; i < filterList.length; i++)
            if (filterList[i].key === filterKey) return i;
        return 0;
    }

    function dateTitle(day) {
        var p = day.split("-");
        var date = new Date(Number(p[0]), Number(p[1]) - 1, Number(p[2]));
        var today = new Date();
        today.setHours(0, 0, 0, 0);
        var yest = new Date(today.getTime());
        yest.setDate(yest.getDate() - 1);
        if (date.getTime() === today.getTime()) return "今天";
        if (date.getTime() === yest.getTime()) return "昨天";
        var week = "日一二三四五六"[date.getDay()];
        var core = (date.getMonth() + 1) + "月" + date.getDate() + "日 周" + week;
        if (date.getFullYear() !== today.getFullYear()) return date.getFullYear() + "年" + core;
        return core;
    }

    function currentItems() {
        var src = sourceOf(filterKey);
        var out = [];
        for (var i = 0; i < items.length; i++) {
            var it = items[i];
            if (filterKey === "all") out.push(it);
            else if (filterKey === "fav") { if (it.favorite) out.push(it); }
            else if (it.source === src) out.push(it);
        }
        return out;
    }

    function refreshCols() {
        var inner = Math.max(1, listView.width - 24);
        var n = Math.max(1, Math.floor(inner / 184));
        if (n !== colCount) colCount = n;
    }

    // 一天一组，组里再按列切成行，ListView 只建看得见的行。payload 用字符串，避免嵌套数组过 QML 模型时丢字段。
    function rebuildRows(y) {
        var list = currentItems();
        var cols = Math.max(1, colCount);
        var out = [];
        var bucket = [];
        var day = "";
        function flush() {
            var i = 0;
            while (i < bucket.length) {
                out.push({ kind: "cards", payload: JSON.stringify(bucket.slice(i, i + cols)) });
                i += cols;
            }
            bucket = [];
        }
        for (var j = 0; j < list.length; j++) {
            var d = String(list[j].time).slice(0, 10);
            if (d !== day) {
                flush();
                day = d;
                out.push({ kind: "header", text: dateTitle(d) });
            }
            bucket.push(list[j]);
        }
        flush();
        var paths = [];
        for (var k = 0; k < list.length; k++) paths.push(list[k].path);
        order = paths;
        rows = out;
        pruneSelection();
        Qt.callLater(function() { listView.contentY = y; });
    }

    function applyScan(text) {
        var doc;
        try { doc = JSON.parse(text); }
        catch (e) {
            shell.log("图库列表解析失败");
            return;
        }
        items = doc.items || [];
        retention = doc.retention || retention;
        occupied = doc.occupied || 0;
        capture = doc.capture || capture;
        loaded = true;
        scanRev++;
        shell.log("图库 " + items.length + " 张");
        rebuildRows(savedY);
    }

    function switchFilter(key) {
        if (filterKey === key) return;
        filterKey = key;
        rebuildRows(0);
    }

    function isSelected(path) {
        selectionRev;
        return !!selected[path];
    }

    function isFav(path) {
        for (var i = 0; i < items.length; i++)
            if (items[i].path === path) return !!items[i].favorite;
        return false;
    }

    function selectedInOrder() {
        var out = [];
        for (var i = 0; i < order.length; i++)
            if (selected[order[i]]) out.push(order[i]);
        return out;
    }

    function pruneSelection() {
        var keep = {};
        for (var i = 0; i < order.length; i++)
            if (selected[order[i]]) keep[order[i]] = true;
        selected = keep;
        selectionRev++;
    }

    function selectOnly(path) {
        var o = {};
        o[path] = true;
        selected = o;
        anchor = path;
        selectionRev++;
    }

    function selectRange(from, to) {
        var a = order.indexOf(from);
        var b = order.indexOf(to);
        if (a < 0 || b < 0) { selectOnly(to); return; }
        if (a > b) { var t = a; a = b; b = t; }
        var o = {};
        for (var i = a; i <= b; i++) o[order[i]] = true;
        selected = o;
        selectionRev++;
    }

    function selectFromClick(path, mods) {
        var ctrl = (mods & Qt.ControlModifier) && !(mods & Qt.AltModifier);
        var shift = (mods & Qt.ShiftModifier) && !(mods & Qt.AltModifier);
        if (ctrl && !shift) {
            var next = {};
            for (var k in selected) next[k] = true;
            if (next[path]) delete next[path];
            else next[path] = true;
            selected = next;
            anchor = path;
            selectionRev++;
        } else if (shift && anchor !== "") {
            selectRange(anchor, path);
        } else {
            selectOnly(path);
        }
    }

    function selectAll() {
        var o = {};
        for (var i = 0; i < order.length; i++) o[order[i]] = true;
        selected = o;
        selectionRev++;
    }

    function clearSelection() {
        selected = {};
        anchor = "";
        selectionRev++;
    }

    function copyFirst() {
        var paths = selectedInOrder();
        if (paths.length === 0) return;
        shell.copyFile(paths[0]);
    }

    function openPath(path) { shell.openFile(path); }

    function toggleOne(path) { shell.setFavorite(path, !isFav(path)); }

    function toggleSelected() {
        var paths = selectedInOrder();
        var pairs = [];
        for (var i = 0; i < paths.length; i++)
            pairs.push({ path: paths[i], want: !isFav(paths[i]) });
        shell.setFavorites(pairs);
    }

    function openCardMenu(path, fromItem, mx, my) {
        if (!isSelected(path)) selectOnly(path);
        var paths = selectedInOrder();
        menuCount = paths.length;
        favLabel = (paths.length === 1 && isFav(paths[0])) ? "取消收藏" : "收藏";
        confirmOpen = false;
        var pt = fromItem.mapToItem(face, mx, my);
        var mw = 196;
        var mh = 32 * 6 + 12;
        menuX = Math.max(8, Math.min(pt.x, Math.max(8, face.width - mw - 8)));
        menuY = Math.max(8, Math.min(pt.y, Math.max(8, face.height - mh - 8)));
        menuOpen = true;
    }

    function askDelete() {
        var paths = selectedInOrder();
        if (paths.length === 0) return;
        pendingDelete = paths;
        var n = paths.length;
        confirmText = n === 1
            ? "这张图会进入回收站，可以从回收站还原。确定删除？"
            : ("这 " + n + " 张图会进入回收站，可以从回收站还原。确定删除？");
        menuOpen = false;
        confirmOpen = true;
    }

    function confirmDelete() {
        var paths = pendingDelete;
        pendingDelete = [];
        confirmOpen = false;
        clearSelection();
        shell.trashOwned(paths);
    }

    function beginFileDrag() {
        var paths = selectedInOrder();
        if (paths.length === 0) return;
        var lines = [];
        for (var i = 0; i < paths.length; i++) lines.push(fileUrl(paths[i]));
        dragProxy.uris = lines.join("\r\n") + "\r\n";
        dragProxy.preview = fileUrl(paths[0]);
        menuOpen = false;
        // 在这次鼠标事件里开始拖。遮罩下一帧再收掉，避免改输入区把这次拖打断；
        // 松手时遮罩已经不在面板外，放置目标收得到。
        dragProxy.Drag.active = true;
        Qt.callLater(function() { if (dragProxy.Drag.active) win.inputTight = true; });
    }

    function onlyCtrl(mods) {
        return (mods & Qt.ControlModifier)
            && !(mods & Qt.ShiftModifier)
            && !(mods & Qt.AltModifier)
            && !(mods & Qt.MetaModifier);
    }

    onWidthChanged: refreshCols()
    onColCountChanged: rebuildRows(listView.contentY)
    onVisibleChanged: if (visible) Qt.callLater(refreshCols)

    Connections {
        target: shell
        function onLibraryTickChanged() { if (win.visible) win.reload(); }
    }

    Process {
        id: scanProc
        command: ["python3", shell.helper, "scan"]
        stdout: StdioCollector {
            onStreamFinished: win.applyScan(text)
        }
        stderr: StdioCollector {
            onStreamFinished: if (text.trim() !== "") shell.log(text.trim())
        }
        onExited: {
            if (win.scanAgain) {
                win.scanAgain = false;
                scanProc.running = true;
            }
        }
    }

    Item {
        id: dragProxy
        property string uris: ""
        property string preview: ""
        width: 1
        height: 1
        Drag.dragType: Drag.Automatic
        Drag.supportedActions: Qt.CopyAction
        Drag.proposedAction: Qt.CopyAction
        Drag.mimeData: ({ "text/uri-list": uris })
        Drag.imageSource: preview
        Drag.onDragFinished: win.endDrag()
    }

    Shortcut {
        sequence: "Escape"
        enabled: win.visible
        context: Qt.WindowShortcut
        onActivated: win.handleEsc()
    }

    ParallelAnimation {
        id: openAnim
        NumberAnimation {
            target: win
            property: "panelScale"
            to: 1
            duration: 220
            easing.type: Easing.OutCubic
        }
        NumberAnimation {
            target: win
            property: "panelOpacity"
            to: 1
            duration: 220
            easing.type: Easing.OutCubic
        }
    }

    ParallelAnimation {
        id: closeAnim
        NumberAnimation {
            target: win
            property: "panelScale"
            to: 0.96
            duration: 160
            easing.type: Easing.InCubic
        }
        NumberAnimation {
            target: win
            property: "panelOpacity"
            to: 0
            duration: 160
            easing.type: Easing.InCubic
        }
        onFinished: if (!win.open && !win.suppressHide) win.finishHide()
    }

    Timer {
        id: outsideGuard
        interval: 200
        onTriggered: win.ignoreOutside = false
    }

    MouseArea {
        id: scrim
        anchors.fill: parent
        acceptedButtons: Qt.AllButtons
        onClicked: if (!win.ignoreOutside) win.requestClose("outside")
    }

    Item {
        id: panel
        width: win.panelW
        height: win.panelH
        // 图层尺寸刚映射时 width 还是 0 或默认 500，先用屏幕逻辑尺寸，避免面板被摆到窗外。
        x: {
            var sw = win.screen ? win.screen.width : 0;
            var w = win.width > sw * 0.9 ? win.width : sw;
            return Math.round((w - width) / 2);
        }
        y: {
            var sh = win.screen ? win.screen.height : 0;
            var h = win.height > sh * 0.9 ? win.height : sh;
            return Math.round((h - height) / 2);
        }
        scale: win.panelScale
        opacity: win.panelOpacity
        transformOrigin: Item.Center

        RectangularShadow {
            anchors.fill: face
            offset.y: 10
            blur: 32
            radius: 18
            color: Qt.rgba(0, 0, 0, 0.18)
        }

        Rectangle {
            id: face
            anchors.fill: parent
            radius: 18
            clip: true
            color: win.shell.theme.galleryBg
            border.width: 1
            border.color: Qt.rgba(30 / 255, 40 / 255, 70 / 255, 0.12)

            // 点在面板上不关。铺在内容下面，空处的点击落在这里。
            MouseArea {
                anchors.fill: parent
                acceptedButtons: Qt.AllButtons
                onPressed: mouse => mouse.accepted = true
            }

    Item {
        id: page
        anchors.fill: parent
        focus: true

        Keys.onEscapePressed: event => {
            win.handleEsc();
            event.accepted = true;
        }
        Keys.onPressed: event => {
            if (event.key === Qt.Key_Delete) {
                win.askDelete();
                event.accepted = true;
            } else if (event.key === Qt.Key_C && win.onlyCtrl(event.modifiers)) {
                win.copyFirst();
                event.accepted = true;
            } else if (event.key === Qt.Key_A && win.onlyCtrl(event.modifiers)) {
                win.selectAll();
                event.accepted = true;
            }
        }

        Item {
            id: side
            width: 180
            anchors.top: parent.top
            anchors.bottom: parent.bottom
            anchors.left: parent.left

            Text {
                id: title
                text: "图库"
                color: "#2E3440"
                font.pixelSize: 20
                font.weight: Font.DemiBold
                font.family: F.ui
                anchors.left: parent.left
                anchors.top: parent.top
                anchors.leftMargin: 16
                anchors.topMargin: 16
            }

            Text {
                id: settingsLink
                text: "设置…"
                color: win.shell.theme.clipColor
                font.pixelSize: 12
                font.family: F.ui
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.bottom: parent.bottom
                anchors.leftMargin: 16
                anchors.rightMargin: 16
                anchors.bottomMargin: 16

                MouseArea {
                    anchors.fill: parent
                    cursorShape: Qt.PointingHandCursor
                    onClicked: win.shell.openSettings()
                }
            }

            Text {
                id: foot
                text: "保存 " + win.retention + " · 占用 " + win.formatSize(win.occupied)
                color: "#8A8478"
                font.pixelSize: 11
                font.family: F.ui
                wrapMode: Text.Wrap
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.bottom: settingsLink.top
                anchors.leftMargin: 16
                anchors.rightMargin: 16
                anchors.bottomMargin: 4
            }

            Item {
                id: filterHost
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.top: title.bottom
                anchors.bottom: foot.top
                anchors.topMargin: 8

                Rectangle {
                    y: filterCol.y + win.filterIndex() * win.filterRowH
                    height: win.filterRowH
                    anchors.left: parent.left
                    anchors.right: parent.right
                    anchors.leftMargin: 8
                    anchors.rightMargin: 8
                    radius: 8
                    color: "#EFDCD6"
                    Behavior on y { NumberAnimation { duration: 180; easing.type: Easing.OutCubic } }
                }

                Column {
                    id: filterCol
                    y: 8
                    width: parent.width

                    Repeater {
                        model: win.filterList
                        delegate: Item {
                            required property var modelData
                            width: filterCol.width
                            height: win.filterRowH

                            Rectangle {
                                anchors.fill: parent
                                anchors.leftMargin: 8
                                anchors.rightMargin: 8
                                anchors.topMargin: 1
                                anchors.bottomMargin: 1
                                radius: 8
                                color: "#F0E9DC"
                                opacity: rowMouse.containsMouse ? 1 : 0
                                Behavior on opacity { NumberAnimation { duration: 120 } }
                            }

                            Text {
                                x: 20
                                anchors.verticalCenter: parent.verticalCenter
                                text: modelData.label
                                color: "#2E3440"
                                font.pixelSize: 13
                                font.family: F.ui
                            }

                            Text {
                                anchors.right: parent.right
                                anchors.rightMargin: 20
                                anchors.verticalCenter: parent.verticalCenter
                                text: win.countOf(modelData.key)
                                color: "#8A8478"
                                font.pixelSize: 12
                                font.family: F.ui
                            }

                            MouseArea {
                                id: rowMouse
                                anchors.fill: parent
                                hoverEnabled: true
                                cursorShape: Qt.PointingHandCursor
                                onClicked: win.switchFilter(modelData.key)
                            }
                        }
                    }
                }
            }
        }

        Rectangle {
            anchors.left: side.right
            anchors.top: parent.top
            anchors.bottom: parent.bottom
            width: 1
            color: "#E3DAC8"
        }

        Item {
            id: right
            anchors.left: side.right
            anchors.leftMargin: 1
            anchors.right: parent.right
            anchors.top: parent.top
            anchors.bottom: parent.bottom
            clip: true

            ListView {
                id: listView
                anchors.fill: parent
                visible: win.order.length > 0
                onWidthChanged: win.refreshCols()
                model: win.rows
                reuseItems: true
                cacheBuffer: 900
                boundsBehavior: Flickable.StopAtBounds
                header: Item { height: 12; width: 1 }
                footer: Item { height: 24; width: 1 }
                ScrollBar.vertical: ScrollBar { policy: ScrollBar.AsNeeded }

                delegate: Item {
                    required property var modelData
                    required property int index
                    width: ListView.view.width
                    height: modelData.kind === "header" ? 38 : 170

                    Text {
                        visible: modelData.kind === "header"
                        x: 16
                        y: 10
                        text: modelData.kind === "header" ? modelData.text : ""
                        color: "#2E3440"
                        font.pixelSize: 14
                        font.weight: Font.DemiBold
                        font.family: F.ui
                    }

                    Row {
                        visible: modelData.kind === "cards"
                        x: 8
                        height: 170
                        Repeater {
                            model: modelData.kind === "cards" ? JSON.parse(modelData.payload) : []
                            delegate: Thumb {}
                        }
                    }
                }
            }

            Text {
                anchors.centerIn: parent
                width: parent.width - 48
                visible: win.loaded && win.order.length === 0
                horizontalAlignment: Text.AlignHCenter
                wrapMode: Text.Wrap
                color: "#8A8478"
                font.pixelSize: 14
                font.family: F.ui
                text: "还没有图。截一张试试：" + win.capture
            }
        }
    }

            Item {
                id: menuLayer
                visible: win.menuOpen
                anchors.fill: parent
                z: 2

                MouseArea {
                    anchors.fill: parent
                    acceptedButtons: Qt.AllButtons
                    onPressed: mouse => {
                        win.menuOpen = false;
                        mouse.accepted = true;
                    }
                }

                Rectangle {
                    x: win.menuX
                    y: win.menuY
                    width: 196
                    height: menuCol.implicitHeight + 8
                    radius: 10
                    color: win.shell.theme.galleryBg
                    border.width: 1
                    border.color: Qt.rgba(30 / 255, 40 / 255, 70 / 255, 0.12)
                    clip: true

                    Column {
                        id: menuCol
                        y: 4
                        width: parent.width

                        MenuRow {
                            label: "挂回绳上"
                            onTriggered: {
                                win.menuOpen = false;
                                win.shell.hangBack(win.selectedInOrder());
                            }
                        }
                        MenuRow {
                            label: "复制"
                            onTriggered: {
                                win.menuOpen = false;
                                win.copyFirst();
                            }
                        }
                        MenuRow {
                            label: "打开"
                            enabledRow: win.menuCount === 1
                            onTriggered: {
                                win.menuOpen = false;
                                var paths = win.selectedInOrder();
                                if (paths.length === 1) win.openPath(paths[0]);
                            }
                        }
                        MenuRow {
                            label: win.favLabel
                            onTriggered: {
                                win.menuOpen = false;
                                win.toggleSelected();
                            }
                        }
                        MenuRow {
                            label: "在文件管理器中显示"
                            onTriggered: {
                                win.menuOpen = false;
                                var paths = win.selectedInOrder();
                                if (paths.length > 0) win.shell.revealFile(paths[0]);
                            }
                        }
                        Rectangle {
                            width: parent.width - 16
                            x: 8
                            height: 1
                            color: "#E3DAC8"
                        }
                        MenuRow {
                            label: "删除"
                            fg: win.shell.theme.clipColor
                            onTriggered: win.askDelete()
                        }
                    }
                }
            }

            Item {
                id: confirmLayer
                visible: win.confirmOpen
                anchors.fill: parent
                z: 3

                MouseArea {
                    anchors.fill: parent
                    acceptedButtons: Qt.AllButtons
                    onClicked: win.confirmOpen = false
                }

                Rectangle {
                    width: 420
                    height: confirmCol.implicitHeight + 28
                    anchors.centerIn: parent
                    radius: 12
                    color: win.shell.theme.galleryBg
                    border.width: 1
                    border.color: Qt.rgba(30 / 255, 40 / 255, 70 / 255, 0.12)

                    MouseArea {
                        anchors.fill: parent
                        acceptedButtons: Qt.AllButtons
                        onPressed: mouse => mouse.accepted = true
                    }

                    Column {
                        id: confirmCol
                        anchors.left: parent.left
                        anchors.right: parent.right
                        anchors.top: parent.top
                        anchors.margins: 16
                        spacing: 14

                        Text {
                            width: parent.width
                            text: win.confirmText
                            wrapMode: Text.Wrap
                            color: "#2E3440"
                            font.pixelSize: 13
                            font.family: F.ui
                        }

                        Row {
                            anchors.right: parent.right
                            spacing: 8

                            Rectangle {
                                width: cancelText.implicitWidth + 28
                                height: 32
                                radius: 8
                                color: cancelHit.containsMouse ? "#EFDCD6" : "transparent"
                                border.width: 1
                                border.color: Qt.rgba(30 / 255, 40 / 255, 70 / 255, 0.12)
                                Text {
                                    id: cancelText
                                    anchors.centerIn: parent
                                    text: "取消"
                                    color: "#2E3440"
                                    font.pixelSize: 13
                                    font.family: F.ui
                                }
                                MouseArea {
                                    id: cancelHit
                                    anchors.fill: parent
                                    hoverEnabled: true
                                    cursorShape: Qt.PointingHandCursor
                                    onClicked: win.confirmOpen = false
                                }
                            }

                            Rectangle {
                                width: okText.implicitWidth + 28
                                height: 32
                                radius: 8
                                color: okHit.containsMouse ? "#EFDCD6" : "transparent"
                                border.width: 1
                                border.color: win.shell.theme.clipColor
                                Text {
                                    id: okText
                                    anchors.centerIn: parent
                                    text: "确定"
                                    color: win.shell.theme.clipColor
                                    font.pixelSize: 13
                                    font.family: F.ui
                                }
                                MouseArea {
                                    id: okHit
                                    anchors.fill: parent
                                    hoverEnabled: true
                                    cursorShape: Qt.PointingHandCursor
                                    onClicked: win.confirmDelete()
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    component MenuRow: Rectangle {
        id: menuRow
        property string label: ""
        property color fg: "#2E3440"
        property bool enabledRow: true
        signal triggered()
        width: parent ? parent.width : 196
        height: 32
        color: rowHit.containsMouse && enabledRow ? "#EFDCD6" : "transparent"
        Text {
            x: 12
            anchors.verticalCenter: parent.verticalCenter
            text: menuRow.label
            color: menuRow.enabledRow ? menuRow.fg : "#B0AAA0"
            font.pixelSize: 13
            font.family: F.ui
        }
        MouseArea {
            id: rowHit
            anchors.fill: parent
            hoverEnabled: true
            enabled: menuRow.enabledRow
            cursorShape: enabled ? Qt.PointingHandCursor : Qt.ArrowCursor
            onClicked: menuRow.triggered()
        }
    }

    component Thumb: Item {
        id: thumb
        required property var modelData
        readonly property var item: modelData
        readonly property bool on: win.isSelected(item.path)

        width: 184
        height: 170

        property bool hover: cardMouse.containsMouse || starMouse.containsMouse
        property bool favNow: item.favorite
        onFavNowChanged: starCanvas.requestPaint()
        property bool dragArmed: false
        property point dragStart
        property real lastClick: 0

        Item {
            id: visual
            x: 8
            y: thumb.hover ? 5 : 8
            width: 168
            height: 154
            scale: thumb.hover ? 1.02 : 1
            transformOrigin: Item.Center
            Behavior on y { NumberAnimation { duration: 150; easing.type: Easing.OutCubic } }
            Behavior on scale { NumberAnimation { duration: 150; easing.type: Easing.OutCubic } }

            RectangularShadow {
                anchors.fill: plate
                offset.y: thumb.hover ? 8 : 6
                blur: 14
                radius: 10
                color: Qt.rgba(0, 0, 0, thumb.hover ? 0.32 : 0.18)
                Behavior on offset.y { NumberAnimation { duration: 150 } }
            }

            Rectangle {
                id: plate
                anchors.fill: parent
                radius: 10
                color: "white"
            }

            Rectangle {
                x: 8
                y: 8
                width: 152
                height: 120
                radius: 6
                color: photo.status === Image.Ready ? "transparent" : "#E8E0D4"

                Image {
                    id: photo
                    anchors.fill: parent
                    fillMode: Image.PreserveAspectFit
                    asynchronous: true
                    cache: true
                    smooth: true
                    sourceSize.width: 336
                    source: win.fileUrl(thumb.item.path)
                    opacity: status === Image.Ready ? 1 : 0
                    onStatusChanged: {
                        if (status === Image.Error)
                            win.shell.log("图库图片加载失败 " + thumb.item.path
                                + " status=" + status
                                + " sourceSize=" + sourceSize.width + "x" + sourceSize.height);
                    }
                    Behavior on opacity { NumberAnimation { duration: 180 } }
                }
            }

            Text {
                x: 8
                y: 134
                text: String(thumb.item.time).slice(11, 16)
                color: "#8A8478"
                font.pixelSize: 11
                font.family: F.ui
            }

            Rectangle {
                anchors.fill: plate
                radius: 10
                color: "transparent"
                border.width: 2
                border.color: win.shell.theme.clipColor
                opacity: thumb.on ? 1 : 0
                Behavior on opacity { NumberAnimation { duration: 120 } }
            }

            Item {
                id: starHost
                x: 168 - 4 - 26
                y: 4
                width: 26
                height: 26
                visible: thumb.item.favorite || thumb.hover

                Rectangle {
                    anchors.fill: parent
                    radius: 13
                    color: Qt.rgba(1, 1, 1, 200 / 255)
                    opacity: thumb.item.favorite ? 0 : 1
                }

                Canvas {
                    id: starCanvas
                    anchors.centerIn: parent
                    width: 18
                    height: 18
                    scale: thumb.item.favorite && starMouse.containsMouse ? 1.15 : 1
                    Behavior on scale { NumberAnimation { duration: 120; easing.type: Easing.OutCubic } }
                    onPaint: {
                        var ctx = getContext("2d");
                        ctx.reset();
                        var pts = [[12, 1], [14.586, 8.44], [22.462, 8.601], [16.185, 13.36], [18.466, 20.899],
                                   [12, 16.4], [5.534, 20.899], [7.815, 13.36], [1.538, 8.601], [9.414, 8.44]];
                        var s = width / 24;
                        ctx.scale(s, s);
                        ctx.beginPath();
                        ctx.moveTo(pts[0][0], pts[0][1]);
                        for (var i = 1; i < pts.length; i++) ctx.lineTo(pts[i][0], pts[i][1]);
                        ctx.closePath();
                        if (thumb.item.favorite) {
                            ctx.fillStyle = win.shell.theme.clipCss;
                            ctx.fill();
                        } else {
                            ctx.strokeStyle = "#8A8478";
                            ctx.lineWidth = 1.4 / s;
                            ctx.lineJoin = "round";
                            ctx.stroke();
                        }
                    }
                    onWidthChanged: requestPaint()
                    Component.onCompleted: requestPaint()
                    Connections {
                        target: win.shell.theme
                        function onClipCssChanged() { starCanvas.requestPaint(); }
                    }
                }

                MouseArea {
                    id: starMouse
                    anchors.fill: parent
                    hoverEnabled: true
                    cursorShape: Qt.PointingHandCursor
                    onPressed: mouse => {
                        mouse.accepted = true;
                        win.toggleOne(thumb.item.path);
                    }
                    onContainsMouseChanged: starCanvas.requestPaint()
                }
            }

            MouseArea {
                id: cardMouse
                anchors.fill: parent
                hoverEnabled: true
                acceptedButtons: Qt.LeftButton | Qt.RightButton
                onPressed: mouse => {
                    page.forceActiveFocus();
                    if (mouse.button === Qt.RightButton) {
                        win.openCardMenu(thumb.item.path, cardMouse, mouse.x, mouse.y);
                        return;
                    }
                    var now = Date.now();
                    if (now - thumb.lastClick < 400) {
                        thumb.lastClick = 0;
                        thumb.dragArmed = false;
                        win.openPath(thumb.item.path);
                        return;
                    }
                    thumb.lastClick = now;
                    thumb.dragArmed = true;
                    thumb.dragStart = Qt.point(mouse.x, mouse.y);
                    win.selectFromClick(thumb.item.path, mouse.modifiers);
                }
                onPositionChanged: mouse => {
                    if (!thumb.dragArmed || !pressed) return;
                    if (Math.abs(mouse.x - thumb.dragStart.x) < 4 && Math.abs(mouse.y - thumb.dragStart.y) < 4) return;
                    thumb.dragArmed = false;
                    win.beginFileDrag();
                }
                onReleased: thumb.dragArmed = false
                onCanceled: thumb.dragArmed = false
            }
        }
    }
}
