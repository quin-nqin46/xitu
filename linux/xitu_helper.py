#!/usr/bin/env python3
"""系图 Linux 版的文件与剪贴板助手。QML 做不了的事都在这里：进图库、去重、读写剪贴板。

子命令（每行输出一个结果，出错写 stderr、退出码非 0）：
  import <文件> <来源>   复制进图库（来源：截图/剪贴板/拖入/文件/其他），输出图库里的路径；内容相同的直接返回已有那张
  auto-import <文件>     截图文件夹来的新文件：按设置 Collect 决定收不收（own 时不收），收就同 import <文件> 截图
  import-b64 <来源>      从 stdin 读 base64 的图像字节，进图库（拖放里直接带的 image/*）
  fetch <url>            下载一张 http(s) 图片（超时 15 秒、上限 30 MB、只要 Content-Type image/*），
                         存临时文件后走 import，来源「拖入」。不跟随到非 http(s)
  clip                   给 `wl-paste --watch` 调用：剪贴板里是图片就进图库并输出路径，否则什么也不输出
  copy <文件>            把图片写进剪贴板，并记下来，免得 clip 又把它挂回绳上
  missing <文件>...      输出其中已经不存在的
  load-state / save-state <路径>...   读写 state.json（与 Windows 版格式相同）
  run <命令>...          设好「父进程退出就跟着退出」再 exec 这个命令。监听进程都经它启动，
                         否则 Quickshell 被杀后 wl-paste / inotifywait 会变成孤儿继续导入
  scan                   列出图库里系图自己的文件，一行 JSON：items（path/source/time/favorite/size，时间倒序）、
                         retention、occupied、capture。不读文件内容
  favorite <路径> <0|1>  收藏或取消。在「收藏」和对应月份文件夹之间移动，重名按原版另起 _n。只对 IsOwned 生效，输出新路径
  trash <路径>           把系图自己的文件放进回收站（gio trash）。别的路径拒绝，退出码 1
  sweep [绳上路径]...    过期清理（同 Windows Retention.cs）：超过 RetentionDays、没收藏、不在绳上的进回收站，
                         输出清理张数。RetentionDays 为 0（永久）时什么也不做
  reveal <路径>          在文件管理器中显示。只对 IsOwned 生效
  screenshot-dir         输出要盯的截图文件夹：$XITU_SCREENSHOT_DIR，否则「图片」目录下已有的
                         Screenshots 或 屏幕截图，都没有就用 Screenshots
  theme-current          输出当前主题（缺字段已补上）的一行 JSON
  theme-list             每行「id 显示名」
  theme-set <id>         把 settings.json 的 Theme 写成这个 id（保留其他字段），并输出主题 JSON
  theme-next             切到列表里的下一个主题，并输出主题 JSON
  settings-get           设置窗口用：一行 JSON，当前图库位置、保存时间、Collect、主题和各自的可选项，图库张数和占用
  settings-set <键> <值> 改 RetentionDays（7/30/90/365/0）或 Collect（all/screenshots/own），其他字段保留
  library-move <目录> <0|1>  换图库位置；1 时把系图自己的文件搬过去（同 Library.ChangeRoot），
                         输出一行 JSON：root、moved（[旧, 新] 路径对）
  pick-folder [起点]     用 zenity / kdialog 选文件夹，输出路径；取消时不输出，两个都没有时退出码 3

图库规则与 Windows 版一致（Library.cs）：根目录下 yyyy-MM 和「收藏」两种子文件夹，
文件名「来源_yyyyMMdd_HHmmss[_n].png」。只认这种文件，别的一概不碰。
"""

import hashlib
import io
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timedelta
from pathlib import Path

HOME = Path.home()
DATA = Path(os.environ.get("XDG_DATA_HOME", HOME / ".local/share")) / "Xitu"
STATE_FILE = DATA / "state.json"
SETTINGS_FILE = DATA / "settings.json"
HASH_CACHE = DATA / "hashes.json"
OWN_CLIP = DATA / "own-clipboard.json"
LOG_FILE = DATA / "log.txt"

LABELS = ("截图", "剪贴板", "拖入", "文件", "其他")
NAME_PATTERN = re.compile(r"^(截图|剪贴板|拖入|文件|其他)_(\d{8})_(\d{6})(?:_(\d+))?\.png$", re.I)
MONTH_FOLDER = re.compile(r"^\d{4}-\d{2}$")
FAVORITES = "收藏"
# 剪贴板里同时有这些文字格式时不当截图（WPS 复制单元格也会带位图）
RICH_TYPES = ("text/rtf", "application/rtf", "text/richtext")
# X11 风格的纯文本类型名，经 XWayland 复制时会出现
PLAIN_TEXT_ATOMS = ("utf8_string", "string", "text", "compound_text")
OWN_CLIP_SECONDS = 30
FETCH_TIMEOUT = 15
FETCH_MAX = 30 * 1024 * 1024


def log(msg):
    DATA.mkdir(parents=True, exist_ok=True)
    with open(LOG_FILE, "a", encoding="utf-8") as f:
        f.write(f"{datetime.now():%Y-%m-%d %H:%M:%S} [helper] {msg}\n")


def pictures_dir():
    try:
        out = subprocess.run(["xdg-user-dir", "PICTURES"], capture_output=True, text=True, timeout=3).stdout.strip()
        if out and out != str(HOME):
            return Path(out)
    except Exception:
        pass
    for name in ("Pictures", "图片"):
        if (HOME / name).is_dir():
            return HOME / name
    return HOME / "Pictures"


