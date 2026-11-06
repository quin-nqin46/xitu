#!/usr/bin/env python3
"""在 Sway 以外的桌面上把系图跑起来，验证 Desktop.qml 的后端：

  niri  嵌在无头 Sway 里运行（winit 后端），走 wlr-foreign-toplevel：
        - 启动后桌面后端是 other，日志里没有 Hyprland
        - ipc peek 放下绳子
        - 全屏窗口时 ipc peek 不放下
  kwin  kwin_wayland --virtual（KDE 的合成器，无头，两块虚拟屏），没有 foreign-toplevel：
        - 启动后桌面后端是 other，两块屏各有一根绳子，没有 QML 报错
        - ipc peek 放下绳子（兜底在第一块屏）
        - 全屏窗口时照样放下（README 写明的 KDE 限制，这里只记录，不算失败）

没法在 KWin 上截屏（不支持 wlr-screencopy），所以放没放下看日志：XITU_DEBUG_REVEAL=1 时
RopeWindow 每次放下记「放下 <屏>」，因全屏不放下记「全屏，不放下 <屏>」。

用法：python3 linux/tests/ui/desktops.py niri|kwin --out ui-desktop-niri
"""

import argparse
import os
import re
import shutil
import subprocess
import sys
import time
import traceback
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import drive  # noqa: E402
from drive import LINUX, Run  # noqa: E402


