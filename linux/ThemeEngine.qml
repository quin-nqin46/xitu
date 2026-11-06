import QtQuick
import Quickshell
import Quickshell.Io

// 主题：内置 shared/themes/<id>.json（和 Windows 版共用），用户 ~/.local/share/Xitu/themes/<id>.json（同 id 用户优先）。
// 颜色字面量在这里解析。"accent" 可以跟着一份外部 qml 里的 activeWorkspaceBackground，明暗看另一份
// env 的 theme=（环境变量 XITU_ACCENT_QML / XITU_ACCENT_STATE）；两个都没设或读不到，用主题里的 accent（默认 #3D6FE0）。
// "accent-strong" 用同一色相，饱和度不低于 0.65，HSL 亮度高于 0.52 时压到 0.52。
Item {
    id: engine

    required property string helper
    required property string home

    property string themeId: "polaroid-blue"
    property string themeName: "主题蓝绳 + 拍立得"
    property var spec: ({})
    property bool specReady: false
    property int rev: 0

    property color accent: "#3D6FE0"
    property color accentStrong: "#2C62DD"
    property color scratch: "#3D6FE0"
    property color galleryBg: "#F6F1E7"
    property bool accentReady: false
    property string accentKey: ""
    property string accentNote: ""

    property color ropeColor: "#3D6FE0"
    property real ropeWidth: 3
    property color ropeShadow: Qt.rgba(20 / 255, 30 / 255, 60 / 255, 0.20)
    property real ropeShadowWidth: 4
    property real ropeShadowOffset: 2
    property string ropeTexture: "none"

    property string endStyle: "pin"
    property real endInset: 40
    property color pinColor: "#FFFFFF"
    property color pinRing: Qt.rgba(30 / 255, 40 / 255, 70 / 255, 0.20)
    property color dotColor: "#3D6FE0"
    property real endRadius: 8
    property real dotRadius: 3.5

    property color clipColor: "#3D6FE0"
    property string clipCss: "#3D6FE0"
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

    property color framePaper: "#FFFFFF"
    property real padLeft: 7
    property real padTop: 7
    property real padRight: 7
    property real padBottom: 22
    property real frameRadius: 4
    property real photoRadius: 2

    property color hintBg: "#FFFFFF"
    property color hintFg: "#2E3440"
    property color hintBorder: Qt.rgba(30 / 255, 40 / 255, 70 / 255, 0.20)
    property bool hintBorderOn: true

    // 抽屉界面（DrawerWindow）的配色。主题 caelestia 时从 ~/.local/state/caelestia/scheme.json
    // 实时读（colours 里不带 # 的 hex），文件读不到就用下面这份内置默认色（m3 深色）。
    readonly property var schemeDefaults: ({
        surface: "#0c0e12",
        surfaceContainer: "#161a1f",
        onSurface: "#e1e5ef",
        onSurfaceVariant: "#a7abb4",
        primary: "#afc9eb",
        outline: "#71767e",
        outlineVariant: "#434850"
    })
    property var scheme: ({})
    // 抽屉界面一律跟随 Caelestia 配色（由 shell.qml 按 uiMode 设置），不看当前绳子主题
    property bool followCaelestia: false
    onFollowCaelestiaChanged: if (specReady) applyDrawer()
    property bool schemeReady: false

    property color drawerSurface: "#FFFFFF"
    property color drawerSurfaceHigh: "#FFFFFF"
    property color drawerFg: "#2E3440"
    property color drawerFgDim: "#2E3440"
    property color drawerPrimary: "#3D6FE0"
    property color drawerOutline: Qt.rgba(30 / 255, 40 / 255, 70 / 255, 0.20)

    property string themeLines: "polaroid-blue 主题蓝绳 + 拍立得\nred-rope 红绳\ngraphite 石墨细线\nlinen 柔和麻绳"
    property var queue: []
    property bool busy: false

    readonly property string stateDir: {
        var e = Quickshell.env("XDG_STATE_HOME");
        return (e && e !== "") ? e : (home + "/.local/state");
    }

    // accent 外部来源：环境变量非空才读对应文件；为空时不拼任何默认路径，按「读不到」处理。
    readonly property string accentQmlPath: {
        var v = Quickshell.env("XITU_ACCENT_QML");
        return (v && v !== "") ? v : "";
    }
    readonly property string accentStatePath: {
        var v = Quickshell.env("XITU_ACCENT_STATE");
        return (v && v !== "") ? v : "";
    }

    function log(msg) { console.log("[xitu] " + msg); }

    function fallbackAccent() {
        var a = spec && spec.accent ? String(spec.accent) : "";
        if (a !== "" && a !== "accent" && a !== "accent-strong")
            return a;
        return "#3D6FE0";
    }

    function sourceAccent() {
        return accentReady ? accent : fallbackAccent();
    }

    // 色相不动。饱和度低于 0.65 就提到 0.65；亮度高于 0.52 就压到 0.52，更暗则保持。
    function strengthen(input) {
        scratch = input;
        var hue = scratch.hslHue;
        var sat = scratch.hslSaturation;
        var lit = scratch.hslLightness;
        if (hue < 0)
            hue = 0;
        if (sat < 0.65)
            sat = 0.65;
        if (lit > 0.52)
            lit = 0.52;
        return Qt.hsla(hue, sat, lit, 1);
    }

    function paint(v) {
        if (v === "accent")
            return sourceAccent();
        if (v === "accent-strong")
            return accentStrong;
        if (v === undefined || v === null || String(v) === "")
            return "transparent";
        var m = /^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([0-9.]+)\s*)?\)$/.exec(String(v).trim());
        if (!m)
            return v;
        var a = m[4] === undefined ? 1 : Number(m[4]);
        return Qt.rgba(Number(m[1]) / 255, Number(m[2]) / 255, Number(m[3]) / 255, a);
    }

    function cssOf(c) {
        var col = paint(c);
        if (typeof col === "string")
            return col;
        return String(col);
    }

    function applySpec() {
        var s = spec;
        if (!s || !s.rope)
            return;
        accentStrong = strengthen(sourceAccent());
        ropeColor = paint(s.rope.color);
        ropeWidth = s.rope.width;
        ropeShadow = paint(s.rope.shadowColor);
        ropeShadowWidth = s.rope.shadowWidth;
        ropeShadowOffset = s.rope.shadowOffset;
        ropeTexture = s.rope.texture;

        endStyle = s.ends.style;
        endInset = s.ends.inset;
        pinColor = paint(s.ends.pinColor);
        pinRing = paint(s.ends.pinRing);
        dotColor = paint(s.ends.dotColor);
        endRadius = s.ends.radius;
        dotRadius = s.ends.dotRadius;

        clipColor = paint(s.clip.color);
        clipCss = cssOf(s.clip.color);
        clipWidth = s.clip.width;
        clipHeight = s.clip.height;
        clipRadius = s.clip.radius;
        clipBorderOn = String(s.clip.border || "") !== "";
        clipBorder = clipBorderOn ? paint(s.clip.border) : "transparent";
        highlightOn = String(s.clip.highlight || "") !== "";
        highlightColor = highlightOn ? paint(s.clip.highlight) : "transparent";
        highlightWidth = s.clip.highlightWidth;
        highlightHeight = s.clip.highlightHeight;
        highlightRadius = s.clip.highlightRadius;
        highlightShift = s.clip.highlightShift;
        clipAbove = s.clip.above;
        clipBelow = s.clip.below;
        clipShadow = !!s.clip.shadow;

        framePaper = paint(s.frame.paper);
        padLeft = s.frame.padLeft;
        padTop = s.frame.padTop;
        padRight = s.frame.padRight;
        padBottom = s.frame.padBottom;
        frameRadius = s.frame.radius;
        photoRadius = s.frame.photoRadius;

        hintBg = paint(s.hint.bg);
        hintFg = paint(s.hint.fg);
        hintBorderOn = String(s.hint.border || "") !== "";
        hintBorder = hintBorderOn ? paint(s.hint.border) : "transparent";
        var gbg = (s.gallery && s.gallery.bg) ? s.gallery.bg : "#F6F1E7";
        galleryBg = paint(gbg);
        applyDrawer();
        rev++;
    }

    // 非 caelestia 主题的抽屉沿用纸面/墨色，保证任何主题下抽屉都协调。
    function applyDrawer() {
        if (themeId === "caelestia" || followCaelestia) {
            drawerSurface = schemeColor("surface");
            drawerSurfaceHigh = schemeColor("surfaceContainer");
            drawerFg = schemeColor("onSurface");
            drawerFgDim = schemeColor("onSurfaceVariant");
            drawerPrimary = schemeColor("primary");
            drawerOutline = schemeColor("outlineVariant");
        } else {
            drawerSurface = framePaper;
            drawerSurfaceHigh = hintBg;
            drawerFg = hintFg;
            drawerFgDim = hintBorderOn ? hintBorder : hintFg;
            drawerPrimary = ropeColor;
            drawerOutline = hintBorderOn ? hintBorder : hintFg;
        }
    }

    function schemeColor(key) {
        if (schemeReady && scheme[key])
            return paint("#" + scheme[key]);
        return paint(schemeDefaults[key] || "transparent");
    }

    function applyScheme(text) {
        var data;
        try { data = JSON.parse(text); }
        catch (e) {
            scheme = ({});
            schemeReady = false;
            log("scheme.json 解析失败，抽屉用内置默认色");
            if (specReady) applyDrawer();
            return;
        }
        var c = data && data.colours;
        if (!c || typeof c !== "object") {
            scheme = ({});
            schemeReady = false;
            log("scheme.json 里没有 colours，抽屉用内置默认色");
        } else {
            scheme = c;
            schemeReady = true;
        }
        if (specReady) applyDrawer();
    }

    function applyJson(text) {
        var data;
        try { data = JSON.parse(text); }
        catch (e) {
            log("主题 JSON 解析失败，保持现状");
            return;
        }
        if (!data || !data.rope) {
            log("主题内容不完整，保持现状");
            return;
        }
        spec = data;
        specReady = true;
        themeId = data.id || themeId;
        themeName = data.name || themeName;
        applySpec();
        log("主题 " + themeId + " " + themeName);
    }

    function take(kind, text) {
        var body = String(text || "").trim();
        if (kind === "theme-list") {
            if (body !== "")
                themeLines = body;
            return;
        }
        if (body !== "")
            applyJson(body);
    }

    function enqueue(args) {
        var q = queue.slice();
        q.push(args);
        queue = q;
        pump();
    }

    function pump() {
        if (busy || queue.length === 0)
            return;
        busy = true;
        var args = queue[0];
        queue = queue.slice(1);
        themeJob.createObject(engine, { kind: args[0], args: args });
    }

    function jobDone() {
        busy = false;
        pump();
    }

    function setTheme(id) {
        enqueue(["theme-set", id]);
        enqueue(["theme-list"]);
    }

    function nextTheme() {
        enqueue(["theme-next"]);
        enqueue(["theme-list"]);
    }

    function themes() { return themeLines; }

    function updateAccent() {
        var qml = accentQmlPath === "" ? "" : accentQml.text();
        var state = accentStatePath === "" ? "" : accentState.text();
        var note = "";
        var next = "#3D6FE0";
        var ready = false;
        var m = /activeWorkspaceBackground\s*:\s*darkMode\s*\?\s*Qt\.darker\(\s*"([^"]+)"\s*,\s*([0-9.]+)\s*\)\s*:\s*"([^"]+)"/.exec(qml);
        var themeLine = /(?:^|\n)\s*theme=(light|dark)\s*(?:\n|$)/.exec(state);
        if (!m || !themeLine) {
            note = "读不到工作区高亮色，用 #3D6FE0";
        } else {
            var dark = themeLine[1] === "dark";
            if (dark)
                next = Qt.darker(m[1], Number(m[2]));
            else
                next = m[3];
            ready = true;
            note = (dark ? "深色 " : "浅色 ")
                + (typeof next === "string" ? next : String(next))
                + "，来自外部 qml 的 activeWorkspaceBackground"
                + "（" + (dark ? ("Qt.darker(" + m[1] + ", " + m[2] + ")") : m[3]) + "）"
                + "，明暗来自外部 env 的 theme=" + themeLine[1];
        }
        var key = String(next) + "|" + note;
        if (key === accentKey)
            return;
        accentKey = key;
        accent = next;
        accentReady = ready;
        accentNote = note;
        var strong = strengthen(accentReady ? accent : fallbackAccent());
        log("主题色 " + note + "；accent-strong " + strong);
        if (specReady)
            applySpec();
    }

    function reloadAccent() {
        if (accentQmlPath !== "")
            accentQml.reload();
        if (accentStatePath !== "")
            accentState.reload();
    }

    Component.onCompleted: {
        updateAccent();
        enqueue(["theme-current"]);
        enqueue(["theme-list"]);
    }

    FileView {
        id: accentQml
        path: engine.accentQmlPath
        blockLoading: true
        watchChanges: true
        printErrors: false
        onFileChanged: reload()
        onLoaded: engine.updateAccent()
    }

    FileView {
        id: accentState
        path: engine.accentStatePath
        blockLoading: true
        watchChanges: true
        printErrors: false
        onFileChanged: reload()
        onLoaded: engine.updateAccent()
    }

    FileView {
        id: schemeFile
        path: engine.stateDir + "/caelestia/scheme.json"
        blockLoading: true
        watchChanges: true
        printErrors: false
        onFileChanged: reload()
        onLoaded: {
            var body = "";
            try { body = text(); } catch (e) { body = ""; }
            engine.applyScheme(body);
        }
    }

    Timer {
        interval: 5000
        running: true
        repeat: true
        onTriggered: {
            engine.reloadAccent();
            schemeFile.reload();
        }
    }

    Component {
        id: themeJob
        Process {
            id: proc
            property string kind
            property var args: []
            running: true
            command: ["python3", engine.helper].concat(args)
            stdout: StdioCollector {
                onStreamFinished: engine.take(proc.kind, text)
            }
            stderr: StdioCollector {
                onStreamFinished: if (String(text).trim() !== "") engine.log(String(text).trim())
            }
            onExited: {
                engine.jobDone();
                destroy();
            }
        }
    }
}
