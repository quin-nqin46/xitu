#!/usr/bin/env python3
"""Linux 界面自动测试：在无头 Sway 里真的跑起系图（qs -p linux），
用 ipc 和虚拟鼠标/键盘把主要功能走一遍，每步用 grim 截图，最后检查日志里有没有 QML 报错。

用法：python3 linux/tests/ui/drive.py --out ui-shots
需要：sway grim slurp wtype wl-clipboard quickshell inotify-tools python-pillow
（CI 在 Arch 容器里装，见 .github/workflows/build.yml 的 linux-ui 任务）

不碰真实的家目录：HOME、XDG_*、截图文件夹都指向临时目录。
外部程序只替身三样：编辑器（XITU_EDITOR 指向会在图上画一笔的脚本）、
zenity（另存为对话框，直接回答目标路径）、notify-send / xdg-open（只记下调用）。
slurp、grim、wl-copy、inotifywait 都是真的。
"""

import argparse
import json
import os
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import traceback
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
from wlinput import VirtualPointer  # noqa: E402

REPO = Path(__file__).resolve().parents[3]
LINUX = REPO / "linux"
W, H = 1920, 1080
BG = (0x55, 0x66, 0x77)

# ---- layout.js 里的几何（照着算卡片在哪）----
SPACING = 174
CARD_W = 150
TRIGGER_MIN_HALF = 160
TRIGGER_MARGIN = 14


def slot_x(i, n, width=W):
    return width / 2 - max(n - 1, 0) * SPACING / 2 + i * SPACING


def trigger_span(n, width=W):
    half = max(TRIGGER_MIN_HALF, max(n - 1, 0) * SPACING / 2 + CARD_W / 2 + TRIGGER_MARGIN)
    return max(0, width / 2 - half), min(width, width / 2 + half)


CARD_Y = 105  # 照片中间大概的高度（绳子 y≈10..40，夹子下面就是相框）

# 允许的日志行：和应用无关、在无头 Sway 里必然出现的提示
ALLOWED = [
    r"HYPRLAND_INSTANCE_SIGNATURE",          # 不在 Hyprland 里，Quickshell.Hyprland 连不上
    r"[Hh]yprland",
    r"QFont::|Fontconfig|font",              # CI 镜像字体少
    r"qt\.qpa\.wayland.*(cursor|Could not find)",
    r"xdg-desktop-portal|org\.freedesktop\.portal",
    r"dbus|DBus|D-Bus",
    r"QStandardPaths",
    r"No such file or directory.*\.config/quickshell",
    r"qt\.qpa\.wayland\.textinput",         # Qt 自己的输入法焦点提示
]