def screenshot_dir():
    env = os.environ.get("XITU_SCREENSHOT_DIR", "").strip()
    if env:
        return Path(env).expanduser()
    pics = pictures_dir()
    for name in ("Screenshots", "屏幕截图"):
        if (pics / name).is_dir():
            return pics / name
    return pics / "Screenshots"


def library_root():
    try:
        s = json.loads(SETTINGS_FILE.read_text(encoding="utf-8"))
        if s.get("LibraryPath"):
            return Path(s["LibraryPath"])
    except Exception:
        pass
    return pictures_dir() / "系图"


def owned_files(root):
    if not root.is_dir():
        return
    for d in root.iterdir():
        if not d.is_dir() or not (d.name == FAVORITES or MONTH_FOLDER.match(d.name)):
            continue
        for f in d.glob("*.png"):
            if NAME_PATTERN.match(f.name):
                yield f


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def load_hash_cache():
    try:
        return json.loads(HASH_CACHE.read_text(encoding="utf-8"))
    except Exception:
        return {}


def find_by_hash(root, digest):
    """图库里有没有内容相同的。按 路径+大小+修改时间 缓存哈希，免得每次全量读。"""
    cache = load_hash_cache()
    fresh = {}
    hit = None
    for f in owned_files(root):
        st = f.stat()
        key = str(f)
        sig = f"{st.st_size}:{int(st.st_mtime)}"
        entry = cache.get(key)
        if entry and entry.get("sig") == sig:
            h = entry["hash"]
        else:
            h = sha256_bytes(f.read_bytes())
        fresh[key] = {"sig": sig, "hash": h}
        if h == digest and hit is None:
            hit = f
    DATA.mkdir(parents=True, exist_ok=True)
    tmp = HASH_CACHE.with_suffix(".tmp")
    tmp.write_text(json.dumps(fresh, ensure_ascii=False), encoding="utf-8")
    tmp.replace(HASH_CACHE)
    return hit


class ImageDecodeError(Exception):
    """字节不是一张完整的图。调用方不进图库，退出码 1。"""


def to_png(data):
    """写入前完整解码一次。截断的 PNG 也有合法文件头，不能只看签名就原样保存。"""
    from PIL import Image
    try:
        with Image.open(io.BytesIO(data)) as im:
            im.load()
            if (im.format or "").upper() == "PNG":
                return data
            out = im
            if out.mode not in ("1", "L", "LA", "RGB", "RGBA", "P"):
                out = out.convert("RGBA")
            buf = io.BytesIO()
            out.save(buf, "PNG")
            return buf.getvalue()
    except Exception as e:
        log(f"图片解不开，不进图库：{e}")
        raise ImageDecodeError(str(e)) from e


def unique_named(directory, label, when):
    """目录里按「来源_时间戳」找一个空文件名。重名从 _2 起，和 Library.UniquePath 一样。"""
    directory.mkdir(parents=True, exist_ok=True)
    stamp = when.strftime("%Y%m%d_%H%M%S")
    p = directory / f"{label}_{stamp}.png"
    n = 2
    while p.exists():
        p = directory / f"{label}_{stamp}_{n}.png"
        n += 1
        if n >= 1000:
            p = directory / f"{label}_{stamp}_{time.time_ns()}.png"
            break
    return p


def unique_path(root, label, when):
    return unique_named(root / when.strftime("%Y-%m"), label, when)


def is_owned(path):
    p = Path(path).resolve()
    root = library_root().resolve()
    return (NAME_PATTERN.match(p.name) is not None
            and (p.parent.name == FAVORITES or MONTH_FOLDER.match(p.parent.name) is not None)
            and p.parent.parent == root)


def import_bytes(data, label, when=None):
    """截图键会同时写文件夹和剪贴板，两路几乎同时进来；加锁，免得两边都以为图库里还没有而各存一份。

    解不开时返回 None，不写文件。
    """
    import fcntl
    DATA.mkdir(parents=True, exist_ok=True)
    with open(DATA / "import.lock", "w") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            return _import_locked(data, label, when)
        except ImageDecodeError:
            return None


def _emit_imported(path):
    if path is None:
        print("图片解不开，不进图库", file=sys.stderr)
        return 1
    print(path)
    return 0


def _import_locked(data, label, when):
    if label not in LABELS:
        label = "其他"
    png = to_png(data)
    root = library_root()
    hit = find_by_hash(root, sha256_bytes(png))
    if hit:
        return hit
    p = unique_path(root, label, when or datetime.now())
    tmp = p.with_suffix(".tmp")
    tmp.write_bytes(png)
    tmp.replace(p)
    log(f"进图库 {p}")
    return p


def wait_stable(path, deadline=3.0):
    """截图工具可能还在写，等大小稳定（Windows 版每 150ms 读一次，最多 3 秒）。"""
    last = -1
    end = time.time() + deadline
    while time.time() < end:
        try:
            size = os.path.getsize(path)
        except OSError:
            size = -1
        if size > 0 and size == last:
            return True
        last = size
        time.sleep(0.15)
    return last > 0


class _HttpOnlyRedirect(urllib.request.HTTPRedirectHandler):
    """相对跳转照旧；跳到 file、ftp 这类协议就拒绝，不跟着走。"""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        scheme = urllib.parse.urlsplit(newurl).scheme
        if scheme and scheme not in ("http", "https"):
            raise urllib.error.URLError(f"拒绝跳转到 {scheme} 协议")
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def cmd_import_b64(label):
    import base64
    raw = sys.stdin.buffer.read()
    try:
        data = base64.b64decode(raw.strip(), validate=False)
    except Exception as e:
        print(f"图像数据解不开：{e}", file=sys.stderr)
        return 1
    if not data:
        print("图像数据是空的", file=sys.stderr)
        return 1
    if len(data) > FETCH_MAX:
        print("图片超过 30 MB", file=sys.stderr)
        return 1
    return _emit_imported(import_bytes(data, label or "拖入"))


