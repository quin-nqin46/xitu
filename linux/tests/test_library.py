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


# ---- sweep（过期清理，同 Windows Retention.cs）----

def _old(helper, library, name, folder="2020-01"):
    d = library / folder
    d.mkdir(exist_ok=True)
    p = d / name
    p.write_bytes(b"x")
    return p


def _fake_gio(monkeypatch):
    seen = []

    def fake_run(cmd, **kw):
        seen.append(cmd)
        return SimpleNamespace(returncode=0, stdout="", stderr="")

    monkeypatch.setattr(subprocess, "run", fake_run)
    return seen


def test_sweep_trashes_only_expired_unkept(helper, library, monkeypatch, capsys):
    old = _old(helper, library, "截图_20200101_000000.png")
    kept = _old(helper, library, "截图_20200102_000000.png")
    fav = _old(helper, library, "截图_20200103_000000.png", folder="收藏")
    new = _old(helper, library, datetime.now().strftime("截图_%Y%m%d_%H%M%S.png"),
               folder=datetime.now().strftime("%Y-%m"))
    foreign = _old(helper, library, "notmine.png")
    seen = _fake_gio(monkeypatch)
    assert helper.cmd_sweep([str(kept)]) == 0
    assert capsys.readouterr().out.strip() == "1"
    assert seen == [["gio", "trash", str(old)]]
    for p in (kept, fav, new, foreign):
        assert p.exists()


def test_sweep_forever_does_nothing(helper, library, monkeypatch, capsys):
    helper.SETTINGS_FILE.write_text(json.dumps({"LibraryPath": str(library), "RetentionDays": 0}))
    _old(helper, library, "截图_20200101_000000.png")
    seen = _fake_gio(monkeypatch)
    assert helper.cmd_sweep([]) == 0
    assert capsys.readouterr().out.strip() == "0"
    assert seen == []


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


BROWSER_HTML = b'<meta charset="utf-8"><img src="https://example.com/a.png">&nbsp;'


@pytest.mark.parametrize("types", [["text/html", "image/png"], ["text/html", "text/plain", "image/png"]])
def test_clip_accepts_browser_copy_image_by_default(helper, library, monkeypatch, sample_png, capsys, types):
    monkeypatch.setattr(subprocess, "run", FakeWlPaste(types, {"image/png": sample_png, "text/html": BROWSER_HTML}))
    assert helper.cmd_clip() == 0
    assert capsys.readouterr().out.strip() != ""


def _collect(helper, library, mode):
    helper.SETTINGS_FILE.write_text(json.dumps({"LibraryPath": str(library), "Collect": mode}))


def test_clip_screenshots_mode_skips_copied_image(helper, library, monkeypatch, sample_png, capsys):
    _collect(helper, library, "screenshots")
    monkeypatch.setattr(subprocess, "run", FakeWlPaste(
        ["text/html", "image/png"], {"image/png": sample_png, "text/html": BROWSER_HTML}))
    assert helper.cmd_clip() == 0
    assert capsys.readouterr().out == ""


def test_clip_screenshots_mode_keeps_plain_image(helper, library, monkeypatch, sample_png, capsys):
    _collect(helper, library, "screenshots")
    monkeypatch.setattr(subprocess, "run", FakeWlPaste(["image/png"], {"image/png": sample_png}))
    assert helper.cmd_clip() == 0
    assert capsys.readouterr().out.strip() != ""


def test_clip_own_mode_takes_nothing(helper, library, monkeypatch, sample_png, capsys):
    _collect(helper, library, "own")
    monkeypatch.setattr(subprocess, "run", FakeWlPaste(["image/png"], {"image/png": sample_png}))
    assert helper.cmd_clip() == 0
    assert capsys.readouterr().out == ""


def test_auto_import_respects_own_mode(helper, library, tmp_path, sample_png, capsys):
    shot = tmp_path / "shot.png"
    shot.write_bytes(sample_png)
    _collect(helper, library, "own")
    assert helper.cmd_auto_import(str(shot)) == 0
    assert capsys.readouterr().out == ""
    _collect(helper, library, "all")
    assert helper.cmd_auto_import(str(shot)) == 0
    assert "截图_" in capsys.readouterr().out


