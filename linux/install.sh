#!/bin/sh
# 系图 Linux 版安装脚本。在仓库或发布包的根目录运行：sh linux/install.sh
#
#   sh linux/install.sh            检查依赖，装到 ~/.local/share/Xitu/app，装命令 ~/.local/bin/xitu，
#                                  写 ~/.config/hypr/xitu.conf（开机自启 + Ctrl+Shift+A 截图），问要不要接进 hyprland.conf
#   sh linux/install.sh --yes      不问，直接接进 hyprland.conf 并启动
#   sh linux/install.sh --check    只检查依赖，打印安装命令
#   sh linux/install.sh --uninstall  删掉程序、命令和 xitu.conf（图库、设置、绳上条目都留着）
#
# 不会用 sudo 装任何东西：缺的包只打印安装命令，由你自己运行。
set -eu

SRC=$(cd "$(dirname "$0")/.." && pwd)
DATA_HOME=${XDG_DATA_HOME:-$HOME/.local/share}
APP="$DATA_HOME/Xitu/app"
BIN="$HOME/.local/bin"
HYPR_DIR=${XDG_CONFIG_HOME:-$HOME/.config}/hypr
HYPR_CONF="$HYPR_DIR/hyprland.conf"
XITU_CONF="$HYPR_DIR/xitu.conf"

mode=install
yes=0
for a in "$@"; do
    case "$a" in
        --check) mode=check ;;
        --uninstall) mode=uninstall ;;
        --yes|-y) yes=1 ;;
        -h|--help) sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "不认识的参数：$a（--help 看用法）" >&2; exit 2 ;;
    esac
done

say() { printf '%s\n' "$*"; }
have() { command -v "$1" >/dev/null 2>&1; }

# ---- 发行版和包名 ----
distro=other
if [ -r /etc/os-release ]; then
    # shellcheck disable=SC1091
    ids=$(. /etc/os-release; echo "${ID:-} ${ID_LIKE:-}")
    case " $ids " in
        *" arch "*|*" manjaro "*|*" endeavouros "*|*" cachyos "*) distro=arch ;;
        *" fedora "*|*" rhel "*) distro=fedora ;;
        *" opensuse"*|*" suse "*) distro=suse ;;
        *" alpine "*) distro=alpine ;;
        *" void "*) distro=void ;;
        *" nixos "*) distro=nixos ;;
        *" ubuntu "*) distro=ubuntu ;;
        *" debian "*) distro=debian ;;
    esac
fi

# 命令 → 包名。第二列 1 = 必需，0 = 可选
pkg_of() {
    case "$distro:$1" in
        *:qs) echo quickshell ;;
        arch:pillow|void:pillow) echo python-pillow ;;
        fedora:pillow) echo python3-pillow ;;
        suse:pillow) echo python3-Pillow ;;
        alpine:pillow) echo py3-pillow ;;
        *:pillow) echo python3-pil ;;
        *:wl-paste) echo wl-clipboard ;;
        *:inotifywait) echo inotify-tools ;;
        *:xdg-open) echo xdg-utils ;;
        debian:gio|ubuntu:gio) echo libglib2.0-bin ;;
        suse:gio) echo glib2-tools ;;
        alpine:gio|void:gio) echo glib ;;
        *:gio) echo glib2 ;;
        *:xdg-user-dir) echo xdg-user-dirs ;;
        *:grim) echo grim ;;
        *:slurp) echo slurp ;;
        debian:notify-send|ubuntu:notify-send) echo libnotify-bin ;;
        suse:notify-send) echo libnotify-tools ;;
        *:notify-send) echo libnotify ;;
        *) echo "$1" ;;
    esac
}

need_required=""
need_optional=""
check_one() {  # 命令 必需(1/0) 用途
    ok=0
    if [ "$1" = pillow ]; then
        have python3 && python3 -c 'import PIL' >/dev/null 2>&1 && ok=1
    else
        have "$1" && ok=1
    fi
    if [ $ok = 1 ]; then
        say "  ✓ $1"
    elif [ "$2" = 1 ]; then
        say "  ✗ $1（必需）$3"
        need_required="$need_required $(pkg_of "$1")"
    else
        say "  - $1（可选）$3"
        need_optional="$need_optional $(pkg_of "$1")"
    fi
}

