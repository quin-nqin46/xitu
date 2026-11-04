#!/usr/bin/env bash
# 抽屉离屏渲染工具。用法：bash linux/tools/render-offscreen.sh
# 1) 用 Pillow 在 tools/out/ 生成 5 张纯色假截图 fake-1..5.png
# 2) 跑一遍 qs（窗口不可见：透明 + opacity 0 + 空 mask，见 render-shell.qml）：
#    XDG_STATE_HOME、XDG_DATA_HOME 都指向 mktemp 空目录（不读用户配色）→ tools/out/drawer.png
set -euo pipefail
cd "$(dirname "$0")/.."   # linux/
# 会话里可能没带 WAYLAND_DISPLAY；合成器 socket 在就补上，qs 才能 grabToImage
if [ -z "${WAYLAND_DISPLAY:-}" ]; then
    _rt="${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
    if [ -S "$_rt/wayland-1" ]; then
        export WAYLAND_DISPLAY=wayland-1
    elif [ -S "$_rt/wayland-0" ]; then
        export WAYLAND_DISPLAY=wayland-0
    fi
fi

mkdir -p tools/out
# 软链不进库，运行时现建；quickshell 的配置扫描出不了 tools 根目录，软链让它用上一级的真实组件
ln -sfn ../layout.js tools/layout.js
ln -sfn ../DrawerPanel.qml tools/DrawerPanel.qml
# render-rope.qml（绳子出图）还要用 Card.qml、Rope.qml
ln -sfn ../Card.qml tools/Card.qml
ln -sfn ../Rope.qml tools/Rope.qml

python3 - <<'EOF'
from PIL import Image
import os
d = os.path.join("tools", "out")
# 5 种颜色，fake-1 最新（大图位置），fake-5 最老
cols = [(224, 92, 74), (94, 129, 172), (163, 190, 140), (208, 135, 112), (180, 142, 173)]
for i, c in enumerate(cols, start=1):
    Image.new("RGB", (400, 300), c).save(os.path.join(d, f"fake-{i}.png"))
print("假图生成完毕")
EOF

data_dir="$(mktemp -d)"
state_dir="$(mktemp -d)"
rm -f tools/out/drawer*.png
echo "---- tools/out/drawer.png ----"
XDG_STATE_HOME="$state_dir" XDG_DATA_HOME="$data_dir" \
    timeout 30 qs -p "$PWD/tools/render-shell.qml" -d
for _ in $(seq 1 60); do
    [ -f "tools/out/drawer.png" ] && break
    sleep 0.5
done
qs kill -p "$PWD/tools/render-shell.qml" 2>/dev/null || true
rm -rf "$data_dir" "$state_dir"

command ls -l tools/out/
