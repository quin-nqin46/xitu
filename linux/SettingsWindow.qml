import QtQuick
import Quickshell
import Quickshell.Io

// 设置窗口，内容照 Windows 版 SettingsWindow.cs 的「图库」「通用」两页，外加主题。
// 普通的浮动窗口（xdg toplevel），和图库浮层分开；从图库打开时图库先关掉，免得浮层盖住它。
// 读写都经助手：settings-get / settings-set / library-move / sweep / pick-folder。
FloatingWindow {
    id: win

    required property var shell

    title: "系图设置"
    visible: false
    color: "#F6F1E7"
    implicitWidth: 520
    implicitHeight: 600
    minimumSize: Qt.size(460, 520)

    readonly property string fontFamily: "HarmonyOS Sans SC"
    readonly property color ink: "#2E3440"
    readonly property color mute: "#8A8478"
    readonly property color line: "#D9D0C1"
    readonly property color accent: shell.theme.clipColor

    property bool loaded: false
    property var info: ({})
    property string pathText: ""
    property string status: ""
    property string cleanResult: ""
    property bool busy: false
    property string pendingDest: ""
    property bool askMove: false

    function show() {
        status = "";
        cleanResult = "";
        askMove = false;
        reload();
        visible = true;
    }

    function reload() {
        if (getProc.running) return;
        getProc.running = true;
    }

    function formatSize(n) {
        if (n < 1024) return n + " B";
        if (n < 1024 * 1024) return (n / 1024).toFixed(1) + " KB";
        if (n < 1024 * 1024 * 1024) return (n / 1024 / 1024).toFixed(1) + " MB";
        return (n / 1024 / 1024 / 1024).toFixed(2) + " GB";
    }

    function setValue(key, value) {
        setJob.createObject(win, { key: key, value: String(value) });
    }

    // 「更改」：路径没变不做事；图库里有图时先问要不要搬，同 ChangePath
    function changeLibrary(dest) {
        dest = dest.trim();
        if (dest === "" || busy) return;
        if (dest.startsWith("~/")) dest = shell.home + dest.slice(1);
        if (dest.charAt(0) !== "/") { status = "图库位置要写绝对路径"; return; }
        if (dest.replace(/\/+$/, "") === String(info.libraryPath || "").replace(/\/+$/, "")) return;
        pendingDest = dest;
        if ((info.count || 0) > 0) askMove = true;
        else runMove(false);
    }

    function runMove(move) {
        askMove = false;
        busy = true;
        status = move ? "正在搬图…" : "";
        moveProc.command = ["python3", shell.helper, "library-move", pendingDest, move ? "1" : "0"];
        moveProc.running = true;
    }

    Process {
        id: getProc
        command: ["python3", win.shell.helper, "settings-get"]
        stdout: StdioCollector {
            onStreamFinished: {
                try {
                    win.info = JSON.parse(text);
                    win.pathText = win.info.libraryPath;
                    win.loaded = true;
                } catch (e) {
                    win.shell.log("settings-get 读不懂：" + e);
                }
            }
        }
        stderr: StdioCollector { onStreamFinished: if (text.trim() !== "") win.shell.log(text.trim()) }
    }

    Component {
        id: setJob
        Process {
            id: setProc
            property string key
            property string value
            running: true
            command: ["python3", win.shell.helper, "settings-set", key, value]
            stdout: StdioCollector {
                onStreamFinished: {
                    if (text.trim() !== "") {
                        win.shell.log("设置 " + setProc.key + " = " + text.trim());
                        win.shell.libraryTick++;
                    }
                    win.reload();
                    setProc.destroy();
                }
            }
            stderr: StdioCollector { onStreamFinished: if (text.trim() !== "") win.status = text.trim() }
        }
    }

    Process {
        id: moveProc
        stdout: StdioCollector {
            onStreamFinished: {
                win.busy = false;
                if (text.trim() === "") return;
                try {
                    var out = JSON.parse(text);
                    for (var i = 0; i < out.moved.length; i++)
                        win.shell.relocate(out.moved[i][0], out.moved[i][1], false);
                    win.shell.libraryTick++;
                    win.status = out.moved.length > 0 ? "已搬 " + out.moved.length + " 张到新位置" : "图库位置已更改";
                } catch (e) {
                    win.shell.log("library-move 读不懂：" + e);
                }
                win.reload();
            }
        }
        stderr: StdioCollector {
            onStreamFinished: {
                if (text.trim() === "") return;
                win.busy = false;
                win.status = text.trim();
                win.pathText = win.info.libraryPath || "";
            }
        }
    }

    Process {
        id: pickProc
        stdout: StdioCollector {
            onStreamFinished: {
                var p = text.trim();
                if (p !== "") { win.pathText = p; win.changeLibrary(p); }
            }
        }
        stderr: StdioCollector { onStreamFinished: if (text.trim() !== "") win.status = text.trim() }
    }

    Process {
        id: sweepNow
        stdout: StdioCollector {
            onStreamFinished: {
                win.busy = false;
                var n = parseInt(text.trim()) || 0;
                win.cleanResult = n === 0 ? "没有过期的图" : "已清理 " + n + " 张（进回收站）";
                if (n > 0) win.shell.libraryTick++;
                win.reload();
            }
        }
        stderr: StdioCollector { onStreamFinished: if (text.trim() !== "") { win.busy = false; win.cleanResult = text.trim(); } }
    }

    // ---- 小部件（内联组件看不到外面的 id，颜色写死，强调色从外面传）----
    component Heading: Text {
        color: "#2E3440"
        font.family: "HarmonyOS Sans SC"
        font.pixelSize: 13
        font.weight: Font.DemiBold
    }

    component Note: Text {
        color: "#8A8478"
        font.family: "HarmonyOS Sans SC"
        font.pixelSize: 12
        wrapMode: Text.Wrap
    }

    component PlainButton: Rectangle {
        id: btn
        property string text
        signal clicked()
        implicitWidth: label.implicitWidth + 28
        implicitHeight: 28
        radius: 6
        color: !btn.enabled ? "#EEE8DC" : area.pressed ? "#E4DACA" : area.containsMouse ? "#F1EADF" : "#FFFFFF"
        border.color: "#D9D0C1"
        border.width: 1
        Text {
            id: label
            anchors.centerIn: parent
            text: btn.text
            color: btn.enabled ? "#2E3440" : "#8A8478"
            font.family: "HarmonyOS Sans SC"
            font.pixelSize: 12
        }
        MouseArea {
            id: area
            anchors.fill: parent
            hoverEnabled: true
            cursorShape: Qt.PointingHandCursor
            onClicked: btn.clicked()
        }
    }

    // 单选的一行：圆点 + 文字
    component Choice: Item {
        id: choice
        property string text
        property bool checked: false
        property color accent: "#3D6FE0"
        signal picked()
        implicitWidth: row.implicitWidth
        implicitHeight: 26
        Row {
            id: row
            spacing: 8
            anchors.verticalCenter: parent.verticalCenter
            Rectangle {
                width: 14; height: 14; radius: 7
                anchors.verticalCenter: parent.verticalCenter
                color: "#FFFFFF"
                border.color: choice.checked ? choice.accent : "#D9D0C1"
                border.width: choice.checked ? 2 : 1
                Rectangle {
                    anchors.centerIn: parent
                    width: 6; height: 6; radius: 3
                    color: choice.accent
                    visible: choice.checked
                }
            }
            Text {
                text: choice.text
                color: "#2E3440"
                font.family: "HarmonyOS Sans SC"
                font.pixelSize: 12
                anchors.verticalCenter: parent.verticalCenter
            }
        }
        MouseArea {
            anchors.fill: parent
            cursorShape: Qt.PointingHandCursor
            onClicked: if (!choice.checked) choice.picked()
        }
    }

    Flickable {
        anchors.fill: parent
        contentHeight: body.implicitHeight + 36
        clip: true
        boundsBehavior: Flickable.StopAtBounds

        Column {
            id: body
            x: 18
            y: 18
            width: parent.width - 36
            spacing: 6
            visible: win.loaded

            // ---- 图库 ----
            Heading { text: "位置" }
            Row {
                spacing: 8
                width: parent.width
                Rectangle {
                    width: parent.width - change.width - browse.width - 16
                    height: 28
                    radius: 6
                    color: "#FFFFFF"
                    border.color: pathInput.activeFocus ? win.accent : win.line
                    border.width: pathInput.activeFocus ? 1.5 : 1
                    TextInput {
                        id: pathInput
                        anchors.fill: parent
                        anchors.leftMargin: 8
                        anchors.rightMargin: 8
                        verticalAlignment: TextInput.AlignVCenter
                        clip: true
                        text: win.pathText
                        onTextEdited: win.pathText = text
                        color: win.ink
                        selectByMouse: true
                        font.family: win.fontFamily
                        font.pixelSize: 12
                        enabled: !win.busy
                        onAccepted: win.changeLibrary(text)
                    }
                }
                PlainButton {
                    id: browse
                    text: "浏览…"
                    enabled: !win.busy
                    onClicked: {
                        if (pickProc.running) return;
                        pickProc.command = ["python3", win.shell.helper, "pick-folder", win.info.libraryPath || win.shell.home];
                        pickProc.running = true;
                    }
                }
                PlainButton {
                    id: change
                    text: "更改"
                    enabled: !win.busy && win.pathText.trim() !== "" && win.pathText !== win.info.libraryPath
                    onClicked: win.changeLibrary(win.pathText)
                }
            }

            // 有图时问要不要搬，同 Windows 的「是 / 否 / 取消」
            Rectangle {
                width: parent.width
                height: askCol.implicitHeight + 20
                radius: 8
                color: "#FFFFFF"
                border.color: win.line
                visible: win.askMove
                Column {
                    id: askCol
                    x: 10; y: 10
                    width: parent.width - 20
                    spacing: 8
                    Text {
                        text: "把现有 " + (win.info.count || 0) + " 张图搬到新位置吗？"
                        color: win.ink
                        font.family: win.fontFamily
                        font.pixelSize: 12
                    }
                    Row {
                        spacing: 8
                        PlainButton { text: "搬过去"; onClicked: win.runMove(true) }
                        PlainButton { text: "不搬，只换位置"; onClicked: win.runMove(false) }
                        PlainButton { text: "取消"; onClicked: { win.askMove = false; win.pathText = win.info.libraryPath; } }
                    }
                }
            }

            Note { text: win.status; visible: win.status !== ""; width: parent.width }

            Item { width: 1; height: 8 }
            Heading { text: "保存时间" }
            Flow {
                width: parent.width
                spacing: 14
                Repeater {
                    model: win.info.retentionOptions || []
                    Choice {
                        required property var modelData
                        accent: win.accent
                        text: modelData.label
                        checked: win.info.retentionDays === modelData.days
                        onPicked: win.setValue("RetentionDays", modelData.days)
                    }
                }
            }
            Note { text: "收藏的和挂在绳上的不会被清理，过期的进回收站。"; width: parent.width }
            Note { text: "图库 " + (win.info.count || 0) + " 张 · 占用 " + win.formatSize(win.info.occupied || 0); width: parent.width }
            Row {
                spacing: 8
                PlainButton {
                    text: sweepNow.running ? "清理中…" : "立即清理过期"
                    enabled: !win.busy && !sweepNow.running
                    onClicked: {
                        win.cleanResult = "";
                        win.busy = true;
                        sweepNow.command = ["python3", win.shell.helper, "sweep"].concat(win.shell.livePaths());
                        sweepNow.running = true;
                    }
                }
                PlainButton { text: "打开文件夹"; onClicked: win.shell.openLibrary() }
                Note { text: win.cleanResult; anchors.verticalCenter: parent.verticalCenter }
            }

            // ---- 通用 ----
            Item { width: 1; height: 12 }
            Heading { text: "自动挂上" }
            Repeater {
                model: win.info.collectOptions || []
                Choice {
                    required property var modelData
                    accent: win.accent
                    text: modelData.label
                    checked: win.info.collect === modelData.value
                    onPicked: win.setValue("Collect", modelData.value)
                }
            }
            Note { text: "拖进来的和用快捷键挂上的不受这项影响。"; width: parent.width }

            Item { width: 1; height: 12 }
            Heading { text: "主题" }
            Repeater {
                model: win.info.themes || []
                Choice {
                    required property var modelData
                    accent: win.accent
                    text: modelData.name
                    checked: win.shell.theme.themeId === modelData.id
                    onPicked: win.shell.theme.setTheme(modelData.id)
                }
            }
        }
    }
}
