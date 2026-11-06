"""进图库：解码、命名、去重、各个 import 入口。"""

import base64
import io
import json
import os
import threading
from datetime import datetime
from http.server import BaseHTTPRequestHandler, HTTPServer

import pytest
from PIL import Image

from conftest import make_image


# ---- to_png ----

def test_to_png_keeps_png_bytes_unchanged(helper, sample_png):
    assert helper.to_png(sample_png) == sample_png


def test_to_png_converts_jpeg(helper, sample_jpeg):
    out = helper.to_png(sample_jpeg)
    with Image.open(io.BytesIO(out)) as im:
        assert im.format == "PNG"
        assert im.size == (8, 6)


def test_to_png_converts_cmyk_to_rgba(helper):
    out = helper.to_png(make_image("JPEG", mode="CMYK"))
    with Image.open(io.BytesIO(out)) as im:
        assert im.format == "PNG"
        assert im.mode == "RGBA"


def test_to_png_rejects_truncated_png(helper):
    big = make_image("PNG", size=(64, 64), color=(1, 2, 3))
    with pytest.raises(helper.ImageDecodeError):
        helper.to_png(big[: len(big) // 2])


def test_to_png_rejects_garbage(helper):
    with pytest.raises(helper.ImageDecodeError):
        helper.to_png(b"not an image")


# ---- 命名 ----

WHEN = datetime(2026, 3, 4, 5, 6, 7)


def test_unique_named_counts_from_two(helper, tmp_path):
    d = tmp_path / "2026-03"
    first = helper.unique_named(d, "截图", WHEN)
    assert first.name == "截图_20260304_050607.png"
    first.write_bytes(b"x")
    second = helper.unique_named(d, "截图", WHEN)
    assert second.name == "截图_20260304_050607_2.png"
    second.write_bytes(b"x")
    assert helper.unique_named(d, "截图", WHEN).name == "截图_20260304_050607_3.png"


def test_unique_path_uses_month_folder(helper, tmp_path):
    p = helper.unique_path(tmp_path, "文件", WHEN)
    assert p.parent == tmp_path / "2026-03"


@pytest.mark.parametrize("name, ok", [
    ("截图_20260304_050607.png", True),
    ("剪贴板_20260304_050607_12.PNG", True),
    ("其他_20260304_050607.png", True),
    ("截图_2026034_050607.png", False),
    ("random.png", False),
    ("截图_20260304_050607.jpg", False),
])
def test_name_pattern(helper, name, ok):
    assert (helper.NAME_PATTERN.match(name) is not None) == ok


def test_is_owned(helper, library):
    (library / "2026-03").mkdir()
    (library / "收藏").mkdir()
    (library / "misc").mkdir()
    assert helper.is_owned(library / "2026-03" / "截图_20260304_050607.png")
    assert helper.is_owned(library / "收藏" / "截图_20260304_050607.png")
    assert not helper.is_owned(library / "misc" / "截图_20260304_050607.png")
    assert not helper.is_owned(library / "2026-03" / "holiday.png")
    assert not helper.is_owned(library / "截图_20260304_050607.png")
    assert not helper.is_owned(library.parent / "2026-03" / "截图_20260304_050607.png")


# ---- import_bytes ----

def test_import_bytes_writes_png_into_month_folder(helper, library, sample_png):
    p = helper.import_bytes(sample_png, "截图", WHEN)
    assert p == library / "2026-03" / "截图_20260304_050607.png"
    assert p.read_bytes() == sample_png
    assert not p.with_suffix(".tmp").exists()


def test_import_bytes_dedupes_same_content(helper, library, sample_png):
    first = helper.import_bytes(sample_png, "截图", WHEN)
    second = helper.import_bytes(sample_png, "剪贴板", datetime(2026, 5, 1))
    assert second == first
    assert len(list(helper.owned_files(library))) == 1


def test_import_bytes_dedupes_after_jpeg_conversion(helper, library, sample_jpeg):
    first = helper.import_bytes(sample_jpeg, "拖入", WHEN)
    assert helper.import_bytes(sample_jpeg, "拖入", WHEN) == first


def test_import_bytes_different_content_gets_suffix(helper, library):
    a = helper.import_bytes(make_image(color=(1, 1, 1)), "截图", WHEN)
    b = helper.import_bytes(make_image(color=(2, 2, 2)), "截图", WHEN)
    assert a.name == "截图_20260304_050607.png"
    assert b.name == "截图_20260304_050607_2.png"


def test_import_bytes_unknown_label_becomes_other(helper, library, sample_png):
    p = helper.import_bytes(sample_png, "whatever", WHEN)
    assert p.name.startswith("其他_")


def test_import_bytes_bad_data_returns_none(helper, library):
    assert helper.import_bytes(b"\x89PNG\r\n\x1a\nbroken", "截图", WHEN) is None
    assert list(library.rglob("*")) == []


def test_hash_cache_is_written_and_reused(helper, library, sample_png, monkeypatch):
    helper.import_bytes(sample_png, "截图", WHEN)
    other = make_image(color=(9, 9, 9))
    helper.import_bytes(other, "截图", WHEN)
    helper.find_by_hash(library, "0" * 64)   # 刚写进去的那张这时才进缓存
    assert len(json.loads(helper.HASH_CACHE.read_text(encoding="utf-8"))) == 2
    # 缓存命中后不该再读文件内容
    calls = []
    real = helper.sha256_bytes
    monkeypatch.setattr(helper, "sha256_bytes", lambda d: calls.append(len(d)) or real(d))
    helper.find_by_hash(library, real(sample_png))
    assert calls == []


# ---- 命令入口 ----

def test_cmd_import_uses_file_mtime(helper, library, tmp_path, sample_png, capsys):
    src = tmp_path / "shot.png"
    src.write_bytes(sample_png)
    stamp = WHEN.timestamp()
    os.utime(src, (stamp, stamp))
    assert helper.cmd_import(str(src), "截图") == 0
    out = capsys.readouterr().out.strip()
    assert out == str(library / "2026-03" / "截图_20260304_050607.png")


def test_cmd_import_owned_file_returns_itself(helper, library, sample_png, capsys):
    p = helper.import_bytes(sample_png, "截图", WHEN)
    capsys.readouterr()
    assert helper.cmd_import(str(p), "文件") == 0
    assert capsys.readouterr().out.strip() == str(p.resolve())


def test_cmd_import_bad_file_fails(helper, library, tmp_path, capsys):
    src = tmp_path / "bad.png"
    src.write_bytes(b"nope")
    assert helper.cmd_import(str(src), "文件") == 1
    assert "解不开" in capsys.readouterr().err


def test_cmd_import_b64(helper, library, sample_png, monkeypatch, capsys):
    monkeypatch.setattr("sys.stdin", io.TextIOWrapper(io.BytesIO(base64.b64encode(sample_png))))
    assert helper.cmd_import_b64("拖入") == 0
    out = capsys.readouterr().out.strip()
    assert os.path.basename(out).startswith("拖入_")


def test_cmd_import_b64_empty(helper, library, monkeypatch, capsys):
    monkeypatch.setattr("sys.stdin", io.TextIOWrapper(io.BytesIO(b"")))
    assert helper.cmd_import_b64("拖入") == 1


# ---- fetch ----

@pytest.mark.parametrize("url", ["file:///etc/passwd", "ftp://example.com/a.png", "http://", "a.png"])
def test_fetch_rejects_non_http(helper, url, capsys):
    assert helper.cmd_fetch(url) == 1
    assert "http(s)" in capsys.readouterr().err


@pytest.fixture
def image_server(sample_png, monkeypatch):
    for var in ("http_proxy", "HTTP_PROXY", "https_proxy", "HTTPS_PROXY", "all_proxy", "ALL_PROXY"):
        monkeypatch.delenv(var, raising=False)
    routes = {
        "/a.png": ("image/png", sample_png),
        "/page.html": ("text/html", b"<html></html>"),
    }

    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path == "/to-file":
                self.send_response(302)
                self.send_header("Location", "file:///etc/passwd")
                self.end_headers()
                return
            ctype, body = routes.get(self.path, ("text/plain", b""))
            self.send_response(200 if self.path in routes else 404)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, *args):
            pass

    server = HTTPServer(("127.0.0.1", 0), Handler)
    t = threading.Thread(target=server.serve_forever, daemon=True)
    t.start()
    yield f"http://127.0.0.1:{server.server_port}"
    server.shutdown()


def test_fetch_downloads_into_library(helper, library, image_server, sample_png, capsys):
    assert helper.cmd_fetch(image_server + "/a.png") == 0
    out = capsys.readouterr().out.strip()
    assert os.path.basename(out).startswith("拖入_")
    with open(out, "rb") as f:
        assert f.read() == sample_png
    assert list((helper.DATA / "fetch-tmp").iterdir()) == []


def test_fetch_rejects_non_image(helper, library, image_server, capsys):
    assert helper.cmd_fetch(image_server + "/page.html") == 1
    assert "不是图片" in capsys.readouterr().err


def test_fetch_refuses_redirect_to_file(helper, library, image_server, capsys):
    assert helper.cmd_fetch(image_server + "/to-file") == 1
    assert list(library.rglob("*.png")) == []


def test_fetch_rejects_oversized(helper, library, image_server, monkeypatch, capsys):
    monkeypatch.setattr(helper, "FETCH_MAX", 10)
    assert helper.cmd_fetch(image_server + "/a.png") == 1
    assert "30 MB" in capsys.readouterr().err