def cmd_fetch(url):
    parts = urllib.parse.urlsplit(url)
    if parts.scheme not in ("http", "https") or not parts.netloc:
        print("只接受 http(s) 图片地址", file=sys.stderr)
        log(f"拒绝的地址 {url}")
        return 1
    opener = urllib.request.build_opener(_HttpOnlyRedirect)
    req = urllib.request.Request(url, headers={"User-Agent": "Xitu/1.0"})
    try:
        resp = opener.open(req, timeout=FETCH_TIMEOUT)
    except Exception as e:
        print(f"下载失败：{e}", file=sys.stderr)
        log(f"下载失败 {url}：{e}")
        return 1
    tmp = None
    try:
        with resp:
            final = resp.geturl()
            if urllib.parse.urlsplit(final).scheme not in ("http", "https"):
                print("拒绝跳转到非 http(s)", file=sys.stderr)
                log(f"拒绝的最终地址 {final}")
                return 1
            ctype = resp.headers.get("Content-Type", "").split(";")[0].strip().lower()
            if not ctype.startswith("image/"):
                print(f"不是图片：{ctype or '无 Content-Type'}", file=sys.stderr)
                log(f"不是图片 {url} Content-Type={ctype or '空'}")
                return 1
            declared = resp.headers.get("Content-Length")
            if declared:
                try:
                    if int(declared) > FETCH_MAX:
                        print("图片超过 30 MB", file=sys.stderr)
                        log(f"太大 {url} Content-Length={declared}")
                        return 1
                except ValueError:
                    pass
            chunks = []
            got = 0
            while True:
                block = resp.read(64 * 1024)
                if not block:
                    break
                got += len(block)
                if got > FETCH_MAX:
                    print("图片超过 30 MB", file=sys.stderr)
                    log(f"太大 {url} 已读 {got}")
                    return 1
                chunks.append(block)
        data = b"".join(chunks)
        if not data:
            print("空的图片", file=sys.stderr)
            return 1
        # 存临时文件再走 import，去重和来源命名跟拖本地文件同一条路
        tmp_dir = DATA / "fetch-tmp"
        tmp_dir.mkdir(parents=True, exist_ok=True)
        tmp = tmp_dir / f"fetch_{time.time_ns()}.bin"
        tmp.write_bytes(data)
        return cmd_import(str(tmp), "拖入")
    finally:
        if tmp is not None:
            try:
                tmp.unlink()
            except OSError:
                pass


def cmd_auto_import(path):
    """截图文件夹里的新文件。Collect 为 own 时不收，什么也不输出。"""
    if collect_mode(load_settings()) == "own":
        return 0
    return cmd_import(path, "截图")


def cmd_import(path, label):
    path = Path(path)
    if is_owned(path):
        print(path.resolve())
        return 0
    if not wait_stable(path):
        log(f"读不到稳定的文件 {path}")
        return 1
    when = datetime.fromtimestamp(path.stat().st_mtime)
    return _emit_imported(import_bytes(path.read_bytes(), label, when))


def clip_types():
    r = subprocess.run(["wl-paste", "--list-types"], capture_output=True, text=True, timeout=3)
    return [t.strip() for t in r.stdout.splitlines() if t.strip()]


def html_is_lone_image():
    """剪贴板的 text/html 是不是只有一张图（浏览器「复制图片」会附一个 <img> 标签）。
    WPS 复制单元格附带的是表格和文字，不算。读不到也不算。规则同 Windows 版 ScreenshotWatcher.HtmlIsLoneImage。"""
    try:
        html = subprocess.run(
            ["wl-paste", "--no-newline", "--type", "text/html"],
            capture_output=True, timeout=3,
        ).stdout[:200_000].decode("utf-8", "replace")
    except subprocess.TimeoutExpired:
        return False
    if not re.search(r"<img\b", html, re.I) or re.search(r"<(table|td|tr)\b", html, re.I):
        return False
    text = re.sub(r"<[^>]*>", "", html)
    text = re.sub(r"&nbsp;|&#160;", "", text)
    return text.strip() == ""


COLLECT_MODES = ("all", "screenshots", "own")


def collect_mode(settings):
    """设置 Collect：自动收哪些图。两端同名同义（Windows Settings.cs CollectMode）。
      all          截图文件夹、剪贴板截图、复制的图片（如浏览器「复制图片」）都收（默认）
      screenshots  只收截图文件夹和剪贴板截图，复制的图片不收
      own          只收系图自己截的图；截图文件夹和剪贴板都不看
    拖进来的、ipc hang 的不受影响。"""
    v = settings.get("Collect")
    return v if v in COLLECT_MODES else "all"


def clip_should_take(types, settings):
    """规则同 Windows 版 ScreenshotWatcher.ShouldTakeClipboard：
    只有图片格式的是截图，收。带文件列表的是复制文件，带 RTF 的是办公软件复制，都不收。
    带 HTML（可能还带文字）的：HTML 里只有一张图就是复制图片，Collect 为 all 才收；否则不收。
    Collect 为 own 时剪贴板一概不收。"""
    if collect_mode(settings) == "own":
        return False
    bases = [t.split(";")[0].strip().lower() for t in types]
    if not any(t.startswith("image/") for t in bases):
        return False
    if any(t == "text/uri-list" or t in RICH_TYPES for t in bases):
        return False
    has_html = "text/html" in bases
    has_text = any((t.startswith("text/") and t != "text/html") or t in PLAIN_TEXT_ATOMS for t in bases)
    if not has_html and not has_text:
        return True
    return has_html and collect_mode(settings) == "all" and html_is_lone_image()


