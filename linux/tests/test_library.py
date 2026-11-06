"""图库管理：scan、收藏、删除、状态文件、剪贴板判断和一些小工具。"""

import json
import subprocess
import time
from datetime import datetime
from types import SimpleNamespace

import pytest

from conftest import make_image

WHEN = datetime(2026, 3, 4, 5, 6, 7)


def put(helper, color, label="截图", when=WHEN):
    return helper.import_bytes(make_image(color=color), label, when)


# ---- scan ----

def test_scan_lists_owned_files_newest_first(helper, library, capsys):
    old = put(helper, (1, 0, 0), "截图", datetime(2026, 1, 2, 3, 4, 5))
    new = put(helper, (2, 0, 0), "剪贴板", datetime(2026, 2, 3, 4, 5, 6))
    (library / "2026-01" / "notes.png").write_bytes(b"x")       # 名字不对，不认
    (library / "misc").mkdir()
    (library / "misc" / "截图_20260101_000000.png").write_bytes(b"x")  # 文件夹不对，不认
    assert helper.cmd_scan() == 0
    data = json.loads(capsys.readouterr().out)
    assert [it["path"] for it in data["items"]] == [str(new.resolve()), str(old.resolve())]
    assert data["items"][0]["source"] == "剪贴板"
    assert data["items"][0]["time"] == "2026-02-03T04:05:06"
    assert data["items"][0]["favorite"] is False
    assert data["occupied"] == old.stat().st_size + new.stat().st_size
    assert data["retention"] == "30 天"
    assert data["capture"] == "Super+Shift+S"


def test_scan_empty_library(helper, tmp_path, capsys):
    helper.DATA.mkdir(parents=True)
    helper.SETTINGS_FILE.write_text(json.dumps({"LibraryPath": str(tmp_path / "nope")}), encoding="utf-8")
    assert helper.cmd_scan() == 0
    assert json.loads(capsys.readouterr().out)["items"] == []


@pytest.mark.parametrize("days, label", [(None, "30 天"), (7, "7 天"), (365, "1 年"), (0, "永久"), (14, "14 天"), ("x", "30 天")])
def test_retention_label(helper, days, label):
    settings = {} if days is None else {"RetentionDays": days}
    assert helper.retention_label(settings) == label


@pytest.mark.parametrize("settings, label", [
    ({}, "Super+Shift+S"),
    ({"CaptureModifiers": 0x0006, "CaptureKey": 0x41}, "Ctrl+Shift+A"),
    ({"CaptureModifiers": 0x0009, "CaptureKey": 0x73}, "Alt+Win+F4"),
    ({"CaptureModifiers": 0, "CaptureKey": 0x2C}, "PrtSc"),
    ({"CaptureModifiers": 0, "CaptureKey": 0x41}, "Super+Shift+S"),   # 没修饰键的普通键不算
    ({"CaptureModifiers": 2, "CaptureKey": 0x11}, "Super+Shift+S"),   # 只有修饰键
    ({"CaptureModifiers": "bad", "CaptureKey": 1}, "Super+Shift+S"),
])
def test_capture_label(helper, settings, label):
    assert helper.capture_label(settings) == label


# ---- favorite ----

def test_favorite_round_trip(helper, library, capsys):
    p = put(helper, (3, 0, 0))
    assert helper.cmd_favorite(str(p), "1") == 0
    fav = library / "收藏" / p.name
    assert capsys.readouterr().out.strip() == str(fav.resolve())
    assert fav.exists() and not p.exists()
    assert helper.cmd_favorite(str(fav), "0") == 0
    assert capsys.readouterr().out.strip() == str(p.resolve())
    assert p.exists() and not fav.exists()


def test_favorite_already_favorite_is_noop(helper, library, capsys):
    p = put(helper, (3, 1, 0))
    helper.cmd_favorite(str(p), "1")
    capsys.readouterr()
    fav = library / "收藏" / p.name
    assert helper.cmd_favorite(str(fav), "1") == 0
    assert capsys.readouterr().out.strip() == str(fav.resolve())