class Run:
    def __init__(self, out):
        self.out = Path(out).resolve()
        if self.out.exists():
            shutil.rmtree(self.out)
        self.out.mkdir(parents=True)
        self.tmp = Path(tempfile.mkdtemp(prefix="xitu-ui-"))
        self.results = []   # (步骤, 通过?, 说明, [截图])
        self.shots = []
        self.procs = []
        self.pointer = None
        self.cur_shots = []
        self.env = self.make_env()
        self.qs_log = self.out / "quickshell.log"

    # ---------- 环境 ----------
    def make_env(self):
        t = self.tmp
        home = t / "home"
        rt = t / "runtime"
        for d in (home, rt, home / "Pictures/Screenshots", t / "stubs", t / "calls", t / "saved"):
            d.mkdir(parents=True, exist_ok=True)
        rt.chmod(0o700)
        env = {k: v for k, v in os.environ.items()
               if not k.startswith(("WAYLAND_", "HYPRLAND_", "SWAYSOCK", "XDG_", "DISPLAY"))}
        env.update({
            "HOME": str(home),
            "XDG_RUNTIME_DIR": str(rt),
            "XDG_CONFIG_HOME": str(home / ".config"),
            "XDG_DATA_HOME": str(home / ".local/share"),
            "XDG_STATE_HOME": str(home / ".local/state"),
            "XDG_CACHE_HOME": str(home / ".cache"),
            "XDG_PICTURES_DIR": str(home / "Pictures"),
            "XITU_SCREENSHOT_DIR": str(home / "Pictures/Screenshots"),
            "XITU_EDITOR": f"python3 {t / 'stubs/edit.py'} {{}}",
            "XITU_TEST_CALLS": str(t / "calls"),
            "XITU_TEST_SAVE_DEST": str(t / "saved/另存的.png"),
            "PATH": f"{t / 'stubs'}:{os.environ.get('PATH', '/usr/bin')}",
            "QT_QPA_PLATFORM": "wayland",
            "QT_QUICK_BACKEND": "software",
            "LANG": "C.UTF-8",
            "WLR_BACKENDS": "headless",
            "WLR_LIBINPUT_NO_DEVICES": "1",
            "WLR_RENDERER": "pixman",
            "XDG_CURRENT_DESKTOP": "sway",
        })
        self.write_stubs(t / "stubs")
        return env

    def write_stubs(self, d):
        calls = "$XITU_TEST_CALLS"
        rec = lambda name: f'#!/bin/sh\nprintf "%s\\n" "$*" >> "{calls}/{name}"\n'
        (d / "notify-send").write_text(rec("notify-send"))
        (d / "xdg-open").write_text(rec("xdg-open"))
        (d / "gio").write_text(rec("gio"))
        (d / "zenity").write_text(rec("zenity") + 'printf "%s\\n" "$XITU_TEST_SAVE_DEST"\n')
        (d / "edit.py").write_text(
            "import os, sys, time\n"
            "from PIL import Image, ImageDraw\n"
            "p = sys.argv[1]\n"
            "open(os.path.join(os.environ['XITU_TEST_CALLS'], 'editor'), 'a').write(p + '\\n')\n"
            "time.sleep(0.5)\n"
            "im = Image.open(p).convert('RGB')\n"
            "d = ImageDraw.Draw(im)\n"
            "w, h = im.size\n"
            "d.line((0, 0, w, h), fill=(230, 30, 30), width=max(8, w // 25))\n"
            "d.line((0, h, w, 0), fill=(230, 30, 30), width=max(8, w // 25))\n"
            "im.save(p, 'PNG')\n"
        )
        for f in d.iterdir():
            f.chmod(0o755)

    def calls(self, name):
        f = self.tmp / "calls" / name
        return f.read_text().splitlines() if f.exists() else []

    def sample(self, name, size, color, label):
        p = self.tmp / name
        im = Image.new("RGB", size, color)
        d = ImageDraw.Draw(im)
        w, h = size
        for i in range(0, w, 60):
            d.line((i, 0, i, h), fill=tuple(min(255, c + 25) for c in color), width=3)
        d.rectangle((w // 8, h // 8, w * 7 // 8, h * 7 // 8), outline=(255, 255, 255), width=10)
        d.text((w // 6, h // 6), label, fill=(255, 255, 255))
        im.save(p)
        return p

    # ---------- 进程 ----------
    def start_sway(self):
        conf = self.tmp / "sway.conf"
        conf.write_text(
            f"output HEADLESS-1 resolution {W}x{H} position 0 0 bg #{'%02x%02x%02x' % BG} solid_color\n"
            "default_border none\nfocus_follows_mouse no\nseat * hide_cursor 0\n"
            "for_window [title=\".*\"] floating enable\n"
        )
        log = open(self.out / "sway.log", "w")
        p = subprocess.Popen(["sway", "-c", str(conf), "-d"] if os.environ.get("XITU_UI_SWAY_DEBUG") else ["sway", "-c", str(conf)],
                             env=self.env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        self.procs.append(p)
        rt = Path(self.env["XDG_RUNTIME_DIR"])
        for _ in range(100):
            socks = [s for s in rt.glob("wayland-*") if not s.name.endswith(".lock")]
            ipc = list(rt.glob("sway-ipc.*.sock"))
            if socks and ipc:
                self.env["WAYLAND_DISPLAY"] = socks[0].name
                self.env["SWAYSOCK"] = str(ipc[0])
                time.sleep(0.5)
                return
            if p.poll() is not None:
                break
            time.sleep(0.1)
        raise RuntimeError("Sway 没起来，见 sway.log")

    def start_qs(self):
        self.qs_out = open(self.qs_log, "w")
        p = subprocess.Popen(["qs", "-p", str(LINUX)], env=self.env, stdout=self.qs_out,
                             stderr=subprocess.STDOUT, start_new_session=True)
        self.procs.append(p)
        self.qs = p
        self.wait_log(r"\[xitu\] 恢复 \d+ 张", 30, "启动后恢复绳子")

    def ipc(self, *args, timeout=10):
        r = subprocess.run(["qs", "ipc", "-p", str(LINUX), "call", "xitu", *args], env=self.env,
                           capture_output=True, text=True, timeout=timeout)
        if r.returncode != 0:
            raise RuntimeError(f"ipc {' '.join(args)} 失败：{r.stderr.strip() or r.stdout.strip()}")
        return r.stdout.strip()

    def sh(self, *cmd, check=True):
        r = subprocess.run(cmd, env=self.env, capture_output=True, text=True, timeout=20)
        if check and r.returncode != 0:
            raise RuntimeError(f"{cmd[0]} 失败：{r.stderr.strip()}")
        return r.stdout

    def key(self, *keys):
        args = []
        for k in keys:
            args += ["-k", k]
        self.sh("wtype", *args)

    def log_text(self):
        self.qs_out.flush()
        return self.qs_log.read_text(errors="replace")

    def count_log(self, pattern):
        return len(re.findall(pattern, self.log_text()))

    def wait_log(self, pattern, timeout=10, what=None, count=1):
        end = time.time() + timeout
        while time.time() < end:
            if self.count_log(pattern) >= count:
                return
            if self.qs is not None and self.qs.poll() is not None:
                raise RuntimeError("quickshell 退出了")
            time.sleep(0.1)
        raise AssertionError(f"等不到日志「{what or pattern}」（{timeout} 秒）")

    def wait_until(self, fn, timeout, what):
        end = time.time() + timeout
        while time.time() < end:
            if fn():
                return
            time.sleep(0.1)
        raise AssertionError(f"等不到：{what}（{timeout} 秒）")

    # ---------- 截图与像素 ----------
    def shot(self, name):
        n = len(self.shots) + 1
        f = self.out / f"{n:02d}-{name}.png"
        self.sh("grim", str(f))
        self.shots.append(f.name)
        self.cur_shots.append(f.name)
        return f

    def grab(self, box=None):
        f = self.tmp / "probe.png"
        self.sh("grim", str(f))
        im = Image.open(f).convert("RGB")
        return im.crop(box) if box else im

    def painted(self, box):
        """box 里不是桌面底色的像素占比。"""
        im = self.grab(box)
        bg = Image.new("RGB", im.size, BG)
        diff = ImageChops.difference(im, bg).convert("L").point(lambda v: 255 if v > 24 else 0)
        hist = diff.histogram()
        return hist[255] / (im.size[0] * im.size[1])

    def rope_shown(self):
        # 绳子在屏幕最上面 0..220 这一条；收起时这里全是底色
        return self.painted((200, 0, W - 200, 220)) > 0.01

    def state_paths(self):
        f = Path(self.env["XDG_DATA_HOME"]) / "Xitu/state.json"
        if not f.exists():
            return []
        return json.loads(f.read_text(encoding="utf-8")).get("Paths", [])

    # ---------- 步骤 ----------
    def step(self, title):
        def deco(fn):
            def run():
                self.cur_shots = []
                t = time.time()
                try:
                    note = fn() or ""
                    self.results.append((title, True, note, list(self.cur_shots)))
                    print(f"✔ {title} {note} ({time.time() - t:.1f}s)", flush=True)
                except Exception as e:  # noqa: BLE001
                    try:
                        self.shot("失败-" + re.sub(r"\W+", "-", title)[:30])
                    except Exception:  # noqa: BLE001
                        pass
                    self.results.append((title, False, f"{type(e).__name__}: {e}", list(self.cur_shots)))
                    print(f"✘ {title}: {e}", flush=True)
                    traceback.print_exc()
                # 每步之间回到「指针在屏幕中间、绳子收起」
                try:
                    self.ensure_idle()
                except Exception:  # noqa: BLE001
                    pass
            return run
        return deco

    def ensure_idle(self):
        if self.pointer:
            self.pointer.move(W // 2, H - 100)
        time.sleep(0.2)
        if self.qs.poll() is None:
            end = time.time() + 4
            while time.time() < end and self.rope_shown():
                time.sleep(0.2)

    def hover_reveal(self, n):
        """模拟用户：指针放到顶边感应段中间，停住等展开，再往下移到照片上。"""
        p = self.pointer
        p.move(W // 2, 300)
        time.sleep(0.1)
        p.glide(W // 2, 0, steps=6)
        p.move(W // 2, 1)
        self.wait_until(self.rope_shown, 3, "顶边悬停后绳子展开")
        time.sleep(0.5)
        p.glide(int(slot_x(0, n)), CARD_Y, steps=10)
        time.sleep(0.4)

    # ---------- 收尾 ----------
    def cleanup(self):
        try:
            if self.qs is not None and self.qs.poll() is None:
                try:
                    self.ipc("quit", timeout=5)
                    self.qs.wait(5)
                except Exception:  # noqa: BLE001
                    pass
        except AttributeError:
            pass
        if self.pointer:
            try:
                self.pointer.close()
            except Exception:  # noqa: BLE001
                pass
        for p in reversed(self.procs):
            if p.poll() is None:
                try:
                    os.killpg(p.pid, signal.SIGTERM)
                    p.wait(5)
                except Exception:  # noqa: BLE001
                    try:
                        os.killpg(p.pid, signal.SIGKILL)
                    except Exception:  # noqa: BLE001
                        pass
        helper_log = Path(self.env["XDG_DATA_HOME"]) / "Xitu"
        for f in helper_log.glob("*.txt"):
            shutil.copy(f, self.out / ("helper-" + f.name))
        for f in (self.tmp / "calls").iterdir():
            shutil.copy(f, self.out / ("calls-" + f.name + ".txt"))

    def bad_log_lines(self):
        bad = []
        for line in self.log_text().splitlines():
            low = line.lower()
            if not (" warn" in low or "error" in low or "typeerror" in low or "referenceerror" in low
                    or re.search(r"\.qml[:\[]\d", line)):
                continue
            if "[xitu]" in line and "error" not in low:
                continue
            if any(re.search(a, line) for a in ALLOWED):
                continue
            bad.append(line)
        return bad


def scenario(r: Run):
    imgs = [
        r.sample("横图.png", (1600, 900), (200, 90, 70), "landscape 1600x900"),
        r.sample("竖图.png", (900, 1300), (70, 120, 190), "portrait 900x1300"),
        r.sample("方图.png", (1000, 1000), (90, 160, 100), "square 1000x1000"),
    ]

    @r.step("启动：空绳 + 提示")
    def _():
        r.ipc("peek")
        r.wait_until(r.rope_shown, 3, "peek 后绳子展开")
        time.sleep(0.6)
        r.shot("空绳提示")
        return f"主题列表：{r.ipc('themes').replace(chr(10), ' | ')}"
    _()

    @r.step("顶边感应：只在中间一段")
    def _():
        p = r.pointer
        left, right = trigger_span(0)
        # 两边（工作区按钮、托盘的位置）碰顶边不该展开
        for x in (60, int(left) - 40, int(right) + 40, W - 60):
            p.move(x, 300)
            p.glide(x, 0, steps=4)
            time.sleep(0.8)
            if r.rope_shown():
                raise AssertionError(f"x={x} 在感应段 {left:.0f}..{right:.0f} 外，不该展开")
            p.move(x, 300)
        # 中间停 0.3 秒展开
        p.glide(W // 2, 0, steps=4)
        r.wait_until(r.rope_shown, 3, "中间顶边悬停后展开")
        time.sleep(0.5)
        r.shot("顶边悬停展开")
        # 离开后收起
        p.glide(W // 2, 600, steps=6)
        r.wait_until(lambda: not r.rope_shown(), 4, "指针离开后收起")
        return f"感应段 x={left:.0f}..{right:.0f}"
    _()

    @r.step("ipc hang 挂 3 张")
    def _():
        for i, f in enumerate(imgs, 1):
            r.ipc("hang", str(f))
            r.wait_log(r"\[xitu\] 挂上 ", 10, "挂上", count=i)
            time.sleep(0.3)
        time.sleep(0.8)
        r.shot("挂了三张")
        r.wait_until(lambda: len(r.state_paths()) == 3, 5, "state.json 里 3 张")
        return "state.json 有 3 条"
    _()

    @r.step("截图文件夹：新文件自动挂上")
    def _():
        before = r.count_log(r"\[xitu\] 挂上 ")
        src = r.sample("新截图.png", (1920, 1080), (150, 110, 170), "screenshot folder")
        dst = Path(r.env["XITU_SCREENSHOT_DIR"]) / "Screenshot_20261106_120000.png"
        shutil.copy(src, dst)
        r.wait_log(r"\[xitu\] 挂上 ", 10, "截图文件夹的新图挂上", count=before + 1)
        time.sleep(1.0)
        r.shot("截图文件夹新图")
        n = len(r.state_paths())
        if n != 4:
            raise AssertionError(f"state.json 应有 4 张，实际 {n}")
    _()

    @r.step("悬停照片（红叉、抬起）")
    def _():
        r.hover_reveal(4)
        r.shot("悬停第一张")
    _()

    @r.step("单击复制")
    def _():
        r.hover_reveal(4)
        r.pointer.click()
        time.sleep(0.8)
        r.shot("已复制提示")
        r.wait_until(lambda: "image/png" in r.sh("wl-paste", "--list-types", check=False), 4, "剪贴板里有 image/png")
        return "剪贴板 image/png"
    _()

    @r.step("长按编辑 → 保存后卡片刷新")
    def _():
        r.hover_reveal(4)
        p = r.pointer
        p.button("left", True)
        time.sleep(0.9)
        p.button("left", False)
        r.wait_until(lambda: r.calls("editor"), 5, "编辑器被调用")
        r.wait_log(r"\[xitu\] 编辑后刷新 ", 8, "编辑后刷新")
        time.sleep(0.8)
        p.move(int(slot_x(0, 4)), CARD_Y + 1)
        time.sleep(0.3)
        r.shot("编辑后的缩略图")
        return f"编辑器收到 {r.calls('editor')[-1]}"
    _()

    @r.step("横拖换位置")
    def _():
        before = r.state_paths()
        r.hover_reveal(4)
        p = r.pointer
        x0, x2 = int(slot_x(0, 4)), int(slot_x(2, 4))
        p.button("left", True)
        time.sleep(0.1)
        p.glide(x0 + 40, CARD_Y, steps=5)
        p.glide(x2, CARD_Y, steps=20, pause=0.03)
        time.sleep(0.3)
        r.shot("拖动中")
        p.button("left", False)
        r.wait_log(r"\[xitu\] 换位置 → 2", 5, "换位置 → 2")
        time.sleep(0.6)
        after = r.state_paths()
        if after == before or sorted(after) != sorted(before):
            raise AssertionError(f"顺序没变或条目变了：{before} → {after}")
        if after[2] != before[0]:
            raise AssertionError(f"第一张应在第三位：{after}")
    _()

    @r.step("右键菜单 → 另存为…")
    def _():
        r.hover_reveal(4)
        r.pointer.click(button="right")
        time.sleep(0.8)
        r.shot("卡片右键菜单")
        # 打开图库 / ─ / 复制 / 另存为…：按三下 ↓ 回车
        r.key("Down", "Down", "Down", "Return")
        dest = Path(r.env["XITU_TEST_SAVE_DEST"])
        r.wait_until(dest.exists, 6, f"另存为写出 {dest.name}")
        return f"zenity 参数：{r.calls('zenity')[-1][:80]}"
    _()

    @r.step("右键菜单 → 编辑")
    def _():
        n = len(r.calls("editor"))
        r.hover_reveal(4)
        r.pointer.click(button="right")
        time.sleep(0.8)
        # 打开图库 / 复制 / 另存为… / 打开 / 编辑
        r.key("Down", "Down", "Down", "Down", "Down", "Return")
        r.wait_until(lambda: len(r.calls("editor")) > n, 5, "菜单「编辑」调起编辑器")
        r.wait_log(r"\[xitu\] 编辑后刷新 ", 8, "第二次编辑后刷新", count=2)
    _()

    @r.step("自带截图：ipc capture + slurp 框选 + grim")
    def _():
        before = r.count_log(r"\[xitu\] 挂上 ")
        r.ipc("capture")
        time.sleep(1.2)
        r.shot("slurp 框选层")
        r.pointer.drag(500, 380, 1300, 860)
        time.sleep(0.3)
        r.shot("截图飞向绳子")
        r.wait_log(r"\[xitu\] 挂上 ", 10, "截图挂上", count=before + 1)
        time.sleep(1.0)
        r.shot("截图挂上")
        new = r.state_paths()[-1]
        size = Image.open(new).size
        if abs(size[0] - 800) > 2 or abs(size[1] - 480) > 2:
            raise AssertionError(f"截图大小 {size}，应约 800x480")
        return f"截到 {size[0]}x{size[1]}"
    _()

    @r.step("图库窗口")
    def _():
        r.ipc("gallery")
        time.sleep(1.5)
        r.shot("图库")
        r.ipc("closeGallery")
        time.sleep(0.8)
    _()

    @r.step("设置窗口")
    def _():
        r.ipc("settings")
        time.sleep(1.5)
        tree = json.loads(r.sh("swaymsg", "-t", "get_tree", "-r"))
        names = []

        def walk(n):
            if n.get("pid") and n.get("type") in ("con", "floating_con"):
                names.append(n.get("name") or n.get("app_id") or "")
            for c in n.get("nodes", []) + n.get("floating_nodes", []):
                walk(c)
        walk(tree)
        r.shot("设置")
        if not names:
            raise AssertionError("设置窗口没出现在 Sway 的窗口树里")
        r.sh("swaymsg", "kill", check=False)
        time.sleep(0.5)
        return f"窗口：{names}"
    _()

    themes = [line.split(" ")[0] for line in r.ipc("themes").splitlines() if line.strip()]

    @r.step("每个主题出一张")
    def _():
        for tid in themes:
            r.ipc("setTheme", tid)
            time.sleep(0.6)
            r.ipc("toggle")
            r.wait_until(r.rope_shown, 3, "toggle 展开")
            time.sleep(1.0)
            r.shot("主题-" + tid)
            r.ipc("toggle")
            r.wait_until(lambda: not r.rope_shown(), 3, "toggle 收起")
        r.ipc("setTheme", "polaroid-blue")
        return f"{len(themes)} 个主题"
    _()

    @r.step("取下一张（悬停红叉）")
    def _():
        n = len(r.state_paths())
        r.hover_reveal(n)
        # 红叉在相框左上角
        x = int(slot_x(0, n)) - CARD_W // 2 + 4
        r.pointer.glide(x, 32, steps=6)
        time.sleep(0.3)
        r.shot("红叉")
        r.pointer.click()
        r.wait_until(lambda: len(r.state_paths()) == n - 1, 4, "少一张")
    _()

    @r.step("ipc clear 清空")
    def _():
        r.ipc("clear")
        r.wait_log(r"\[xitu\] 清空绳子", 4, "清空绳子")
        r.wait_until(lambda: r.state_paths() == [], 4, "state.json 清空")
        r.ipc("peek")
        time.sleep(1.0)
        r.shot("清空后")
    _()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="ui-shots")
    args = ap.parse_args()
    r = Run(args.out)
    r.qs = None
    fatal = None
    try:
        r.start_sway()
        r.pointer = VirtualPointer(W, H, display=r.env["WAYLAND_DISPLAY"], runtime_dir=r.env["XDG_RUNTIME_DIR"])
        r.start_qs()
        scenario(r)
    except Exception as e:  # noqa: BLE001
        fatal = f"{type(e).__name__}: {e}"
        traceback.print_exc()
    finally:
        r.cleanup()

    bad = r.bad_log_lines() if r.qs_log.exists() else []
    ok = fatal is None and bad == [] and all(x[1] for x in r.results)

    md = ["## Linux 界面测试（无头 Sway + Quickshell）", ""]
    if fatal:
        md += [f"**没跑完：** {fatal}", ""]
    md += ["| 步骤 | 结果 | 说明 | 截图 |", "|---|---|---|---|"]
    for title, passed, note, shots in r.results:
        md.append(f"| {title} | {'✅' if passed else '❌'} | {note.replace('|', '/')} | {', '.join(shots)} |")
    md += ["", f"日志里的 QML 警告 / 错误：{len(bad)} 行"]
    if bad:
        md += ["```"] + bad[:60] + ["```"]
    (r.out / "summary.md").write_text("\n".join(md) + "\n", encoding="utf-8")
    # 分支首页直接显示截图
    readme = md + ["", "## 截图", ""]
    for f in r.shots:
        readme += [f"### {f}", "", f"![{f}](<{f}>)", ""]
    readme += ["日志：[quickshell.log](quickshell.log) · [sway.log](sway.log)"]
    (r.out / "README.md").write_text("\n".join(readme) + "\n", encoding="utf-8")
    print("\n".join(md))
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as f:
            f.write("\n".join(md) + "\n")
    shutil.rmtree(r.tmp, ignore_errors=True)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