def cmd_clip():
    types = clip_types()
    if not clip_should_take(types, load_settings()):
        return 0
    image = next((t for t in types if t == "image/png"), None) or next(t for t in types if t.startswith("image/"))
    # timeout 到了 subprocess.run 抛 TimeoutExpired，不会把半截 stdout 当成成功结果返回。
    # 异常对象里仍可能带着已经读到的字节，这里不用，直接放弃。
    # 复制刚发生时读取偶尔会超时，隔一秒再试一次。
    data = None
    for attempt in range(2):
        try:
            data = subprocess.run(
                ["wl-paste", "--no-newline", "--type", image],
                capture_output=True, timeout=5,
            ).stdout
            break
        except subprocess.TimeoutExpired:
            if attempt == 0:
                log("剪贴板读取超时，1 秒后重试")
                time.sleep(1)
    if data is None:
        log("剪贴板读取超时，放弃这次导入")
        print("剪贴板读取超时，放弃这次导入", file=sys.stderr)
        return 1
    if not data:
        return 0
    try:
        own = json.loads(OWN_CLIP.read_text(encoding="utf-8"))
        if own.get("hash") == sha256_bytes(data) and time.time() - own.get("time", 0) < OWN_CLIP_SECONDS:
            return 0
    except Exception:
        pass
    # 截图键会同时写文件夹和剪贴板；剪贴板这路先到时，按截图记，免得来源标成「剪贴板」
    label = "剪贴板"
    digest = sha256_bytes(data)
    shots = screenshot_dir()
    try:
        for f in shots.iterdir():
            if f.suffix.lower() == ".png" and time.time() - f.stat().st_mtime < 5 and sha256_bytes(f.read_bytes()) == digest:
                label = "截图"
                break
    except OSError:
        pass
    return _emit_imported(import_bytes(data, label))