check_deps() {
    say "检查依赖："
    if ! have python3; then
        say "  ✗ python3（必需）"
        need_required="$need_required python3"
    fi
    check_one qs 1 "Quickshell，界面运行时"
    check_one pillow 1 "Pillow，读写图片"
    check_one wl-paste 1 "剪贴板里的截图自动挂上"
    check_one inotifywait 1 "截图文件夹自动挂上"
    check_one xdg-open 1 "双击打开图片"
    check_one gio 1 "删除进回收站"
    check_one grim 0 "自带截图 Ctrl+Shift+A"
    check_one slurp 0 "自带截图框选"
    check_one xdg-user-dir 0 "找「图片」文件夹"
    check_one notify-send 0 "缺东西时弹通知"
    if have hyprctl || [ -n "${HYPRLAND_INSTANCE_SIGNATURE:-}" ]; then
        say "  ✓ Hyprland"
    else
        say "  ! 没检测到 Hyprland。系图目前只在 Hyprland 上完整支持（多屏和全屏判断走 Hyprland）。"
    fi
    # wl-paste 和 wl-copy 同一个包，去重
    # shellcheck disable=SC2086
    req=$(printf '%s\n' $need_required | sort -u | xargs)
    # shellcheck disable=SC2086
    opt=$(printf '%s\n' $need_optional | sort -u | xargs)
    if [ -z "$req" ] && [ -z "$opt" ]; then say "依赖都齐了。"; return 0; fi
    say ""
    print_install "$req" "$opt"
    [ -z "$req" ]
}

print_install() {  # 必需包 可选包
    all=$(echo "$1 $2" | xargs)
    case "$distro" in
        arch) cmd="sudo pacman -S --needed $all" ;;
        fedora) cmd="sudo dnf install $all" ;;
        suse) cmd="sudo zypper install $all" ;;
        alpine) cmd="sudo apk add $all" ;;
        void) cmd="sudo xbps-install $all" ;;
        debian|ubuntu) cmd="sudo apt install $all" ;;
        nixos) cmd="" ;;
        *) cmd="" ;;
    esac
    if [ -n "$1" ]; then say "缺必需的包：$1"; fi
    if [ -n "$2" ]; then say "可选的包：$2"; fi
    if [ -n "$cmd" ]; then
        say "安装命令（不想要可选的就从里面删掉）："
        say "  $cmd"
    else
        say "请用你的包管理器安装上面这些包。"
    fi
    case " $all " in *" quickshell "*)
        case "$distro" in
            ubuntu) say "Ubuntu 官方源没有 quickshell：用 PPA avengemedia/danklinux，或见 https://quickshell.org/docs/guide/install-setup" ;;
            debian) say "quickshell 在 Debian testing / unstable 里；稳定版见 https://quickshell.org/docs/guide/install-setup" ;;
            fedora) say "quickshell 在 Fedora 44 及以后的官方源里；更早的版本用 COPR errornointernet/quickshell" ;;
            arch) ;;
            *) say "Quickshell 的安装方法见 https://quickshell.org/docs/guide/install-setup" ;;
        esac ;;
    esac
}

# ---- 卸载 ----
if [ $mode = uninstall ]; then
    if have qs && [ -d "$APP/linux" ]; then
        qs ipc -p "$APP/linux" call xitu quit >/dev/null 2>&1 || true
    fi
    rm -rf "$APP"
    [ -f "$BIN/xitu" ] && grep -q 'Xitu/app' "$BIN/xitu" && rm -f "$BIN/xitu"
    rm -f "$XITU_CONF"
    if [ -f "$HYPR_CONF" ] && grep -q 'xitu.conf' "$HYPR_CONF"; then
        sed -i -e '/xitu\.conf/d' -e '/^# 系图$/d' "$HYPR_CONF"
        say "已从 hyprland.conf 去掉 source xitu.conf 那一行。"
    fi
    say "系图已卸载。图库（图片/系图）、设置和日志（$DATA_HOME/Xitu）都还在，不要了可以自己删。"
    exit 0
fi

if [ $mode = check ]; then
    check_deps
    exit $?
fi

# ---- 安装 ----
if [ ! -f "$SRC/linux/shell.qml" ] || [ ! -d "$SRC/shared/themes" ]; then
    say "找不到 linux/shell.qml 或 shared/themes。请在仓库或发布包解压出来的目录里运行：sh linux/install.sh" >&2
    exit 1
fi

deps_ok=1
check_deps || deps_ok=0
say ""