def test_collect_mode_defaults_to_all(helper):
    assert helper.collect_mode({}) == "all"
    assert helper.collect_mode({"Collect": "bogus"}) == "all"


@pytest.mark.parametrize("extra", ["text/plain;charset=utf-8", "UTF8_STRING", "text/uri-list"])
def test_clip_skips_image_with_text_or_files(helper, library, monkeypatch, sample_png, capsys, extra):
    monkeypatch.setattr(subprocess, "run", FakeWlPaste([extra, "image/png"], {"image/png": sample_png}))
    assert helper.cmd_clip() == 0
    assert capsys.readouterr().out == ""


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


def test_clip_labels_screenshot_in_custom_dir(helper, library, monkeypatch, tmp_path, sample_png, capsys):
    shots = tmp_path / "shots"
    shots.mkdir()
    (shots / "s.png").write_bytes(sample_png)
    monkeypatch.setenv("XITU_SCREENSHOT_DIR", str(shots))
    monkeypatch.setattr(subprocess, "run", FakeWlPaste(["image/png"], {"image/png": sample_png}))
    assert helper.cmd_clip() == 0
    assert "截图_" in capsys.readouterr().out


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


# ---- 设置窗口 ----

def test_settings_get_defaults(helper, library, capsys):
    put(helper, (9, 0, 0))
    assert helper.cmd_settings_get() == 0
    data = json.loads(capsys.readouterr().out)
    assert data["libraryPath"] == str(library)
    assert data["retentionDays"] == 30
    assert [o["days"] for o in data["retentionOptions"]] == [7, 30, 90, 365, 0]
    assert data["collect"] == "all"
    assert [o["value"] for o in data["collectOptions"]] == ["all", "screenshots", "own"]
    assert data["theme"] == "polaroid-blue"
    assert any(t["id"] == "red-rope" for t in data["themes"])
    assert data["count"] == 1
    assert data["occupied"] > 0


@pytest.mark.parametrize("key, value, stored", [("RetentionDays", "7", 7), ("RetentionDays", "0", 0), ("Collect", "own", "own")])
def test_settings_set_keeps_other_fields(helper, library, key, value, stored):
    assert helper.cmd_settings_set(key, value) == 0
    data = json.loads(helper.SETTINGS_FILE.read_text(encoding="utf-8"))
    assert data[key] == stored
    assert data["LibraryPath"] == str(library)


@pytest.mark.parametrize("key, value", [("RetentionDays", "14"), ("RetentionDays", "x"), ("Collect", "nope"), ("Theme", "red-rope")])
def test_settings_set_rejects_bad_values(helper, library, key, value):
    before = helper.SETTINGS_FILE.read_text(encoding="utf-8")
    assert helper.cmd_settings_set(key, value) == 1
    assert helper.SETTINGS_FILE.read_text(encoding="utf-8") == before


def test_library_move_moves_owned_files_only(helper, library, tmp_path, capsys):
    a = put(helper, (1, 2, 3))
    fav = helper.import_bytes(make_image(color=(4, 5, 6)), "剪贴板", WHEN)
    assert helper.cmd_favorite(str(fav), "1") == 0
    fav = library / "收藏" / fav.name
    stray = library / "2026-03" / "notes.png"
    stray.write_bytes(b"x")
    capsys.readouterr()
    dest = tmp_path / "elsewhere"
    assert helper.cmd_library_move(str(dest), "1") == 0
    out = json.loads(capsys.readouterr().out)
    assert out["root"] == str(dest.resolve())
    pairs = {old: new for old, new in out["moved"]}
    assert pairs[str(a.resolve())] == str((dest / "2026-03" / a.name).resolve())
    assert pairs[str(fav.resolve())] == str((dest / "收藏" / fav.name).resolve())
    assert stray.exists()  # 别人的文件不碰
    assert not a.exists()
    assert helper.library_root() == dest.resolve()


def test_library_move_without_moving_keeps_files(helper, library, tmp_path, capsys):
    a = put(helper, (1, 2, 3))
    capsys.readouterr()
    dest = tmp_path / "fresh"
    assert helper.cmd_library_move(str(dest), "0") == 0
    assert json.loads(capsys.readouterr().out)["moved"] == []
    assert a.exists()
    assert (dest / "收藏").is_dir()
    assert helper.library_root() == dest.resolve()