class DesktopRun(Run):
    def __init__(self, out, desktop):
        super().__init__(out)
        self.desktop = desktop
        self.qs = None
        self.env["XITU_DEBUG_REVEAL"] = "1"
        self.env["XDG_CURRENT_DESKTOP"] = {"niri": "niri", "kwin": "KDE"}[desktop]

    def wayland_sockets(self):
        rt = Path(self.env["XDG_RUNTIME_DIR"])
        return {s.name for s in rt.glob("wayland-*") if not s.name.endswith(".lock")}

    # ---------- niri：嵌在无头 Sway 里 ----------
    def start_niri(self):
        self.start_sway()
        parent = self.env["WAYLAND_DISPLAY"]
        before = self.wayland_sockets()
        conf = self.tmp / "niri.kdl"
        conf.write_text(
            "hotkey-overlay { skip-at-startup; }\n"
            "prefer-no-csd\n"
            "animations { off; }\n"
        )
        env = dict(self.env)
        env.pop("SWAYSOCK", None)
        log = open(self.out / "niri.log", "w")
        p = subprocess.Popen(["niri", "-c", str(conf)], env=env, stdout=log, stderr=subprocess.STDOUT,
                             start_new_session=True)
        self.procs.append(p)
        rt = Path(self.env["XDG_RUNTIME_DIR"])
        for _ in range(150):
            new = self.wayland_sockets() - before - {parent}
            niri_sock = list(rt.glob("niri.*.sock"))
            if new and niri_sock:
                self.env["WAYLAND_DISPLAY"] = sorted(new)[0]
                self.env["NIRI_SOCKET"] = str(niri_sock[0])
                self.env.pop("SWAYSOCK", None)
                time.sleep(1.0)
                return
            if p.poll() is not None:
                break
            time.sleep(0.1)
        raise RuntimeError("niri 没起来，见 niri.log")

    # ---------- KWin：无头虚拟屏 ----------
    def start_kwin(self):
        # KWin 要会话总线
        out = subprocess.run(["dbus-daemon", "--session", "--fork", "--print-address=1", "--print-pid=1"],
                             env=self.env, capture_output=True, text=True, timeout=10)
        lines = out.stdout.split()
        if out.returncode != 0 or not lines:
            raise RuntimeError("dbus-daemon 起不来：" + out.stderr)
        self.env["DBUS_SESSION_BUS_ADDRESS"] = lines[0]
        if len(lines) > 1:
            self.dbus_pid = int(lines[1])
        env = dict(self.env)
        env["KWIN_COMPOSE"] = "Q"   # QPainter，不要 GPU
        env.pop("QT_QPA_PLATFORM", None)   # kwin_wayland 用自己的 QPA，不能被指到别的 Wayland 上
        log = open(self.out / "kwin.log", "w")
        p = subprocess.Popen(["kwin_wayland", "--virtual", "--no-lockscreen", "--no-global-shortcuts",
                              "--socket", "wayland-kwin", "--width", "1280", "--height", "720",
                              "--output-count", "2"],
                             env=env, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        self.procs.append(p)
        rt = Path(self.env["XDG_RUNTIME_DIR"])
        for _ in range(200):
            if (rt / "wayland-kwin").exists():
                self.env["WAYLAND_DISPLAY"] = "wayland-kwin"
                time.sleep(2.0)
                return
            if p.poll() is not None:
                break
            time.sleep(0.1)
        raise RuntimeError("kwin_wayland 没起来，见 kwin.log")

    def spawn(self, *cmd):
        p = subprocess.Popen(list(cmd), env=self.env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                             start_new_session=True)
        self.procs.append(p)
        return p

    def ensure_idle(self):
        time.sleep(0.2)

    def maybe_shot(self, name):
        if self.desktop == "niri":
            try:
                self.shot(name)
            except Exception as e:  # noqa: BLE001
                print(f"截图失败（不算失败）：{e}", flush=True)

    def reveals(self):
        return self.count_log(r"\[xitu\] 放下 ")

    def blocked(self):
        return self.count_log(r"\[xitu\] 全屏，不放下 ")

    def wait_retracted(self):
        # peek 放下后过几秒自己收起；收起前再 peek 不会再记「放下」
        time.sleep(4.5)

    def fullscreen_client(self):
        p = self.spawn("foot", "--fullscreen", "-a", "xitu-fs", "sleep", "600")
        time.sleep(3.0)   # 等窗口映射、拿到焦点、进入全屏
        if p.poll() is not None:
            raise RuntimeError("foot 没起来")
        return p


def scenario(r: DesktopRun):
    want_screens = 2 if r.desktop == "kwin" else 1

    @r.step("启动：桌面后端 other，不碰 Hyprland")
    def _():
        r.wait_log(r"\[xitu\] 桌面后端：other", 3, "桌面后端：other")
        if re.search(r"[Hh]yprland", r.log_text()):
            raise AssertionError("不该加载 Quickshell.Hyprland")
        return f"{want_screens} 块屏"
    _()

    @r.step("ipc peek 放下绳子")
    def _():
        n = r.reveals()
        r.ipc("peek")
        r.wait_until(lambda: r.reveals() > n, 4, "日志「放下」")
        time.sleep(0.6)
        r.maybe_shot("peek")
        return re.findall(r"\[xitu\] 放下 (\S+)", r.log_text())[-1]
    _()
    r.wait_retracted()

    @r.step("全屏窗口时 ipc peek")
    def _():
        p = r.fullscreen_client()
        try:
            n, b = r.reveals(), r.blocked()
            r.ipc("peek")
            time.sleep(1.5)
            r.maybe_shot("全屏时-peek")
            revealed, blocked = r.reveals() > n, r.blocked() > b
            if r.desktop == "kwin":
                # KDE 不给窗口信息：已知会放下，只记录
                return "放下了（KDE 已知限制）" if revealed else ("没放下" if blocked else "没反应")
            if revealed or not blocked:
                raise AssertionError(f"全屏时应该不放下：放下={revealed} 拦下={blocked}")
            return "没放下"
        finally:
            p.terminate()
            time.sleep(1.0)
    _()
    r.wait_retracted()

    @r.step("关掉全屏窗口后 peek 又能放下")
    def _():
        n = r.reveals()
        r.ipc("peek")
        r.wait_until(lambda: r.reveals() > n, 4, "日志「放下」")
    _()

    @r.step("图库窗口能打开")
    def _():
        r.ipc("gallery")
        time.sleep(1.5)
        r.maybe_shot("图库")
        r.ipc("closeGallery")
        time.sleep(0.5)
    _()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("desktop", choices=["niri", "kwin"])
    ap.add_argument("--out", default=None)
    args = ap.parse_args()
    out = args.out or f"ui-desktop-{args.desktop}"
    r = DesktopRun(out, args.desktop)
    fatal = None
    try:
        r.start_niri() if args.desktop == "niri" else r.start_kwin()
        r.start_qs()
        scenario(r)
    except Exception as e:  # noqa: BLE001
        fatal = f"{type(e).__name__}: {e}"
        traceback.print_exc()
    finally:
        r.cleanup()
        pid = getattr(r, "dbus_pid", None)
        if pid:
            try:
                os.kill(pid, 15)
            except OSError:
                pass

    bad = r.bad_log_lines() if r.qs_log.exists() else []
    ok = fatal is None and bad == [] and all(x[1] for x in r.results)
    name = {"niri": "niri（嵌在无头 Sway 里）", "kwin": "KDE（kwin_wayland --virtual）"}[args.desktop]
    md = [f"## Linux 界面测试：{name}", ""]
    if fatal:
        md += [f"**没跑完：** {fatal}", ""]
    md += ["| 步骤 | 结果 | 说明 | 截图 |", "|---|---|---|---|"]
    for title, passed, note, shots in r.results:
        md.append(f"| {title} | {'✅' if passed else '❌'} | {note.replace('|', '/')} | {', '.join(shots)} |")
    md += ["", f"日志里的 QML 警告 / 错误：{len(bad)} 行"]
    if bad:
        md += ["```"] + bad[:60] + ["```"]
    (r.out / "summary.md").write_text("\n".join(md) + "\n", encoding="utf-8")
    readme = md + ["", "## 截图", ""]
    for f in r.shots:
        readme += [f"### {f}", "", f"![{f}](<{f}>)", ""]
    (r.out / "README.md").write_text("\n".join(readme) + "\n", encoding="utf-8")
    print("\n".join(md))
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as f:
            f.write("\n".join(md) + "\n")
    shutil.rmtree(r.tmp, ignore_errors=True)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