def test_favorite_name_clash_gets_suffix(helper, library, capsys):
    p = put(helper, (4, 0, 0))
    (library / "收藏").mkdir()
    (library / "收藏" / p.name).write_bytes(b"other")
    assert helper.cmd_favorite(str(p), "1") == 0
    out = capsys.readouterr().out.strip()
    assert out.endswith("_2.png")


def test_favorite_rejects_foreign_file(helper, library, tmp_path, capsys):
    foreign = tmp_path / "截图_20260304_050607.png"
    foreign.write_bytes(make_image())
    assert helper.cmd_favorite(str(foreign), "1") == 1
    assert foreign.exists()


def test_favorite_rejects_bad_flag(helper, library):
    assert helper.cmd_favorite("whatever", "yes") == 1


# ---- trash / reveal ----

def test_trash_refuses_foreign_file(helper, library, tmp_path, monkeypatch):
    foreign = tmp_path / "x.png"
    foreign.write_bytes(b"x")
    called = []
    monkeypatch.setattr(subprocess, "run", lambda *a, **k: called.append(a))
    assert helper.cmd_trash(str(foreign)) == 1
    assert called == []
    assert foreign.exists()


def test_trash_calls_gio(helper, library, monkeypatch, capsys):
    p = put(helper, (5, 0, 0))
    seen = []

    def fake_run(cmd, **kw):
        seen.append(cmd)
        return SimpleNamespace(returncode=0, stdout="", stderr="")

    monkeypatch.setattr(subprocess, "run", fake_run)
    assert helper.cmd_trash(str(p)) == 0
    assert seen == [["gio", "trash", str(p.resolve())]]


def test_trash_reports_gio_failure(helper, library, monkeypatch, capsys):
    p = put(helper, (5, 1, 0))
    monkeypatch.setattr(subprocess, "run", lambda *a, **k: SimpleNamespace(returncode=1, stdout="", stderr="boom"))
    assert helper.cmd_trash(str(p)) == 1
    assert "boom" in capsys.readouterr().err


def test_reveal_refuses_foreign_file(helper, library, tmp_path):
    assert helper.cmd_reveal(str(tmp_path / "x.png")) == 1


# ---- state / missing ----

def test_save_and_load_state(helper, tmp_path, capsys):
    a = tmp_path / "a.png"
    a.write_bytes(b"x")
    gone = tmp_path / "gone.png"
    assert helper.cmd_save_state([str(a), str(gone)]) == 0
    assert json.loads(helper.STATE_FILE.read_text(encoding="utf-8")) == {"Paths": [str(a), str(gone)]}
    assert helper.cmd_load_state() == 0
    assert capsys.readouterr().out.splitlines() == [str(a)]


def test_load_state_missing_or_broken(helper, capsys):
    assert helper.cmd_load_state() == 0
    helper.DATA.mkdir(parents=True)
    helper.STATE_FILE.write_text("{oops", encoding="utf-8")
    assert helper.cmd_load_state() == 0
    assert capsys.readouterr().out == ""


def test_missing(helper, tmp_path, capsys):
    a = tmp_path / "a"
    a.write_text("x")
    helper.cmd_missing([str(a), str(tmp_path / "b")])
    assert capsys.readouterr().out.splitlines() == [str(tmp_path / "b")]


# ---- 目录 ----

def test_library_root_defaults_to_pictures(helper):
    assert helper.library_root() == helper.pictures_dir() / "系图"


def test_library_root_from_settings(helper, library):
    assert helper.library_root() == library


def test_screenshot_dir_env(helper, monkeypatch, tmp_path):
    monkeypatch.setenv("XITU_SCREENSHOT_DIR", str(tmp_path / "shots"))
    assert helper.screenshot_dir() == tmp_path / "shots"


