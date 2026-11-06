import QtQuick
import Quickshell
import Quickshell.Io
import "layout.js" as L

// 系图的后台：绳上条目、图库导入、截图文件夹与剪贴板监听、图库窗口、ipc。
// 不建抽屉或绳子窗口；界面由外面的壳（本目录 shell.qml，或 Caelestia 外壳的 xitu 模块）
// 接 popRequested / peekRequested / toggleRequested 自己决定在哪块屏上显示。
// dir 是本目录的真实路径；经软链接加载时 Qt.resolvedUrl 指向链接处，由外面传进来。
Scope {
    id: root

    property string dir: Qt.resolvedUrl(".").toString().replace("file://", "").replace(/\/$/, "")
    readonly property string helper: dir + "/xitu_helper.py"
    readonly property string home: Quickshell.env("HOME")

    property bool followCaelestia: true
    signal popRequested(string path)   // 新图到达：聚焦屏展开并播入场动画
    signal peekRequested()             // 挂回几张：聚焦屏展开一会儿
    signal toggleRequested()           // 快捷键
    signal quitRequested()             // ipc quit：独立运行时退出；并进 Caelestia 时不理，免得把整个外壳关掉

    ThemeEngine {
        id: themeEngine
        helper: root.helper
        home: root.home
        followCaelestia: root.followCaelestia
    }
    property alias theme: themeEngine

    // 聚焦屏、全屏判断（Hyprland / Sway / 其他 wlroots 系 / 兜底），见 Desktop.qml
    Desktop { id: desktop }
    property alias desktop: desktop
    Component.onCompleted: log("桌面后端：" + desktop.kind)

    property int version: 0
    property int fallingCount: 0
    property bool restored: false
    property int libraryTick: 0

    ListModel { id: lineModel }
    property alias line: lineModel

    readonly property int capacity: {
        var n = L.MaxItemsCap;
        for (var i = 0; i < Quickshell.screens.length; i++)
            n = Math.min(n, L.capacity(Quickshell.screens[i].width));
        return n;
    }

    function log(msg) { console.log("[xitu] " + msg); }

    function uid() { return Date.now().toString(36) + Math.random().toString(36).slice(2, 8); }

    function livePaths() {
        var out = [];
        for (var i = 0; i < lineModel.count; i++) {
            var e = lineModel.get(i);
            if (!e.gone) out.push(e.path);
        }
        return out;
    }

    function save() {
        Quickshell.execDetached(["python3", helper, "save-state"].concat(livePaths()));
    }

    // 挂上一张（已经在图库里的）图。quiet：恢复时不弹出绳子
    function hang(path, quiet) {
        path = String(path).trim();
        if (path === "") return;
        if (livePaths().indexOf(path) >= 0) {
            log("已在绳上，跳过 " + path);
            return;
        }
        lineModel.append({ entryId: uid(), path: path, tilt: Math.random() * 5 - 2.5, gone: false });
        var live = livePaths();
        while (live.length > capacity) {
            log("超出容量，取下最老的 " + live[0]);
            takeOffPath(live[0]);
            live = livePaths();
        }
        version++;
        if (!quiet) libraryTick++;
        if (restored) save();
        if (!quiet) {
            log("挂上 " + path);
            popNew(path);
        }
    }

    function indexOfId(id) {
        for (var i = 0; i < lineModel.count; i++)
            if (lineModel.get(i).entryId === id) return i;
        return -1;
    }

    // 从绳上拿掉，不删文件。卡片先播下落，播完再从列表移除。
    function takeOff(i) {
        if (i < 0 || lineModel.get(i).gone) return;
        lineModel.setProperty(i, "gone", true);
        fallingCount++;
        var id = lineModel.get(i).entryId;
        purgeLater.createObject(root, { entryId: id });
    }

    function takeOffPath(path) {
        for (var i = 0; i < lineModel.count; i++) {
            var e = lineModel.get(i);
            if (!e.gone && e.path === path) { takeOff(i); return; }
        }
    }

    function discard(id) {
        var i = indexOfId(id);
        if (i < 0) return;
        log("取下 " + lineModel.get(i).path);
        takeOff(i);
        version++;
        save();
    }

    // 清空绳子（Windows 托盘「清空绳子」）。文件留在图库。
    function clear() {
        var live = livePaths();
        if (live.length === 0) return;
        for (var i = 0; i < lineModel.count; i++)
            if (!lineModel.get(i).gone) takeOff(i);
        version++;
        save();
        log("清空绳子");
    }

    function moveEntry(id, slot) {
        var from = indexOfId(id);
        if (from < 0) return;
        var liveIdx = [];
        for (var i = 0; i < lineModel.count; i++)
            if (!lineModel.get(i).gone) liveIdx.push(i);
        slot = Math.max(0, Math.min(liveIdx.length - 1, slot));
        var to = liveIdx[slot];
        if (to === from) return;
        lineModel.move(from, to, 1);
        version++;
        save();
        log("换位置 → " + slot);
    }

    function copyFile(path) { Quickshell.execDetached(["python3", helper, "copy", path]); }
    function openFile(path) { Quickshell.execDetached(["xdg-open", path]); }
    function revealFile(path) { Quickshell.execDetached(["python3", helper, "reveal", path]); }

    // 另存为：助手弹 zenity / kdialog 的保存对话框，复制一份过去（同 Windows GrabArea.SaveAs）
    function saveAs(path) { saveAsJob.createObject(root, { file: path }); }

    Component {
        id: saveAsJob
        Process {
            id: saveProc
            property string file
            running: true
            command: ["python3", root.helper, "save-as", file]
            stdout: StdioCollector { onStreamFinished: if (text.trim() !== "") root.log("另存为 " + text.trim()) }
            stderr: StdioCollector {
                onStreamFinished: {
                    var msg = text.trim();
                    if (msg === "") return;
                    root.log(msg);
                    Quickshell.execDetached(["notify-send", "-a", "系图", "另存为", msg]);
                }
            }
            onExited: saveProc.destroy()
        }
    }

    // ---- 编辑：同 Windows 长按 / 「用画图编辑」。编辑器存回原文件后刷新卡片缩略图 ----
    property var thumbRevs: ({})
    property int thumbTick: 0
    property var editWatches: ({})

    function thumbRev(path) { return thumbTick >= 0 ? (thumbRevs[path] || 0) : 0; }

    function bumpThumb(path) {
        thumbRevs[path] = (thumbRevs[path] || 0) + 1;
        thumbTick++;
        libraryTick++;
        log("编辑后刷新 " + path);
    }

    function editFile(path) {
        editJob.createObject(root, { file: path });
        if (editWatches[path]) { editWatches[path].restartLife(); return; }
        editWatches[path] = editWatch.createObject(root, { file: path });
    }

    Component {
        id: editJob
        Process {
            id: editProc
            property string file
            running: true
            command: ["python3", root.helper, "edit", file]
            stdout: StdioCollector { onStreamFinished: if (text.trim() !== "") root.log("编辑器 " + text.trim()) }
            stderr: StdioCollector { onStreamFinished: if (text.trim() !== "") root.log(text.trim()) }
            onExited: editProc.destroy()
        }
    }

    // 盯着图片所在的文件夹（编辑器常先写临时文件再改名），名字对上就刷新。一小时后不再盯
    Component {
        id: editWatch
        Scope {
            id: watchScope
            property string file
            readonly property string dirName: file.substring(0, file.lastIndexOf("/"))
            readonly property string baseName: file.substring(file.lastIndexOf("/") + 1)
            function restartLife() { life.restart(); }
            Process {
                running: true
                command: ["python3", root.helper, "run", "inotifywait", "-m", "-q", "-e", "close_write,moved_to", "--format", "%f", watchScope.dirName]
                stdout: SplitParser {
                    onRead: line => { if (line === watchScope.baseName) settle.restart(); }
                }
            }
            // 编辑器一次保存可能连写几下，停 300 毫秒再刷新
            Timer { id: settle; interval: 300; onTriggered: root.bumpThumb(watchScope.file) }
            Timer {
                id: life
                interval: 3600 * 1000
                running: true
                onTriggered: {
                    delete root.editWatches[watchScope.file];
                    watchScope.destroy();
                }
            }
        }
    }

    // 收藏移动之后绳上的路径要跟着改，否则卡片还指着旧文件。tick 为 false 时先不通知图库，一批做完再通知。
    function relocate(oldPath, newPath, tick) {
        if (newPath && oldPath !== newPath) {
            var changed = false;
            for (var i = 0; i < lineModel.count; i++) {
                if (lineModel.get(i).path === oldPath) {
                    lineModel.setProperty(i, "path", newPath);
                    changed = true;
                }
            }
            if (changed) {
                version++;
                if (restored) save();
                log("绳上路径改为 " + newPath);
            }
        }
        if (tick !== false) libraryTick++;
    }

    function setFavorite(path, on) {
        setFavorites([{ path: path, want: !!on }]);
    }

    function setFavorites(pairs) {
        if (!pairs || pairs.length === 0) return;
        var first = pairs[0];
        favJob.createObject(root, {
            file: first.path,
            flag: first.want ? "1" : "0",
            rest: pairs.slice(1),
        });
    }

    function trashOwned(paths) {
        if (!paths || paths.length === 0) return;
        trashJob.createObject(root, { file: paths[0], rest: paths.slice(1) });
    }

    // 挂回绳上。已经在绳上的跳过，最后统一展开一次。
    function hangBack(paths) {
        for (var i = 0; i < paths.length; i++) hang(paths[i], true);
        peekRequested();
    }

    function openGallery() { gallery.toggle(); }
    function showGallery(screen) { gallery.showOn(screen); }
    function closeGallery() { gallery.requestClose(); }
    // 设置是普通窗口，图库浮层在它上面一层，先关图库
    function openSettings() { gallery.requestClose("settings"); settingsWin.show(); }

    function popNew(path) { popRequested(path); }

    Component {
        id: purgeLater
        Timer {
            property string entryId
            interval: L.FallSeconds * 1000 + 80
            running: true
            onTriggered: {
                var i = root.indexOfId(entryId);
                if (i >= 0) lineModel.remove(i, 1);
                root.fallingCount = Math.max(0, root.fallingCount - 1);
                root.version++;
                destroy();
            }
        }
    }

    // 把一个文件交给助手进图库，成功后挂上
    Component {
        id: importJob
        Process {
            property string file
            property string source
            property bool fromFolder: false
            running: true
            command: fromFolder ? ["python3", root.helper, "auto-import", file]
                          : ["python3", root.helper, "import", file, source]
            stdout: StdioCollector {
                onStreamFinished: {
                    var p = text.trim();
                    if (p !== "") root.hang(p, false);
                }
            }
            stderr: StdioCollector {
                onStreamFinished: if (text.trim() !== "") root.log(text.trim());
            }
            onExited: destroy()
        }
    }

    // ---- 自带截图：同 Windows Ctrl+Shift+A。快捷键由合成器的绑定调 ipc capture（install.sh 写好 Hyprland / Sway 的） ----
    // 壳把 flyHandler 设成一个函数(shot) → bool：接手飞行动画就返回 true，落地后自己调 hang
    property var flyHandler: null
    function flightHandled(shot) {
        if (!flyHandler) return false;
        try { return !!flyHandler(shot); } catch (e) { log("飞行失败：" + e); return false; }
    }

    function capture() {
        if (captureProc.running) return;
        captureProc.running = true;
    }

    Process {
        id: captureProc
        command: ["python3", root.helper, "capture"]
        stdout: StdioCollector {
            onStreamFinished: {
                var t = text.trim();
                if (t === "") return;
                var shot;
                try { shot = JSON.parse(t); } catch (e) { root.log("capture 读不懂：" + t); return; }
                if (!shot.path) return;
                // 外面的壳接了 flightRequested 就从框选处飞上绳子，落下再挂；没人接就直接挂
                if (shot.w > 0 && shot.h > 0 && root.flightHandled(shot)) return;
                root.hang(shot.path, false);
            }
        }
        stderr: StdioCollector {
            onStreamFinished: {
                var msg = text.trim();
                if (msg === "") return;
                root.log(msg);
                Quickshell.execDetached(["notify-send", "-a", "系图", "系图截图", msg]);
            }
        }
    }

    function importFile(file, source, fromFolder) {
        importJob.createObject(root, { file: file, source: source, fromFolder: !!fromFolder });
    }

    Component {
        id: fetchJob
        Process {
            property string url
            running: true
            command: ["python3", root.helper, "fetch", url]
            stdout: StdioCollector {
                onStreamFinished: {
                    var p = text.trim();
                    if (p !== "") root.hang(p, false);
                }
            }
            stderr: StdioCollector {
                onStreamFinished: if (text.trim() !== "") root.log(text.trim());
            }
            onExited: destroy()
        }
    }

    Component {
        id: b64Job
        Process {
            property string payload
            stdinEnabled: true
            running: true
            command: ["python3", root.helper, "import-b64", "拖入"]
            stdout: StdioCollector {
                onStreamFinished: {
                    var p = text.trim();
                    if (p !== "") root.hang(p, false);
                }
            }
            stderr: StdioCollector {
                onStreamFinished: if (text.trim() !== "") root.log(text.trim());
            }
            onStarted: {
                write(payload);
                stdinEnabled = false;
            }
            onExited: destroy()
        }
    }

    function isImagePath(path) {
        return /\.(png|jpe?g|bmp|gif|webp|tiff?)$/i.test(String(path));
    }

    function fileUrlToPath(u) {
        var m = /^file:\/\/(?:localhost)?(\/.*)$/i.exec(String(u));
        if (!m) return "";
        try { return decodeURIComponent(m[1]); } catch (e) { return m[1]; }
    }

    // 拖进绳子的一批地址。有本地图片就只进那些；否则图片网址走 fetch；
    // 都没有再收 image/* 字节。返回有没有收下一件。
    function ingestDrop(urls, b64) {
        var files = [];
        var remote = [];
        var dataB64 = "";
        for (var i = 0; i < urls.length; i++) {
            var u = String(urls[i]).trim();
            if (u === "" || u.charAt(0) === "#") continue;
            if (/^file:/i.test(u)) {
                var path = fileUrlToPath(u);
                if (path !== "" && isImagePath(path)) files.push(path);
                else log("拖入跳过非图片 " + (path || u));
            } else if (/^https?:\/\//i.test(u)) {
                remote.push(u);
            } else if (/^data:image\//i.test(u) && dataB64 === "") {
                var comma = u.indexOf(",");
                if (comma > 0 && /;base64/i.test(u.slice(0, comma)))
                    dataB64 = u.slice(comma + 1).replace(/\s/g, "");
            }
        }
        if (files.length > 0) {
            for (var f = 0; f < files.length; f++) importFile(files[f], "拖入");
            return true;
        }
        var imageRemote = [];
        for (var r = 0; r < remote.length; r++) {
            var bare = remote[r].split("?")[0].split("#")[0];
            if (isImagePath(bare)) imageRemote.push(remote[r]);
        }
        if (imageRemote.length > 0) {
            for (var a = 0; a < imageRemote.length; a++) {
                log("拖入网址 " + imageRemote[a]);
                fetchJob.createObject(root, { url: imageRemote[a] });
            }
            return true;
        }
        var payload = b64 || dataB64;
        if (payload !== "") {
            log("拖入图像数据");
            b64Job.createObject(root, { payload: payload });
            return true;
        }
        if (remote.length > 0) {
            for (var b = 0; b < remote.length; b++) {
                log("拖入网址 " + remote[b]);
                fetchJob.createObject(root, { url: remote[b] });
            }
            return true;
        }
        return false;
    }

    function openLibrary() {
        if (libRoot.running) return;
        libRoot.running = true;
    }

    // ---- 启动：恢复上次绳上的照片 ----
    Process {
        running: true
        command: ["python3", root.helper, "load-state"]
        stdout: StdioCollector {
            onStreamFinished: {
                var lines = text.split("\n");
                for (var i = 0; i < lines.length; i++)
                    if (lines[i].trim() !== "") root.hang(lines[i], true);
                root.restored = true;
                root.log("恢复 " + root.livePaths().length + " 张");
            }
        }
    }

    // ---- 启动时查一遍依赖：缺必需的（wl-clipboard、inotify-tools 等）弹一次通知，见 xitu_helper.py check-deps ----
    Process {
        running: true
        command: ["python3", root.helper, "check-deps", "--notify"]
    }

    // ---- 截图文件夹：新文件进图库并挂上 ----
    Process {
        id: folderWatch
        running: true
        // 文件夹由助手定（XITU_SCREENSHOT_DIR、XDG_SCREENSHOTS_DIR，或「图片」下的 Screenshots / 屏幕截图 和「图片」根目录），
        // 只盯已有的，不替用户建文件夹。见 xitu_helper.py screenshot-dirs / watch-shots
        command: ["python3", root.helper, "run", "python3", root.helper, "watch-shots"]
        stdout: SplitParser {
            onRead: line => {
                // 走 auto-import：设置 Collect 为 own 时助手不收
                if (/\.(png|jpe?g|webp)$/i.test(line)) root.importFile(line, "截图", true);
            }
        }
        onExited: restartFolder.start()
    }
    Timer { id: restartFolder; interval: 5000; onTriggered: folderWatch.running = true }

    // ---- 剪贴板：每次变化助手看一眼，是图片（且不是自己刚复制的、不带文字格式）才进图库 ----
    Process {
        id: clipWatch
        running: true
        command: ["python3", root.helper, "run", "wl-paste", "--watch", "python3", root.helper, "clip"]
        stdout: SplitParser {
            // wl-paste --watch 一启动就会对现有内容跑一次，那张不算新截图
            onRead: line => { if (clipArmed.armed) root.hang(line, false); }
        }
        onExited: restartClip.start()
    }
    Timer { id: restartClip; interval: 5000; onTriggered: { clipArmed.armed = false; clipArmed.restart(); clipWatch.running = true; } }
    Timer { id: clipArmed; property bool armed: false; interval: 2000; running: true; onTriggered: armed = true }

    // ---- 文件被删或移走：从绳上取下 ----
    Timer {
        interval: 3000
        running: root.restored
        repeat: true
        onTriggered: {
            var paths = root.livePaths();
            if (paths.length === 0) return;
            pruneProc.command = ["python3", root.helper, "missing"].concat(paths);
            pruneProc.running = true;
        }
    }
    Process {
        id: pruneProc
        stdout: StdioCollector {
            onStreamFinished: {
                var gone = text.split("\n").filter(s => s.trim() !== "");
                if (gone.length === 0) return;
                for (var i = 0; i < gone.length; i++) {
                    root.log("文件不见了，取下 " + gone[i]);
                    root.takeOffPath(gone[i]);
                }
                root.version++;
                root.save();
            }
        }
    }

    // ---- 过期清理：同 Windows 版 Retention.cs，启动 1 分钟后一次，之后每 6 小时 ----
    Timer {
        id: sweepTimer
        interval: 60 * 1000
        running: root.restored
        repeat: true
        onTriggered: {
            interval = 6 * 3600 * 1000;
            if (sweepProc.running) return;
            sweepProc.command = ["python3", root.helper, "sweep"].concat(root.livePaths());
            sweepProc.running = true;
        }
    }
    Process {
        id: sweepProc
        stdout: StdioCollector {
            onStreamFinished: {
                var n = parseInt(text.trim());
                if (n > 0) {
                    root.log("清理过期 " + n + " 张");
                    root.libraryTick++;
                }
            }
        }
        stderr: StdioCollector {
            onStreamFinished: if (text.trim() !== "") root.log(text.trim());
        }
    }

    // 打开图库文件夹。Quickshell 0.3.2 的 SystemTray 只能显示别人的托盘项，发不出自己的。
    Process {
        id: libRoot
        command: ["python3", root.helper, "library-root"]
        stdout: StdioCollector {
            onStreamFinished: {
                var p = text.trim();
                if (p !== "") Quickshell.execDetached(["sh", "-c", "mkdir -p \"$1\" && exec xdg-open \"$1\"", "sh", p]);
            }
        }
    }

    // ---- 快捷键入口：qs ipc -p <本目录> call xitu toggle（并进 Caelestia 时 qs -c caelestia ipc call xitu toggle）----
    // 托盘菜单做不了，展开 / 打开图库 / 退出由顶栏或速查表调这三个函数。
    IpcHandler {
        target: "xitu"
        function toggle(): void { root.toggleRequested(); }
        function peek(): void { root.peekRequested(); }
        function clear(): void { root.clear(); }
        function hang(path: string): void { root.importFile(path, "文件"); }
        function openLibrary(): void { root.openLibrary(); }
        function gallery(): void { root.openGallery(); }
        function closeGallery(): void { root.closeGallery(); }
        function settings(): void { root.openSettings(); }
        function capture(): void { root.capture(); }
        function quit(): void { root.quitRequested(); }
        function setTheme(id: string): void { themeEngine.setTheme(id); }
        function themes(): string { return themeEngine.themes(); }
        function nextTheme(): void { themeEngine.nextTheme(); }
    }

    GalleryWindow {
        id: gallery
        shell: root
    }

    SettingsWindow {
        id: settingsWin
        shell: root
    }

    Component {
        id: favJob
        Process {
            id: favProc
            property string file
            property string flag
            property var rest: []
            running: true
            command: ["python3", root.helper, "favorite", file, flag]
            stdout: StdioCollector {
                onStreamFinished: {
                    var p = text.trim();
                    if (p !== "") root.relocate(favProc.file, p, false);
                }
            }
            stderr: StdioCollector {
                onStreamFinished: if (text.trim() !== "") root.log(text.trim());
            }
            onExited: {
                var more = favProc.rest;
                if (more && more.length > 0) {
                    favJob.createObject(root, {
                        file: more[0].path,
                        flag: more[0].want ? "1" : "0",
                        rest: more.slice(1),
                    });
                } else {
                    root.libraryTick++;
                }
                destroy();
            }
        }
    }

    Component {
        id: trashJob
        Process {
            id: trashProc
            property string file
            property var rest: []
            running: true
            command: ["python3", root.helper, "trash", file]
            stdout: StdioCollector {
                onStreamFinished: {
                    if (text.trim() !== "") root.takeOffPath(trashProc.file);
                }
            }
            stderr: StdioCollector {
                onStreamFinished: if (text.trim() !== "") root.log(text.trim());
            }
            onExited: {
                var more = trashProc.rest;
                if (more && more.length > 0)
                    trashJob.createObject(root, { file: more[0], rest: more.slice(1) });
                else
                    root.libraryTick++;
                destroy();
            }
        }
    }
}