def test_library_move_renames_on_clash(helper, library, tmp_path, capsys):
    a = put(helper, (1, 2, 3))
    dest = tmp_path / "other"
    (dest / "2026-03").mkdir(parents=True)
    (dest / "2026-03" / a.name).write_bytes(b"old")
    capsys.readouterr()
    assert helper.cmd_library_move(str(dest), "1") == 0
    new = json.loads(capsys.readouterr().out)["moved"][0][1]
    assert new.endswith("_2.png")
    assert helper.NAME_PATTERN.match(new.rsplit("/", 1)[1])
    assert (dest / "2026-03" / a.name).read_bytes() == b"old"


def test_library_move_refuses_inside_old_library(helper, library):
    before = helper.SETTINGS_FILE.read_text(encoding="utf-8")
    assert helper.cmd_library_move(str(library / "sub"), "1") == 1
    assert not (library / "sub").exists()
    assert helper.SETTINGS_FILE.read_text(encoding="utf-8") == before


def test_library_move_refuses_relative_path(helper, library):
    assert helper.cmd_library_move("relative/dir", "1") == 1


# ---- 编辑 ----

def _which(*have):
    return lambda name: "/usr/bin/" + name if name in have else None


def test_editor_prefers_env(helper):
    name, cmd = helper.editor_command("/p/a.png", {"XITU_EDITOR": "gimp -n"}, _which("satty"))
    assert name == "gimp" and cmd == ["gimp", "-n", "/p/a.png"]


def test_editor_env_placeholder(helper):
    _, cmd = helper.editor_command("/p/a b.png", {"XITU_EDITOR": "pinta --open {}"}, _which())
    assert cmd == ["pinta", "--open", "/p/a b.png"]


@pytest.mark.parametrize("have, name, cmd", [
    (("satty", "swappy"), "satty", ["satty", "--filename", "/p/a.png", "--output-filename", "/p/a.png"]),
    (("swappy",), "swappy", ["swappy", "-f", "/p/a.png", "-o", "/p/a.png"]),
    ((), "xdg-open", ["xdg-open", "/p/a.png"]),
])
def test_editor_fallbacks(helper, have, name, cmd):
    assert helper.editor_command("/p/a.png", {}, _which(*have)) == (name, cmd)


def test_edit_refuses_foreign_file(helper, library, tmp_path):
    other = tmp_path / "x.png"
    other.write_bytes(make_image())
    assert helper.cmd_edit(str(other)) == 1


def test_edit_launches_editor(helper, library, monkeypatch, capsys):
    a = put(helper, (7, 7, 7))
    launched = []
    monkeypatch.setattr(helper.subprocess, "Popen", lambda cmd, **kw: launched.append(cmd))
    monkeypatch.setenv("XITU_EDITOR", "myedit")
    assert helper.cmd_edit(str(a)) == 0
    assert launched == [["myedit", str(a.resolve())]]
    assert capsys.readouterr().out.strip() == "myedit"


# ---- 自带截图 ----

def test_window_boxes_only_visible_workspaces(helper):
    monitors = [{"activeWorkspace": {"id": 1}, "specialWorkspace": {"id": 0}},
                {"activeWorkspace": {"id": 4}, "specialWorkspace": {"id": -98}}]
    clients = [
        {"at": [10, 20], "size": [300, 200], "workspace": {"id": 1}, "title": "终端  窗口"},
        {"at": [0, 0], "size": [100, 100], "workspace": {"id": 2}, "title": "看不见"},
        {"at": [1920, 0], "size": [800, 600], "workspace": {"id": 4}, "class": "firefox"},
        {"at": [5, 5], "size": [50, 50], "workspace": {"id": -98}, "title": "special"},
        {"at": [1, 1], "size": [9, 9], "workspace": {"id": 1}, "hidden": True},
        {"at": [1, 1], "size": [0, 9], "workspace": {"id": 1}},
    ]
    assert helper.window_boxes(clients, monitors) == [
        "10,20 300x200 终端 窗口",
        "1920,0 800x600 firefox",
        "5,5 50x50 special",
    ]