def test_screenshot_dir_prefers_existing(helper):
    pics = helper.pictures_dir()
    assert helper.screenshot_dir() == pics / "Screenshots"
    (pics / "屏幕截图").mkdir()
    assert helper.screenshot_dir() == pics / "屏幕截图"
    (pics / "Screenshots").mkdir()
    assert helper.screenshot_dir() == pics / "Screenshots"


# ---- 剪贴板 ----

class FakeWlPaste:
    """替 subprocess.run 扮演 wl-paste。"""

    def __init__(self, types, payloads):
        self.types = types
        self.payloads = payloads

    def __call__(self, cmd, **kw):
        if "--list-types" in cmd:
            return SimpleNamespace(stdout="\n".join(self.types) + "\n", returncode=0)
        mime = cmd[cmd.index("--type") + 1]
        return SimpleNamespace(stdout=self.payloads.get(mime, b""), returncode=0)


def test_clip_imports_plain_image(helper, library, monkeypatch, sample_png, capsys):
    monkeypatch.setattr(subprocess, "run", FakeWlPaste(["image/png"], {"image/png": sample_png}))
    assert helper.cmd_clip() == 0
    out = capsys.readouterr().out.strip()
    assert "剪贴板_" in out


def test_clip_ignores_text(helper, library, monkeypatch, capsys):
    monkeypatch.setattr(subprocess, "run", FakeWlPaste(["text/plain"], {}))
    assert helper.cmd_clip() == 0
    assert capsys.readouterr().out == ""


def test_clip_skips_office_bitmap(helper, library, monkeypatch, sample_png, capsys):
    html = b"<table><tr><td>1</td></tr></table>"
    monkeypatch.setattr(subprocess, "run", FakeWlPaste(
        ["text/html", "image/png"], {"image/png": sample_png, "text/html": html}))
    assert helper.cmd_clip() == 0
    assert capsys.readouterr().out == ""


def test_clip_accepts_browser_copy_image(helper, library, monkeypatch, sample_png, capsys):
    html = b'<meta charset="utf-8"><img src="https://example.com/a.png">&nbsp;'
    monkeypatch.setattr(subprocess, "run", FakeWlPaste(
        ["text/html", "image/png"], {"image/png": sample_png, "text/html": html}))
    assert helper.cmd_clip() == 0
    assert capsys.readouterr().out.strip() != ""


def test_clip_skips_rtf(helper, library, monkeypatch, sample_png, capsys):
    monkeypatch.setattr(subprocess, "run", FakeWlPaste(
        ["text/rtf", "image/png"], {"image/png": sample_png}))
    assert helper.cmd_clip() == 0
    assert capsys.readouterr().out == ""


def test_clip_skips_own_copy(helper, library, monkeypatch, sample_png, capsys):
    helper.DATA.mkdir(parents=True, exist_ok=True)
    helper.OWN_CLIP.write_text(json.dumps({"hash": helper.sha256_bytes(sample_png), "time": time.time()}))
    monkeypatch.setattr(subprocess, "run", FakeWlPaste(["image/png"], {"image/png": sample_png}))
    assert helper.cmd_clip() == 0
    assert capsys.readouterr().out == ""


def test_clip_labels_fresh_screenshot(helper, library, monkeypatch, sample_png, capsys):
    shots = helper.pictures_dir() / "屏幕截图"
    shots.mkdir()
    (shots / "s.png").write_bytes(sample_png)
    monkeypatch.setattr(subprocess, "run", FakeWlPaste(["image/png"], {"image/png": sample_png}))
    assert helper.cmd_clip() == 0
    assert "截图_" in capsys.readouterr().out


# ---- main ----

def test_main_unknown_command(helper, capsys):
    assert helper.main(["nope"]) == 2


def test_main_no_args_prints_usage(helper, capsys):
    assert helper.main([]) == 2
    assert "子命令" in capsys.readouterr().err


def test_main_logs_exceptions(helper, capsys):
    assert helper.main(["import"]) == 1       # 缺参数 -> IndexError，被兜住
    assert "import 失败" in helper.LOG_FILE.read_text(encoding="utf-8")