def cmd_copy(path):
    data = Path(path).read_bytes()
    DATA.mkdir(parents=True, exist_ok=True)
    OWN_CLIP.write_text(json.dumps({"hash": sha256_bytes(data), "time": time.time()}), encoding="utf-8")
    # wl-copy 会留在后台提供剪贴板内容，不等它
    p = subprocess.Popen(["wl-copy", "--type", "image/png"], stdin=subprocess.PIPE,
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
    p.stdin.write(data)
    p.stdin.close()
    return 0


def cmd_missing(paths):
    for p in paths:
        if not os.path.exists(p):
            print(p)
    return 0


def cmd_load_state():
    try:
        paths = json.loads(STATE_FILE.read_text(encoding="utf-8")).get("Paths", [])
    except Exception:
        paths = []
    for p in paths:
        if os.path.exists(p):
            print(p)
    return 0


def cmd_save_state(paths):
    DATA.mkdir(parents=True, exist_ok=True)
    tmp = STATE_FILE.with_suffix(".tmp")
    tmp.write_text(json.dumps({"Paths": paths}, ensure_ascii=False, indent=2), encoding="utf-8")
    tmp.replace(STATE_FILE)
    return 0


def cmd_run(args):
    import ctypes
    import signal
    PR_SET_PDEATHSIG = 1
    ctypes.CDLL("libc.so.6", use_errno=True).prctl(PR_SET_PDEATHSIG, signal.SIGTERM)
    os.execvp(args[0], args)


def load_settings():
    try:
        return json.loads(SETTINGS_FILE.read_text(encoding="utf-8"))
    except Exception:
        return {}


def retention_days(settings):
    days = settings.get("RetentionDays")
    if days is None:
        return 30
    try:
        return int(days)
    except (TypeError, ValueError):
        return 30


def retention_label(settings):
    days = retention_days(settings)
    return {7: "7 天", 30: "30 天", 90: "90 天", 365: "1 年", 0: "永久"}.get(days, f"{days} 天")


def _key_name(vk):
    if 0x30 <= vk <= 0x39 or 0x41 <= vk <= 0x5A:
        return chr(vk)
    if 0x70 <= vk <= 0x87:
        return "F" + str(vk - 0x6F)
    return {
        0x2C: "PrtSc", 0x0D: "Enter", 0x20: "Space", 0x09: "Tab", 0x1B: "Esc",
        0x2E: "Delete", 0x08: "Backspace", 0x25: "Left", 0x26: "Up", 0x27: "Right", 0x28: "Down",
    }.get(vk, "VK" + format(vk, "X"))


def capture_label(settings):
    """空状态那行字。没设过或设了不能注册的键时，Linux 上显示系统截图键 Super+Shift+S。"""
    try:
        mods = int(settings.get("CaptureModifiers") or 0)
        key = int(settings.get("CaptureKey") or 0)
    except (TypeError, ValueError):
        mods, key = 0, 0
    modifiers = (0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5)
    if key == 0 or key in modifiers or (key != 0x2C and mods == 0):
        return "Super+Shift+S"
    parts = []
    if mods & 0x0002:
        parts.append("Ctrl")
    if mods & 0x0001:
        parts.append("Alt")
    if mods & 0x0004:
        parts.append("Shift")
    if mods & 0x0008:
        parts.append("Win")
    parts.append(_key_name(key))
    return "+".join(parts)


def _stamp_of(name):
    match = NAME_PATTERN.match(name)
    if not match:
        return None
    try:
        return datetime.strptime(match.group(2) + match.group(3), "%Y%m%d%H%M%S"), match.group(1)
    except ValueError:
        return None


def cmd_scan():
    root = library_root()
    settings = load_settings()
    items = []
    total = 0
    for path in owned_files(root):
        parsed = _stamp_of(path.name)
        if not parsed:
            continue
        when, label = parsed
        try:
            size = path.stat().st_size
        except OSError:
            continue
        total += size
        items.append({
            "path": str(path.resolve()),
            "source": label,
            "time": when.strftime("%Y-%m-%dT%H:%M:%S"),
            "favorite": path.parent.name == FAVORITES,
            "size": size,
        })
    items.sort(key=lambda it: it["time"], reverse=True)
    json.dump({
        "retention": retention_label(settings),
        "occupied": total,
        "capture": capture_label(settings),
        "items": items,
    }, sys.stdout, ensure_ascii=False)
    sys.stdout.write("\n")
    return 0


def _with_import_lock(fn):
    import fcntl
    DATA.mkdir(parents=True, exist_ok=True)
    with open(DATA / "import.lock", "w", encoding="utf-8") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        return fn()


def cmd_favorite(path, flag):
    if flag not in ("0", "1"):
        print("favorite 的第二个参数是 0 或 1", file=sys.stderr)
        return 1

    def go():
        if not is_owned(path):
            print("不是图库自己的文件，拒绝收藏", file=sys.stderr)
            return 1
        src = Path(path).resolve()
        if not is_owned(src) or not src.is_file():
            print("不是图库自己的文件，拒绝收藏", file=sys.stderr)
            return 1
        want = flag == "1"
        if want == (src.parent.name == FAVORITES):
            print(src)
            return 0
        parsed = _stamp_of(src.name)
        if not parsed:
            print("不是图库自己的文件，拒绝收藏", file=sys.stderr)
            return 1
        when, label = parsed
        root = library_root()
        dest_dir = (root / FAVORITES) if want else (root / when.strftime("%Y-%m"))
        dest_dir.mkdir(parents=True, exist_ok=True)
        dest = dest_dir / src.name
        if dest.exists():
            dest = unique_named(dest_dir, label, when)
        src.rename(dest)
        log(("收藏 " if want else "取消收藏 ") + str(dest))
        print(dest.resolve())
        return 0

    return _with_import_lock(go)


def cmd_trash(path):
    if not is_owned(path):
        print("不是图库自己的文件，拒绝删除", file=sys.stderr)
        return 1
    src = Path(path).resolve()
    if not is_owned(src) or not src.is_file():
        print("不是图库自己的文件，拒绝删除", file=sys.stderr)
        return 1
    result = subprocess.run(["gio", "trash", str(src)], capture_output=True, text=True)
    if result.returncode != 0:
        err = (result.stderr or result.stdout or "无法放入回收站").strip()
        log("无法放入回收站: " + err)
        print(err, file=sys.stderr)
        return 1
    log("图库进回收站 " + str(src))
    print(src)
    return 0


def cmd_sweep(keep):
    """同 Windows 版 Retention.SweepNow。绳上的路径由调用方传进来。"""
    days = retention_days(load_settings())
    if days <= 0:
        log("保存时间为永久，跳过清理")
        print(0)
        return 0
    cutoff = datetime.now() - timedelta(days=days)
    keep_set = set()
    for k in keep:
        try:
            keep_set.add(str(Path(k).resolve()))
        except OSError:
            keep_set.add(k)
    n = 0
    for path in owned_files(library_root()):
        if path.parent.name == FAVORITES:
            continue
        if str(path.resolve()) in keep_set:
            continue
        parsed = _stamp_of(path.name)
        if not parsed or parsed[0] >= cutoff:
            continue
        result = subprocess.run(["gio", "trash", str(path)], capture_output=True, text=True)
        if result.returncode != 0:
            log("无法放入回收站: " + (result.stderr or result.stdout or "").strip())
            continue
        n += 1
    log(f"清理过期 {n} 张")
    print(n)
    return 0


def cmd_reveal(path):
    if not is_owned(path):
        print("不是图库自己的文件", file=sys.stderr)
        return 1
    src = Path(path).resolve()
    if not src.is_file():
        print("文件不存在", file=sys.stderr)
        return 1
    uri = src.as_uri()
    result = subprocess.run(
        [
            "dbus-send", "--session", "--print-reply",
            "--dest=org.freedesktop.FileManager1",
            "/org/freedesktop/FileManager1",
            "org.freedesktop.FileManager1.ShowItems",
            "array:string:" + uri,
            "string:",
        ],
        capture_output=True, text=True,
    )
    if result.returncode != 0:
        log("文件管理器接口失败，改为打开所在文件夹: " + (result.stderr or "").strip())
        subprocess.Popen(
            ["xdg-open", str(src.parent)],
            start_new_session=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
    return 0


# 内置主题和 Windows 版共用一份：仓库根目录 shared/themes。只拷了 linux/ 目录的安装退回 linux/themes。
_HERE = Path(__file__).resolve().parent
BUILTIN_THEMES = next((d for d in (_HERE.parent / "shared" / "themes", _HERE / "themes") if d.is_dir()),
                      _HERE.parent / "shared" / "themes")
THEME_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$")
# 缺字段时补这套。和 shared/themes/polaroid-blue.json 一致；那个文件读不了才用到这里。
THEME_FALLBACK = {
    "name": "主题蓝绳 + 拍立得",
    "accent": "#3D6FE0",
    "rope": {
        "color": "accent-strong", "width": 3, "shadowColor": "rgba(20,30,60,0.20)",
        "shadowWidth": 4, "shadowOffset": 2, "texture": "none",
    },
    "ends": {
        "style": "pin", "inset": 40, "pinColor": "#FFFFFF",
        "pinRing": "rgba(30,40,70,0.20)", "dotColor": "accent-strong",
        "radius": 8, "dotRadius": 3.5,
    },
    "clip": {
        "color": "accent-strong", "width": 12, "height": 22, "radius": 3,
        "highlight": "rgba(255,255,255,0.55)", "highlightWidth": 2, "highlightHeight": 16,
        "highlightRadius": 1, "highlightShift": -2.5, "above": 10, "below": 12,
        "border": "", "shadow": True,
    },
    "frame": {
        "paper": "#FFFFFF", "padLeft": 7, "padTop": 7, "padRight": 7, "padBottom": 22,
        "radius": 4, "photoRadius": 2,
    },
    "hint": {"bg": "#FFFFFF", "fg": "#2E3440", "border": "rgba(30,40,70,0.20)"},
    "gallery": {"bg": "#F6F1E7"},
}
_COLOR_TOKEN = ("accent", "accent-strong")
_COLOR = re.compile(
    r"^(#[0-9A-Fa-f]{6}|#[0-9A-Fa-f]{8}|rgba?\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*(?:,\s*[0-9.]+\s*)?\))$"
)


def read_settings_obj():
    """给会写回的命令用。文件坏了就抛错，免得用空对象把原文件盖掉。"""
    if not SETTINGS_FILE.exists():
        return {}
    try:
        data = json.loads(SETTINGS_FILE.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        log(f"settings.json 解析失败：{e}")
        raise
    if not isinstance(data, dict):
        log("settings.json 不是对象，拒绝写入")
        raise ValueError("settings.json 不是对象")
    return data


def save_settings(data):
    DATA.mkdir(parents=True, exist_ok=True)
    tmp = SETTINGS_FILE.with_suffix(".tmp")
    tmp.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    tmp.replace(SETTINGS_FILE)


def _theme_template():
    path = BUILTIN_THEMES / "polaroid-blue.json"
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        if isinstance(data, dict):
            return data
    except Exception as e:
        log(f"内置默认主题读不了：{e}")
    return THEME_FALLBACK


def _color(value, default, allow_empty=False):
    if isinstance(value, str) and value.strip() in _COLOR_TOKEN:
        return value.strip()
    if isinstance(value, str) and value.strip() == "" and allow_empty:
        return ""
    if isinstance(value, str) and _COLOR.match(value.strip()):
        return value.strip()
    return default


def _num(value, default):
    try:
        n = float(value)
    except (TypeError, ValueError):
        return float(default)
    if n != n:
        return float(default)
    return n


def normalize_theme(data, template=None):
    base = template if template is not None else _theme_template()
    src = data if isinstance(data, dict) else {}
    rope = src.get("rope") if isinstance(src.get("rope"), dict) else {}
    ends = src.get("ends") if isinstance(src.get("ends"), dict) else {}
    clip = src.get("clip") if isinstance(src.get("clip"), dict) else {}
    frame = src.get("frame") if isinstance(src.get("frame"), dict) else {}
    hint = src.get("hint") if isinstance(src.get("hint"), dict) else {}
    gallery = src.get("gallery") if isinstance(src.get("gallery"), dict) else {}
    br, be, bc, bf, bh = (base[k] for k in ("rope", "ends", "clip", "frame", "hint"))
    bg = base.get("gallery") if isinstance(base.get("gallery"), dict) else {}
    name = src.get("name")
    if not isinstance(name, str) or not name.strip():
        name = base.get("name") or "主题蓝绳 + 拍立得"
    accent = _color(src.get("accent"), base.get("accent") or "#3D6FE0")
    style = ends.get("style") if ends.get("style") in ("pin", "knot", "none") else be["style"]
    texture = rope.get("texture") if rope.get("texture") in ("none", "twist") else br["texture"]
    shadow = clip.get("shadow") if isinstance(clip.get("shadow"), bool) else bool(bc["shadow"])
    return {
        "name": name.strip(),
        "accent": accent,
        "rope": {
            "color": _color(rope.get("color"), br["color"]),
            "width": _num(rope.get("width"), br["width"]),
            "shadowColor": _color(rope.get("shadowColor"), br["shadowColor"]),
            "shadowWidth": _num(rope.get("shadowWidth"), br["shadowWidth"]),
            "shadowOffset": _num(rope.get("shadowOffset"), br["shadowOffset"]),
            "texture": texture,
        },
        "ends": {
            "style": style,
            "inset": _num(ends.get("inset"), be["inset"]),
            "pinColor": _color(ends.get("pinColor"), be["pinColor"]),
            "pinRing": _color(ends.get("pinRing"), be["pinRing"]),
            "dotColor": _color(ends.get("dotColor"), be["dotColor"]),
            "radius": _num(ends.get("radius"), be["radius"]),
            "dotRadius": _num(ends.get("dotRadius"), be["dotRadius"]),
        },
        "clip": {
            "color": _color(clip.get("color"), bc["color"]),
            "width": _num(clip.get("width"), bc["width"]),
            "height": _num(clip.get("height"), bc["height"]),
            "radius": _num(clip.get("radius"), bc["radius"]),
            "highlight": _color(clip.get("highlight"), bc["highlight"], allow_empty=True),
            "highlightWidth": _num(clip.get("highlightWidth"), bc["highlightWidth"]),
            "highlightHeight": _num(clip.get("highlightHeight"), bc["highlightHeight"]),
            "highlightRadius": _num(clip.get("highlightRadius"), bc["highlightRadius"]),
            "highlightShift": _num(clip.get("highlightShift"), bc["highlightShift"]),
            "above": _num(clip.get("above"), bc["above"]),
            "below": _num(clip.get("below"), bc["below"]),
            "border": _color(clip.get("border"), bc["border"], allow_empty=True),
            "shadow": shadow,
        },
        "frame": {
            "paper": _color(frame.get("paper"), bf["paper"]),
            "padLeft": _num(frame.get("padLeft"), bf["padLeft"]),
            "padTop": _num(frame.get("padTop"), bf["padTop"]),
            "padRight": _num(frame.get("padRight"), bf["padRight"]),
            "padBottom": _num(frame.get("padBottom"), bf["padBottom"]),
            "radius": _num(frame.get("radius"), bf["radius"]),
            "photoRadius": _num(frame.get("photoRadius"), bf["photoRadius"]),
        },
        "hint": {
            "bg": _color(hint.get("bg"), bh["bg"]),
            "fg": _color(hint.get("fg"), bh["fg"]),
            "border": _color(hint.get("border"), bh["border"], allow_empty=True),
        },
        "gallery": {
            "bg": _color(gallery.get("bg"), bg.get("bg") or "#F6F1E7"),
        },
    }


def theme_file(tid):
    if not isinstance(tid, str) or not THEME_ID.match(tid):
        return None
    user = DATA / "themes" / f"{tid}.json"
    if user.is_file():
        return user
    builtin = BUILTIN_THEMES / f"{tid}.json"
    if builtin.is_file():
        return builtin
    return None


def load_theme(tid):
    path = theme_file(tid)
    if path is None:
        return None
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as e:
        log(f"主题 {tid} 读不了（{path}）：{e}，改用默认")
        return None
    if not isinstance(data, dict):
        log(f"主题 {tid} 不是对象，改用默认")
        return None
    spec = normalize_theme(data)
    spec["id"] = tid
    return spec


def default_theme():
    spec = load_theme("polaroid-blue")
    if spec is not None:
        return spec
    spec = normalize_theme(THEME_FALLBACK)
    spec["id"] = "polaroid-blue"
    return spec


def current_theme_id():
    try:
        data = read_settings_obj()
    except Exception:
        return "polaroid-blue"
    tid = data.get("Theme") or "polaroid-blue"
    if not isinstance(tid, str) or not THEME_ID.match(tid):
        return "polaroid-blue"
    return tid


def list_themes():
    ids = []
    seen = set()
    for tid in ("polaroid-blue", "red-rope", "graphite", "linen"):
        if theme_file(tid):
            ids.append(tid)
            seen.add(tid)
    if BUILTIN_THEMES.is_dir():
        for path in sorted(BUILTIN_THEMES.glob("*.json")):
            if path.stem not in seen and THEME_ID.match(path.stem):
                ids.append(path.stem)
                seen.add(path.stem)
    user_dir = DATA / "themes"
    if user_dir.is_dir():
        for path in sorted(user_dir.glob("*.json")):
            if path.stem not in seen and THEME_ID.match(path.stem):
                ids.append(path.stem)
                seen.add(path.stem)
    out = []
    for tid in ids:
        spec = load_theme(tid)
        out.append((tid, spec["name"] if spec else tid))
    return out


def emit_theme(spec):
    json.dump(spec, sys.stdout, ensure_ascii=False)
    sys.stdout.write("\n")


def cmd_theme_current():
    tid = current_theme_id()
    spec = load_theme(tid)
    if spec is None:
        log(f"当前主题 {tid} 不可用，退回默认")
        spec = default_theme()
    emit_theme(spec)
    return 0


def cmd_theme_list():
    for tid, name in list_themes():
        print(f"{tid} {name}")
    return 0


def cmd_theme_set(tid):
    spec = load_theme(tid)
    if spec is None:
        print(f"没有主题 {tid}", file=sys.stderr)
        return 1
    settings = read_settings_obj()
    settings["Theme"] = tid
    save_settings(settings)
    emit_theme(spec)
    log(f"切换主题 {tid}")
    return 0


def cmd_theme_next():
    themes = list_themes()
    if not themes:
        print("没有主题", file=sys.stderr)
        return 1
    ids = [tid for tid, _ in themes]
    cur = current_theme_id()
    nxt = ids[(ids.index(cur) + 1) % len(ids)] if cur in ids else ids[0]
    return cmd_theme_set(nxt)


# ---- 设置窗口（同 Windows SettingsWindow.cs）----

RETENTION_OPTIONS = ((7, "7 天"), (30, "30 天"), (90, "90 天"), (365, "1 年"), (0, "永久"))
COLLECT_OPTIONS = (
    ("all", "截图和复制的图片"),
    ("screenshots", "只要截图"),
    ("own", "只要系图自己截的图"),
)


def cmd_settings_get():
    """设置窗口打开时读一次：当前值、可选项、图库占用和张数。"""
    settings = load_settings()
    root = library_root()
    count, total = 0, 0
    for f in owned_files(root):
        try:
            total += f.stat().st_size
            count += 1
        except OSError:
            pass
    days = retention_days(settings)
    json.dump({
        "libraryPath": str(root),
        "defaultLibraryPath": str(pictures_dir() / "系图"),
        "retentionDays": days if days in [d for d, _ in RETENTION_OPTIONS] else 30,
        "retentionOptions": [{"days": d, "label": label} for d, label in RETENTION_OPTIONS],
        "collect": collect_mode(settings),
        "collectOptions": [{"value": v, "label": label} for v, label in COLLECT_OPTIONS],
        "theme": current_theme_id(),
        "themes": [{"id": tid, "name": name} for tid, name in list_themes()],
        "count": count,
        "occupied": total,
    }, sys.stdout, ensure_ascii=False)
    sys.stdout.write("\n")
    return 0


def cmd_settings_set(key, value):
    """只改设置窗口管的两项，其他字段原样保留。值不合法就拒绝，不写文件。"""
    if key == "RetentionDays":
        try:
            parsed = int(value)
        except ValueError:
            parsed = None
        if parsed not in [d for d, _ in RETENTION_OPTIONS]:
            print(f"RetentionDays 只能是 {', '.join(str(d) for d, _ in RETENTION_OPTIONS)}", file=sys.stderr)
            return 1
    elif key == "Collect":
        if value not in COLLECT_MODES:
            print(f"Collect 只能是 {', '.join(COLLECT_MODES)}", file=sys.stderr)
            return 1
        parsed = value
    else:
        print(f"settings-set 不管 {key}", file=sys.stderr)
        return 1
    settings = read_settings_obj()
    settings[key] = parsed
    save_settings(settings)
    log(f"设置 {key} = {parsed}")
    print(parsed)
    return 0


def _same_dir(a, b):
    try:
        return a.resolve() == b.resolve()
    except OSError:
        return False


def cmd_library_move(dest, move):
    """换图库位置（同 Library.ChangeRoot）。move 为 1 时把系图自己的文件按原来的子文件夹搬过去，
    重名另起 _n。别的文件不碰。输出一行 JSON：root 和 moved（[旧路径, 新路径] 列表），绳上路径按它改。"""
    if move not in ("0", "1"):
        print("library-move 的第二个参数是 0 或 1", file=sys.stderr)
        return 1
    target = Path(dest).expanduser()
    if not target.is_absolute():
        print("图库位置要写绝对路径", file=sys.stderr)
        return 1
    import shutil

    def go():
        old_root = library_root()
        if old_root.is_dir() and old_root.resolve() in target.resolve().parents:
            print("新位置不能放在原图库里面", file=sys.stderr)
            return 1
        target.mkdir(parents=True, exist_ok=True)
        (target / FAVORITES).mkdir(exist_ok=True)
        new_root = target.resolve()
        moved = []
        if move == "1" and old_root.is_dir() and not _same_dir(old_root, new_root):
            for f in list(owned_files(old_root)):
                parsed = _stamp_of(f.name)
                if not parsed:
                    continue
                when, label = parsed
                dest_dir = new_root / f.parent.name
                dest_dir.mkdir(parents=True, exist_ok=True)
                out = dest_dir / f.name
                if out.exists():
                    out = unique_named(dest_dir, label, when)
                src = str(f.resolve())
                shutil.move(str(f), str(out))
                moved.append([src, str(out.resolve())])
        settings = read_settings_obj()
        settings["LibraryPath"] = str(new_root)
        save_settings(settings)
        try:
            HASH_CACHE.unlink()
        except FileNotFoundError:
            pass
        log(f"图库位置 {new_root}，搬了 {len(moved)} 张")
        json.dump({"root": str(new_root), "moved": moved}, sys.stdout, ensure_ascii=False)
        sys.stdout.write("\n")
        return 0

    return _with_import_lock(go)


def cmd_pick_folder(start):
    """有 zenity 或 kdialog 就弹系统的选文件夹对话框，输出选中的路径；取消或都没有时什么也不输出。"""
    import shutil
    if shutil.which("zenity"):
        cmd = ["zenity", "--file-selection", "--directory", "--title=选择图库位置", f"--filename={start}/"]
    elif shutil.which("kdialog"):
        cmd = ["kdialog", "--getexistingdirectory", start, "--title", "选择图库位置"]
    else:
        print("没有 zenity 或 kdialog，直接在框里输入路径", file=sys.stderr)
        return 3
    out = subprocess.run(cmd, capture_output=True, text=True).stdout.strip()
    if out:
        print(out)
    return 0


def main(argv):
    if not argv:
        print(__doc__, file=sys.stderr)
        return 2
    cmd, args = argv[0], argv[1:]
    try:
        if cmd == "import":
            return cmd_import(args[0], args[1] if len(args) > 1 else "文件")
        if cmd == "auto-import":
            return cmd_auto_import(args[0])
        if cmd == "import-b64":
            return cmd_import_b64(args[0] if args else "拖入")
        if cmd == "fetch":
            return cmd_fetch(args[0])
        if cmd == "clip":
            return cmd_clip()
        if cmd == "copy":
            return cmd_copy(args[0])
        if cmd == "missing":
            return cmd_missing(args)
        if cmd == "load-state":
            return cmd_load_state()
        if cmd == "save-state":
            return cmd_save_state(args)
        if cmd == "run":
            return cmd_run(args)
        if cmd == "screenshot-dir":
            print(screenshot_dir())
            return 0
        if cmd == "library-root":
            print(library_root())
            return 0
        if cmd == "scan":
            return cmd_scan()
        if cmd == "favorite":
            return cmd_favorite(args[0], args[1] if len(args) > 1 else "")
        if cmd == "trash":
            return cmd_trash(args[0])
        if cmd == "sweep":
            return cmd_sweep(args)
        if cmd == "reveal":
            return cmd_reveal(args[0])
        if cmd == "theme-current":
            return cmd_theme_current()
        if cmd == "theme-list":
            return cmd_theme_list()
        if cmd == "theme-set":
            return cmd_theme_set(args[0] if args else "")
        if cmd == "theme-next":
            return cmd_theme_next()
        if cmd == "settings-get":
            return cmd_settings_get()
        if cmd == "settings-set":
            return cmd_settings_set(args[0], args[1])
        if cmd == "library-move":
            return cmd_library_move(args[0], args[1] if len(args) > 1 else "")
        if cmd == "pick-folder":
            return cmd_pick_folder(args[0] if args else str(HOME))
    except Exception as e:  # 记日志，别让 QML 那边卡住
        log(f"{cmd} 失败：{e!r}")
        print(f"{cmd} 失败：{e}", file=sys.stderr)
        return 1
    print(f"不认识的命令 {cmd}", file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