# 程序文件：linux/ 和 shared/ 保持仓库里的相对位置（助手按 ../shared/themes 找内置主题）
tmp="$APP.new"
rm -rf "$tmp"
mkdir -p "$tmp/linux" "$tmp/shared"
for f in "$SRC"/linux/*.qml "$SRC"/linux/*.js "$SRC"/linux/xitu_helper.py; do
    cp "$f" "$tmp/linux/"
done
cp -R "$SRC/shared/themes" "$tmp/shared/"
[ -f "$SRC/VERSION" ] && cp "$SRC/VERSION" "$tmp/"
rm -rf "$APP"
mv "$tmp" "$APP"
say "程序装到了 $APP"

# 命令 xitu：不带参数启动（已经在跑就放下绳子），带参数转成 qs ipc call xitu ...
mkdir -p "$BIN"
cat > "$BIN/xitu" <<EOF
#!/bin/sh
# 系图启动器（install.sh 生成）。
#   xitu start   启动（Hyprland 开机自启用这个）
#   xitu         已经在跑就放下 / 收起绳子，没在跑就启动
#   xitu capture | toggle | gallery | settings | clear | quit ...   发给正在跑的系图（qs ipc call xitu ...）
APP="$APP/linux"
if [ "\${1:-}" = start ]; then exec qs -p "\$APP"; fi
if [ \$# -eq 0 ]; then
    if qs ipc -p "\$APP" call xitu toggle >/dev/null 2>&1; then exit 0; fi
    exec qs -p "\$APP"
fi
exec qs ipc -p "\$APP" call xitu "\$@"
EOF
chmod +x "$BIN/xitu"
say "命令装到了 $BIN/xitu"
case ":$PATH:" in *":$BIN:"*) ;; *) say "（$BIN 不在 PATH 里；Hyprland 配置里用的是完整路径，不受影响）" ;; esac

# Hyprland：单独一个文件，hyprland.conf 里 source 它，卸载时好删
mkdir -p "$HYPR_DIR"
cat > "$XITU_CONF" <<EOF
# 系图（install.sh 生成，重新安装会覆盖；要改快捷键就改这里或在 hyprland.conf 里另绑）
exec-once = $BIN/xitu start
bind = CTRL SHIFT, A, exec, $BIN/xitu capture
EOF
say "Hyprland 配置写到了 $XITU_CONF（开机自启，Ctrl+Shift+A 截图）"

if [ -f "$HYPR_CONF" ] && grep -q 'xitu.conf' "$HYPR_CONF"; then
    say "hyprland.conf 里已经 source 了 xitu.conf。"
elif [ -f "$HYPR_CONF" ]; then
    answer=n
    if [ $yes = 1 ]; then
        answer=y
    elif [ -t 0 ]; then
        printf '要在 %s 末尾加一行 source = %s 吗？[Y/n] ' "$HYPR_CONF" "$XITU_CONF"
        read -r answer || answer=n
        [ -z "$answer" ] && answer=y
    fi
    case "$answer" in
        y|Y|yes)
            printf '\n# 系图\nsource = %s\n' "$XITU_CONF" >> "$HYPR_CONF"
            say "已加上。" ;;
        *)
            say "没改 hyprland.conf。要自启和快捷键，就自己在里面加一行：source = $XITU_CONF" ;;
    esac
else
    say "没找到 $HYPR_CONF。用 Hyprland 的话，在你的配置里加一行：source = $XITU_CONF"
fi

# 在 Hyprland 里装的就顺手启动（Hyprland 重新加载配置时不会再跑 exec-once）
if [ $deps_ok = 1 ] && [ -n "${HYPRLAND_INSTANCE_SIGNATURE:-}" ] && have qs; then
    if qs ipc -p "$APP/linux" call xitu themes >/dev/null 2>&1; then
        say "系图已经在运行；要用新装的版本，先 xitu quit 再 xitu。"
    else
        (setsid qs -p "$APP/linux" >/dev/null 2>&1 &) 2>/dev/null || (nohup qs -p "$APP/linux" >/dev/null 2>&1 &)
        say "系图已启动。指针停在屏幕顶边中间，绳子会放下来。"
    fi
elif [ $deps_ok = 0 ]; then
    say ""
    say "先把上面缺的必需包装上，再运行 xitu（或重新登录 Hyprland）。"
else
    say "运行 xitu 启动，或重新登录 Hyprland。"
fi
