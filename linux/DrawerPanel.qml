import QtQuick
import QtQuick.Shapes
import Qt.labs.platform as Native
import Quickshell.Widgets
import "layout.js" as L

// 抽屉的本体（展开态的全部内容）：Caelestia 风格的底板 + 左边最新大图 + 右边 3×2 小格。
// 窗口壳和悬停逻辑在 DrawerWindow.qml；这里只依赖 shell 的现有函数，
// 所以能在 _test 里配一个假 shell 离屏渲染。
// 坐标：底板右缘贴窗口右缘（即屏幕右边框内沿），顶缘贴屏幕顶边框内沿，
// 窗口比底板多出左侧和下方的内凹圆角空间（layout.js 的 DrawerConcave）。
// bare：不画底板和内凹角，只放内容（并进 Caelestia 时底板由外壳的抽屉底色画），尺寸就是 DrawerW × DrawerH。
Item {
    id: panel

    required property var shell
    property var theme: shell.theme   // 要 drawerSurface/SurfaceHigh/Fg/FgDim/Primary 五个颜色；外壳可换成自己的
    property bool bare: false
    property int menuOpen: 0          // 右键菜单开着时不许收起（窗口读这个）
    property string flashPath: ""     // 新图到达：大图位置的缩放+淡入
    property bool dropHover: false    // 外部拖着东西悬在抽屉上：描边 +「松手放进系图」
    property bool ownDragging: false  // 自己的格子正在往外拖，不当外部拖入

    readonly property real w: width
    readonly property real h: height
    readonly property real conc: bare ? 0 : L.DrawerConcave
    readonly property real bodyW: w - conc
    readonly property real bodyH: h - conc

    // 窗口里接收鼠标的范围是底板矩形（conc, 0, bodyW, bodyH），由 DrawerWindow 的 mask 自己算。

    // ---- 数据：live 条目、最新在前，大图 1 张 + 小格最多 6 张 ----
    ListModel { id: items }

    function sync() {
        var ids = [];
        var rows = [];
        for (var i = shell.line.count - 1; i >= 0 && rows.length < 1 + L.DrawerTileCount; i--) {
            var e = shell.line.get(i);
            if (e.gone) continue;
            ids.push(e.entryId);
            rows.push({ entryId: e.entryId, path: e.path });
        }
        if (ids.length === items.count) {
            var same = true;
            for (var j = 0; j < ids.length; j++)
                if (items.get(j).entryId !== ids[j]) { same = false; break; }
            if (same) return;
        }
        items.clear();
        for (var k = 0; k < rows.length; k++) items.append(rows[k]);
    }

    Connections {
        target: panel.shell.line
        function onDataChanged() { panel.sync(); }
        function onCountChanged() { panel.sync(); }
    }
    Component.onCompleted: panel.sync()

    // 底板路径：顺时针一圈。两个贴边框的角（左上、右下）内凹，左下大圆角，右上顺着屏幕角小圆角。
    Shape {
        visible: !panel.bare
        anchors.fill: parent
        preferredRendererType: Shape.CurveRenderer
        asynchronous: false

        ShapePath {
            startX: 0
            startY: 0
            strokeWidth: panel.dropHover ? 2 : -1
            strokeColor: panel.theme.drawerPrimary
            fillColor: panel.theme.drawerSurface

            // 左上：贴顶边框的内凹角（圆心在 (0, conc)）
            PathArc {
                relativeX: panel.conc; relativeY: panel.conc
                radiusX: panel.conc; radiusY: panel.conc
                direction: PathArc.Clockwise
            }
            PathLine { relativeX: 0; relativeY: panel.bodyH - L.DrawerCorner - panel.conc }
            // 左下：外凸大圆角
            PathArc {
                relativeX: L.DrawerCorner; relativeY: L.DrawerCorner
                radiusX: L.DrawerCorner; radiusY: L.DrawerCorner
                direction: PathArc.Counterclockwise
            }
            PathLine { relativeX: panel.bodyW - L.DrawerCorner - panel.conc; relativeY: 0 }
            // 右下：贴右边框的内凹角，往下顺进边框（圆心在 (w - conc, bodyH + conc)）
            PathArc {
                relativeX: panel.conc; relativeY: panel.conc
                radiusX: panel.conc; radiusY: panel.conc
                direction: PathArc.Clockwise
            }
            PathLine { relativeX: 0; relativeY: -(panel.h - L.DrawerTopRight) }
            // 右上：填满边框内侧的圆角
            PathArc {
                relativeX: -L.DrawerTopRight; relativeY: -L.DrawerTopRight
                radiusX: L.DrawerTopRight; radiusY: L.DrawerTopRight
                direction: PathArc.Counterclockwise
            }
            PathLine { relativeX: -(panel.w - L.DrawerTopRight); relativeY: 0 }
        }
    }

    // 外部拖入悬停：内容区盖一层底板色，正中写提示（六格满时没有空角可放）。
    // 描边在底板 ShapePath 上换成主色。
    Rectangle {
        visible: panel.dropHover
        z: 20
        x: panel.conc + L.DrawerPad - 2
        y: L.DrawerPad - 2
        width: panel.bodyW - 2 * L.DrawerPad + 4
        height: panel.bodyH - 2 * L.DrawerPad + 4
        radius: 12
        color: Qt.rgba(panel.theme.drawerSurface.r, panel.theme.drawerSurface.g,
                       panel.theme.drawerSurface.b, 0.8)
        // 没有底板描边时，主色描边画在这层上
        border.width: panel.bare ? 2 : 0
        border.color: panel.theme.drawerPrimary

        Text {
            anchors.centerIn: parent
            text: "松手放进系图"
            color: panel.theme.drawerPrimary
            font.pixelSize: 14
            font.family: "HarmonyOS Sans SC"
        }
    }

    // ---- 空态 ----
    Text {
        x: panel.conc + (panel.bodyW - width) / 2
        y: (panel.bodyH - height) / 2
        text: "还没有截图"
        color: panel.theme.drawerFgDim
        font.pixelSize: 13
        font.family: "HarmonyOS Sans SC"
        visible: items.count === 0
    }

    // ---- 条目 ----
    Repeater {
        model: items

        delegate: Item {
            id: tile

            required property int index
            required property string entryId
            required property string path

            readonly property bool big: index === 0
            readonly property var theme: panel.theme
            readonly property bool favorite: {
                var parts = path.split("/");
                return parts.length >= 2 && parts[parts.length - 2] === "收藏";
            }
            readonly property url fileUrl: "file://" + path
            readonly property bool flashing: panel.flashPath !== "" && panel.flashPath === path && big

            x: panel.conc + (big ? L.DrawerPad
                   : L.DrawerPad + L.DrawerBigW + L.DrawerGap + (index - 1) % L.DrawerCols * (L.DrawerTileW + L.DrawerGap))
            y: big ? L.DrawerPad
                   : L.DrawerPad + Math.floor((index - 1) / L.DrawerCols) * (L.DrawerTileH + L.DrawerGap)
            width: big ? L.DrawerBigW : L.DrawerTileW
            height: big ? L.DrawerBigH : L.DrawerTileH
            transformOrigin: Item.Center

            // 新图入场：缩放 + 淡入
            property real flashScale: 1
            property real flashOpacity: 1
            opacity: flashOpacity
            scale: flashScale

            ParallelAnimation {
                id: flashAnim
                NumberAnimation { target: tile; property: "flashOpacity"; to: 1; duration: 320; easing.type: Easing.OutCubic }
                NumberAnimation { target: tile; property: "flashScale"; to: 1; duration: 450; easing.type: Easing.OutCubic }
            }
            onFlashingChanged: if (flashing) {
                flashScale = 1.15;
                flashOpacity = 0;
                flashAnim.restart();
            }

            ClippingRectangle {
                anchors.fill: parent
                radius: tile.big ? 12 : 8
                color: panel.theme.drawerSurfaceHigh

                Image {
                    anchors.fill: parent
                    source: tile.fileUrl
                    sourceSize.width: tile.big ? 480 : 160
                    asynchronous: true
                    cache: true
                    fillMode: Image.PreserveAspectCrop
                    smooth: true
                    mipmap: true
                }

                // 悬停描边
                Rectangle {
                    anchors.fill: parent
                    radius: tile.big ? 12 : 8
                    color: "transparent"
                    border.width: hitArea.containsMouse ? 2 : 0
                    border.color: panel.theme.drawerPrimary
                    Behavior on border.width { NumberAnimation { duration: 120 } }
                }

                // 大图左下角：截图时间（文件名抠不出时间就不显示）
                Rectangle {
                    visible: tile.big && caption.text !== ""
                    anchors.left: parent.left
                    anchors.bottom: parent.bottom
                    anchors.margins: 8
                    width: caption.implicitWidth + 14
                    height: 22
                    radius: 6
                    color: Qt.rgba(0, 0, 0, 0.45)

                    Text {
                        id: caption
                        anchors.centerIn: parent
                        text: L.timeAgo(tile.path)
                        color: "white"
                        font.pixelSize: 11
                        font.family: "HarmonyOS Sans SC"
                    }
                }
            }

            // 收藏星标（右上角，不接收鼠标；画法和 Card 一致）
            Canvas {
                visible: tile.favorite
                anchors.right: parent.right
                anchors.top: parent.top
                anchors.rightMargin: tile.big ? -4 : -5
                anchors.topMargin: tile.big ? -4 : -5
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

            MouseArea {
                id: hitArea
                anchors.fill: parent
                hoverEnabled: true
                acceptedButtons: Qt.LeftButton | Qt.RightButton
                cursorShape: Qt.PointingHandCursor

                property point pressAt
                property bool moved: false
                property real lastClick: 0

                onPressed: mouse => {
                    if (mouse.button === Qt.RightButton) {
                        panel.menuShown();
                        panel.pick(tile);
                        tileMenu.open();
                        return;
                    }
                    pressAt = Qt.point(mouse.x, mouse.y);
                    moved = false;
                }

                onPositionChanged: mouse => {
                    // hoverEnabled 时不按键移动也会进来；只有按住左键才算拖
                    if (moved || !(mouse.buttons & Qt.LeftButton)) return;
                    var dx = mouse.x - pressAt.x;
                    var dy = mouse.y - pressAt.y;
                    if (Math.hypot(dx, dy) < 6) return;
                    moved = true;
                    clickTimer.stop();
                    tile.startExternalDrag();
                }

                onReleased: {
                    if (moved) return;
                    var now = Date.now();
                    if (now - lastClick < L.ClickDelay) {
                        // 双击：只打开，不复制
                        clickTimer.stop();
                        lastClick = 0;
                        panel.shell.openFile(tile.path);
                        return;
                    }
                    lastClick = now;
                    clickTimer.restart();
                }

                onCanceled: moved = false
            }

            Timer {
                id: clickTimer
                interval: L.ClickDelay
                onTriggered: panel.shell.copyFile(tile.path)
            }

            // 拖出一份给别的程序（只 Copy，图留在抽屉里）
            function startExternalDrag() {
                panel.ownDragging = true;
                dragProxy.Drag.active = true;
            }

            Item {
                id: dragProxy
                width: 1
                height: 1
                Drag.dragType: Drag.Automatic
                Drag.supportedActions: Qt.CopyAction
                Drag.proposedAction: Qt.CopyAction
                Drag.mimeData: ({ "text/uri-list": tile.fileUrl + "\r\n" })
                Drag.imageSource: tile.fileUrl
                Drag.imageSourceSize: Qt.size(tile.width, tile.height)
                Drag.onDragFinished: dropAction => {
                    dragProxy.Drag.active = false;
                    panel.ownDragging = false;
                }
            }
        }
    }

    property var picked: null   // 右键菜单当前指向的条目

    function pick(tile) { picked = tile; }

    function menuShown() {
        menuOpen++;
    }

    function menuHidden() {
        menuOpen = Math.max(0, menuOpen - 1);
    }

    Native.Menu {
        id: tileMenu
        onAboutToHide: panel.menuHidden()

        Native.MenuItem { text: "打开图库"; onTriggered: panel.shell.openGallery() }
        Native.MenuSeparator {}
        Native.MenuItem { text: "复制"; visible: !!panel.picked; onTriggered: panel.shell.copyFile(panel.picked.path) }
        Native.MenuItem { text: "打开"; visible: !!panel.picked; onTriggered: panel.shell.openFile(panel.picked.path) }
        Native.MenuItem {
            visible: !!panel.picked
            text: panel.picked && panel.picked.favorite ? "取消收藏" : "收藏"
            onTriggered: panel.shell.setFavorite(panel.picked.path, !panel.picked.favorite)
        }
        Native.MenuItem { text: "在文件管理器中显示"; visible: !!panel.picked; onTriggered: panel.shell.revealFile(panel.picked.path) }
        Native.MenuSeparator {}
        Native.MenuItem { text: "取下"; visible: !!panel.picked; onTriggered: panel.shell.discard(panel.picked.entryId) }
    }
}
