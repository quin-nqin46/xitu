"""主题：补字段、校验颜色、读写 settings.json、切换。"""

import json

import pytest

BUILTIN_IDS = ["polaroid-blue", "red-rope", "graphite", "linen"]


def test_builtin_themes_load(helper):
    for tid in BUILTIN_IDS + ["caelestia"]:
        spec = helper.load_theme(tid)
        assert spec is not None, tid
        assert spec["id"] == tid
        assert set(spec) >= {"name", "accent", "rope", "ends", "clip", "frame", "hint", "gallery"}


def test_normalize_fills_missing_fields(helper):
    spec = helper.normalize_theme({"name": "自定义", "rope": {"width": "5"}}, helper.THEME_FALLBACK)
    assert spec["name"] == "自定义"
    assert spec["rope"]["width"] == 5.0
    assert spec["rope"]["color"] == "accent-strong"
    assert spec["frame"]["padBottom"] == 22.0
    assert spec["gallery"]["bg"] == "#F6F1E7"


@pytest.mark.parametrize("value, expected", [
    ("#112233", "#112233"),
    ("#11223344", "#11223344"),
    (" rgba(1, 2, 3, 0.5) ", "rgba(1, 2, 3, 0.5)"),
    ("rgb(1,2,3)", "rgb(1,2,3)"),
    ("accent", "accent"),
    ("red", "#DEFAULT"),
    ("#12345", "#DEFAULT"),
    ("url(javascript:x)", "#DEFAULT"),
    (42, "#DEFAULT"),
])
def test_color_validation(helper, value, expected):
    assert helper._color(value, "#DEFAULT") == expected


def test_color_allow_empty(helper):
    assert helper._color("", "#D", allow_empty=True) == ""
    assert helper._color("", "#D") == "#D"


@pytest.mark.parametrize("value, expected", [("3", 3.0), (2.5, 2.5), ("x", 7.0), (None, 7.0), (float("nan"), 7.0)])
def test_num(helper, value, expected):
    assert helper._num(value, 7) == expected


def test_normalize_rejects_bad_enums(helper):
    spec = helper.normalize_theme(
        {"ends": {"style": "spike"}, "rope": {"texture": "glitter"}, "clip": {"shadow": "yes"}, "name": "  "},
        helper.THEME_FALLBACK,
    )
    assert spec["ends"]["style"] == "pin"
    assert spec["rope"]["texture"] == "none"
    assert spec["clip"]["shadow"] is True
    assert spec["name"] == helper.THEME_FALLBACK["name"]


def test_normalize_non_dict_gives_template(helper):
    assert helper.normalize_theme("garbage", helper.THEME_FALLBACK) == helper.normalize_theme({}, helper.THEME_FALLBACK)


def test_theme_file_rejects_bad_ids(helper):
    for tid in ("../etc/passwd", "", "-x", "a/b", None):
        assert helper.theme_file(tid) is None


def test_user_theme_overrides_builtin(helper):
    user_dir = helper.DATA / "themes"
    user_dir.mkdir(parents=True)
    (user_dir / "graphite.json").write_text(json.dumps({"name": "我的石墨"}), encoding="utf-8")
    assert helper.load_theme("graphite")["name"] == "我的石墨"


def test_broken_user_theme_returns_none(helper):
    user_dir = helper.DATA / "themes"
    user_dir.mkdir(parents=True)
    (user_dir / "mine.json").write_text("{nope", encoding="utf-8")
    assert helper.load_theme("mine") is None


def test_theme_list_order(helper, capsys):
    user_dir = helper.DATA / "themes"
    user_dir.mkdir(parents=True)
    (user_dir / "zz-mine.json").write_text(json.dumps({"name": "我的"}), encoding="utf-8")
    ids = [tid for tid, _ in helper.list_themes()]
    assert ids[:4] == BUILTIN_IDS
    assert ids[-1] == "zz-mine"
    helper.cmd_theme_list()
    assert "zz-mine 我的" in capsys.readouterr().out.splitlines()


def test_theme_current_defaults(helper, capsys):
    assert helper.cmd_theme_current() == 0
    assert json.loads(capsys.readouterr().out)["id"] == "polaroid-blue"


def test_theme_current_unknown_falls_back(helper, capsys):
    helper.save_settings({"Theme": "does-not-exist"})
    assert helper.cmd_theme_current() == 0
    assert json.loads(capsys.readouterr().out)["id"] == "polaroid-blue"


def test_theme_set_keeps_other_settings(helper, capsys):
    helper.save_settings({"LibraryPath": "/x", "RetentionDays": 7})
    assert helper.cmd_theme_set("linen") == 0
    assert json.loads(capsys.readouterr().out)["id"] == "linen"
    saved = json.loads(helper.SETTINGS_FILE.read_text(encoding="utf-8"))
    assert saved == {"LibraryPath": "/x", "RetentionDays": 7, "Theme": "linen"}


def test_theme_set_unknown(helper, capsys):
    assert helper.cmd_theme_set("nope") == 1
    assert not helper.SETTINGS_FILE.exists()


def test_theme_set_refuses_to_overwrite_broken_settings(helper):
    helper.DATA.mkdir(parents=True)
    helper.SETTINGS_FILE.write_text("{broken", encoding="utf-8")
    with pytest.raises(json.JSONDecodeError):
        helper.cmd_theme_set("linen")
    assert helper.SETTINGS_FILE.read_text(encoding="utf-8") == "{broken"


def test_theme_next_cycles(helper, capsys):
    ids = [tid for tid, _ in helper.list_themes()]
    seen = []
    for _ in ids:
        helper.cmd_theme_next()
        seen.append(json.loads(capsys.readouterr().out)["id"])
    assert seen == ids[1:] + ids[:1]