def _fake_run(helper, monkeypatch, slurp_out, slurp_code=0, png=None):
    calls = []

    def run(cmd, **kw):
        calls.append(cmd)
        if cmd[0] == "hyprctl":
            return SimpleNamespace(returncode=0, stdout="[]")
        if cmd[0] == "slurp":
            return SimpleNamespace(returncode=slurp_code, stdout=slurp_out)
        if cmd[0] == "grim":
            return SimpleNamespace(returncode=0, stdout=png, stderr=b"")
        raise AssertionError(cmd)

    monkeypatch.setattr(helper.subprocess, "run", run)
    monkeypatch.setattr("shutil.which", lambda name: "/usr/bin/" + name)
    return calls


def test_capture_imports_and_copies(helper, library, monkeypatch, capsys):
    calls = _fake_run(helper, monkeypatch, "10,20 30x40\n", png=make_image(color=(3, 3, 3)))
    copied = []
    monkeypatch.setattr(helper, "cmd_copy", lambda p: copied.append(p))
    assert helper.cmd_capture() == 0
    out = capsys.readouterr().out.strip()
    assert out.startswith(str(library.resolve())) and "/截图_" in out
    assert ["grim", "-g", "10,20 30x40", "-"] in calls
    assert copied == [out]


def test_capture_respects_copy_setting(helper, library, monkeypatch, capsys):
    helper.SETTINGS_FILE.write_text(json.dumps({"LibraryPath": str(library), "CopyToClipboard": False}), encoding="utf-8")
    _fake_run(helper, monkeypatch, "0,0 5x5\n", png=make_image(color=(4, 4, 4)))
    copied = []
    monkeypatch.setattr(helper, "cmd_copy", lambda p: copied.append(p))
    assert helper.cmd_capture() == 0
    assert copied == []


def test_capture_cancelled(helper, library, monkeypatch, capsys):
    calls = _fake_run(helper, monkeypatch, "", slurp_code=1)
    assert helper.cmd_capture() == 0
    assert capsys.readouterr().out == ""
    assert not any(c[0] == "grim" for c in calls)


def test_capture_without_tools(helper, library, monkeypatch):
    monkeypatch.setattr("shutil.which", lambda name: None if name == "slurp" else "/usr/bin/" + name)
    assert helper.cmd_capture() == 3


# ---- 另存为 ----

def test_save_as_copies_and_adds_extension(helper, library, tmp_path, capsys):
    a = put(helper, (8, 8, 8))
    dest = tmp_path / "out" / "给同事"
    assert helper.cmd_save_as(str(a), str(dest)) == 0
    saved = tmp_path / "out" / "给同事.png"
    assert capsys.readouterr().out.strip() == str(saved)
    assert saved.read_bytes() == a.read_bytes()
    assert a.exists()  # 原图不动


def test_save_as_refuses_foreign_file(helper, library, tmp_path):
    other = tmp_path / "x.png"
    other.write_bytes(make_image())
    assert helper.cmd_save_as(str(other), str(tmp_path / "y.png")) == 1


def test_save_as_onto_itself_is_noop(helper, library, capsys):
    a = put(helper, (9, 9, 9))
    before = a.read_bytes()
    assert helper.cmd_save_as(str(a), str(a)) == 0
    assert a.read_bytes() == before


def test_save_as_dialog_cancelled(helper, library, monkeypatch, capsys):
    a = put(helper, (10, 10, 10))
    monkeypatch.setattr("shutil.which", lambda name: "/usr/bin/zenity" if name == "zenity" else None)
    monkeypatch.setattr(helper.subprocess, "run", lambda cmd, **kw: SimpleNamespace(returncode=1, stdout=""))
    assert helper.cmd_save_as(str(a)) == 0
    assert capsys.readouterr().out == ""


def test_save_as_without_dialog_tool(helper, library, monkeypatch):
    a = put(helper, (11, 11, 11))
    monkeypatch.setattr("shutil.which", lambda name: None)
    assert helper.cmd_save_as(str(a)) == 3


def test_save_dialog_prefers_zenity(helper):
    cmd = helper.save_dialog_command("/p/a.png", lambda n: "/usr/bin/" + n)
    assert cmd[0] == "zenity" and "--save" in cmd and "--filename=/p/a.png" in cmd
    cmd = helper.save_dialog_command("/p/a.png", lambda n: "/usr/bin/kdialog" if n == "kdialog" else None)
    assert cmd[:3] == ["kdialog", "--getsavefilename", "/p/a.png"]
