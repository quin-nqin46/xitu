"""xitu_helper.py 的测试夹具。

助手在导入时就把数据目录算好了（DATA、SETTINGS_FILE 等模块常量），
这里每个测试都把这些常量换到 tmp_path 底下，图库也指到临时目录，
所以测试不会碰到真实的 ~/.local/share/Xitu 或「图片」文件夹。
"""

import importlib.util
import io
import json
import sys
from pathlib import Path

import pytest
from PIL import Image

HELPER = Path(__file__).resolve().parents[1] / "xitu_helper.py"


def _load_helper():
    spec = importlib.util.spec_from_file_location("xitu_helper", HELPER)
    module = importlib.util.module_from_spec(spec)
    sys.modules["xitu_helper"] = module
    spec.loader.exec_module(module)
    return module


@pytest.fixture
def helper(tmp_path, monkeypatch):
    h = _load_helper()
    data = tmp_path / "data"
    pictures = tmp_path / "Pictures"
    pictures.mkdir()
    monkeypatch.setattr(h, "DATA", data)
    monkeypatch.setattr(h, "STATE_FILE", data / "state.json")
    monkeypatch.setattr(h, "SETTINGS_FILE", data / "settings.json")
    monkeypatch.setattr(h, "HASH_CACHE", data / "hashes.json")
    monkeypatch.setattr(h, "OWN_CLIP", data / "own-clipboard.json")
    monkeypatch.setattr(h, "LOG_FILE", data / "log.txt")
    monkeypatch.setattr(h, "pictures_dir", lambda: pictures)
    monkeypatch.setattr(h, "_xdg_screenshots_dir", lambda: None)
    monkeypatch.delenv("XITU_SCREENSHOT_DIR", raising=False)
    monkeypatch.delenv("XDG_SCREENSHOTS_DIR", raising=False)
    return h


@pytest.fixture
def library(helper, tmp_path):
    """settings.json 里写好 LibraryPath，返回图库根目录。"""
    root = tmp_path / "library"
    root.mkdir()
    helper.DATA.mkdir(parents=True, exist_ok=True)
    helper.SETTINGS_FILE.write_text(json.dumps({"LibraryPath": str(root)}), encoding="utf-8")
    return root


def make_image(fmt="PNG", size=(8, 6), color=(200, 30, 60), mode="RGB"):
    """生成一张小样例图的字节。颜色不同内容就不同，方便测去重。"""
    if mode == "CMYK":
        color = (0, 255, 255, 0)
    im = Image.new(mode, size, color)
    buf = io.BytesIO()
    im.save(buf, fmt)
    return buf.getvalue()


@pytest.fixture
def sample_png():
    return make_image("PNG")


@pytest.fixture
def sample_jpeg():
    return make_image("JPEG", color=(10, 120, 240))
