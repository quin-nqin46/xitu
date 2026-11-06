"""跑 shared/tests/*.json 里两端共用的用例。Windows 那边 windows/tests/Xitu.Tests/SharedCaseTests.cs 读同一份文件。

layout.js 是 QML 的 JS 库，用 node 跑：去掉 .pragma 行，Qt.rgba 换成占位。
"""

import json
import shutil
import subprocess
from pathlib import Path

import pytest

SHARED = Path(__file__).resolve().parents[2] / "shared" / "tests"
LAYOUT_JS = Path(__file__).resolve().parents[1] / "layout.js"

CLIP = json.loads((SHARED / "clipboard.json").read_text(encoding="utf-8"))
LAYOUT = json.loads((SHARED / "layout.json").read_text(encoding="utf-8"))

MIME = {
    "bitmap": "image/png",
    "files": "text/uri-list",
    "rtf": "text/rtf",
    "html": "text/html",
    "text": "text/plain;charset=utf-8",
}


@pytest.mark.parametrize("case", CLIP["cases"], ids=[c["name"] for c in CLIP["cases"]])
def test_clipboard(helper, monkeypatch, case):
    html = case.get("html", "")
    monkeypatch.setattr(helper, "html_is_lone_image", lambda: helper.html_lone_image(html))
    types = [MIME[f] for f in case["formats"]]
    assert helper.clip_should_take(types, {"Collect": case["collect"]}) is case["take"]


@pytest.mark.parametrize("case", LAYOUT["retentionLabel"], ids=lambda c: str(c["days"]))
def test_retention_label(helper, case):
    assert helper.retention_label({"RetentionDays": case["days"]}) == case["label"]


def _run_layout(expr):
    node = shutil.which("node")
    assert node, "跑 layout.js 的用例需要 node"
    src = LAYOUT_JS.read_text(encoding="utf-8")
    src = "\n".join(line for line in src.splitlines() if not line.startswith(".pragma"))
    src = src.replace("Qt.rgba(", "(function(){return 0;})(")
    out = subprocess.run([node, "-e", src + "\nconsole.log(JSON.stringify(" + expr + "));"],
                         capture_output=True, text=True, check=True)
    return json.loads(out.stdout)


def test_capacity():
    widths = [c["width"] for c in LAYOUT["capacity"]]
    got = _run_layout(json.dumps(widths) + ".map(capacity)")
    assert got == [c["items"] for c in LAYOUT["capacity"]]


def test_trigger_span():
    args = [[c["count"], c["width"]] for c in LAYOUT["triggerSpan"]]
    got = _run_layout(json.dumps(args) + ".map(a => triggerSpan(a[0], a[1]))")
    assert got == [{"left": c["left"], "right": c["right"]} for c in LAYOUT["triggerSpan"]]
