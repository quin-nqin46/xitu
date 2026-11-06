<p align="center">
  <img src="windows/Assets/xitu-256.png" width="96" alt="Xitu icon">
</p>

<h1 align="center">Xitu 系图</h1>

<p align="center">
  A clothesline for your screenshots — new captures hang on a rope at the top of the screen,<br>
  ready to copy, open, or drag into a chat.
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-b5302a" alt="MIT license"></a>
  <img src="https://img.shields.io/badge/Windows-.NET%208%20%2F%20WPF-2e3440" alt="Windows">
  <img src="https://img.shields.io/badge/Linux-Wayland%20%2B%20Quickshell-c8a57a" alt="Linux">
</p>

<p align="center"><b>English</b> · <a href="README.zh-CN.md">简体中文</a></p>

<p align="center">
  <img src="docs/demo-rope.gif" alt="Screenshots flying onto the rope, then switching rope themes">
</p>

## What it is

Screenshots usually vanish into a folder. Xitu keeps the last few in sight: anything that lands in your screenshot folder or clipboard drops onto a rope along the top edge of the screen, sways for a moment, and stays there until you take it down. Click to copy, double-click to open, drag a photo straight into a chat window.

On Windows it is a tray app with one rope per screen. On Linux it runs on Wayland desktops (Hyprland, Sway, niri, KDE Plasma) with Quickshell, with the same rope along the top edge.


Xitu started as a port of [Tendedero](https://github.com/alejandrobujan/tendedero) for macOS (MIT, see `NOTICE`). It has its own name and icon and is not Tendedero.

| Path | Contents |
|---|---|
| `windows/` | Windows source (`Clothesline.csproj`, WPF; `Assets/`, `Capture/`, `tools/make-icon/`) |
| `linux/` | Linux source (Quickshell shell: QML, `xitu_helper.py`) |
| `shared/themes/` | rope themes (JSON), read by both Windows and Linux |
| `shared/tests/` | test cases (JSON) run by both the Windows xunit tests and the Linux pytest tests |
| `docs/` | demo images |
| `LICENSE`, `NOTICE` | licensing |

## Install

### Windows

1. Download `Xitu-<version>-win-x64.zip` from [Releases](https://github.com/quin-nqin46/xitu/releases/latest) and unzip it somewhere permanent (for example `D:\Apps\Xitu\`).
2. Double-click `Xitu.exe`. The .NET runtime is bundled; nothing else to install.
3. On first launch Windows SmartScreen may say "Windows protected your PC" (the app is not code-signed): click "More info" → "Run anyway".
4. Xitu lives in the tray. To start it with Windows: right-click the tray icon › 开机自启 (start at login).

Capture with Ctrl+Shift+A. If another app (WeChat, QQ, …) already owns that key, Xitu says so; pick another key in Settings › Hotkeys.

### Linux (Hyprland, Sway, niri, KDE Plasma)

1. Download `xitu-<version>-linux.tar.gz` from [Releases](https://github.com/quin-nqin46/xitu/releases/latest) and extract it, or `git clone` this repository.
2. From the extracted folder (or the repository root) run:

   ```
   sh linux/install.sh
   ```

   The script checks dependencies and prints the install command for your distro when something is missing (it never runs sudo itself); copies the app to `~/.local/share/Xitu/app` and a `xitu` command to `~/.local/bin/xitu`; then sets up start at login and Ctrl+Shift+A capture for your desktop: on Hyprland it writes `~/.config/hypr/xitu.conf` and asks before adding a `source` line to `hyprland.conf`; on Sway it writes `~/.config/sway/xitu.conf` and asks before adding an `include` line to `config`; on niri it prints the two lines to add to `config.kdl` (also saved as `~/.config/niri/xitu.kdl`); on KDE it writes `~/.config/autostart/xitu.desktop` and tells you where to add the shortcut. Run inside one of these desktops, it starts Xitu right away.
3. After installing any missing packages, run `xitu start` (or log in again).

Required packages: quickshell, python3 with Pillow, wl-clipboard, inotify-tools, xdg-utils, gio (glib2). Optional: grim and slurp (built-in capture), xdg-user-dirs, libnotify (notifications about missing packages), zenity or kdialog (Save As, folder picker), satty or swappy (editing). `sh linux/install.sh --check` only checks. At startup Xitu also shows a notification if a required package is missing.

Everyday commands: `xitu` (show or hide the rope), `xitu capture`, `xitu gallery`, `xitu settings`, `xitu quit`. Uninstall with `sh linux/install.sh --uninstall`; the gallery and settings are kept.

If something else is already bound to Ctrl+Shift+A, both will fire; edit the binding line in `xitu.conf`.

Desktop support:

| Desktop | Support | Notes |
|---|---|---|
| Hyprland | full | |
| Sway | full | clicking a window while capturing grabs that window, as on Hyprland |
| niri, river and other wlroots compositors | full | focus and fullscreen tracking via wlr-foreign-toplevel; capture is drag or whole screen only |
| KDE Plasma (Wayland) | partial | the shortcut drops the rope on the screen the pointer last touched; touching the top edge over a fullscreen video still drops it |
| GNOME, any X11 session | not supported | no layer-shell or clipboard watching |

## Features

Both platforms:

- Screenshots that appear in your screenshot folder or clipboard hang up on their own. Images copied in a browser ("Copy image") hang up too. Bitmaps copied together with table cells, text or files are ignored.
- What hangs up on its own is set by `Collect` in `settings.json` (Settings › 自动挂上 on both; Linux opens Settings from the gallery's 设置… link, the rope's blank-area menu, or `qs ipc … call xitu settings`): `all` (default: screenshots and copied images), `screenshots` (no copied images), `own` (only screenshots Xitu took itself; the screenshot folder and clipboard are not watched). Dragged-in images always hang up.
- Single click copies a photo, double click opens it. Hover and click the X to take it down; the file stays in the gallery.
- A taken-down photo falls off; in rope mode photos sway gently and every so often a breeze passes.
- A gallery window keeps everything that has been hung up.

Windows:

- Tray menu, one rope per screen, slides out from the top edge.
- Global hotkey Ctrl+Shift+A by default: box-select, click a window, or press Enter for the whole screen; the photo flies onto the rope.
- Rope themes: tray menu › 换主题. Same theme files as Linux (`shared/themes/`); the default is the blue rope with polaroid frames, the old red rope is still there as 红绳. Custom themes go in `%LOCALAPPDATA%\Xitu\themes\<id>.json`.
- Press and hold to edit in Paint. Dragging a photo off the line only copies it; the photo stays on the rope.

Linux (Wayland + Quickshell):

- Settings window (library location with optional move, retention and 立即清理过期, 自动挂上, theme) matches the Windows one. The folder picker uses `zenity` or `kdialog` if installed; otherwise type the path.
- No tray menu; expand, gallery and quit are triggered with `qs ipc` (see below) or from your bar or key bindings.
- Press and hold a card (or right-click › 编辑) to edit: `$XITU_EDITOR` if set (path appended, or use `{}` as placeholder), else satty or swappy (saving back to the image), else `xdg-open`. The card refreshes after saving (needs `inotifywait`).
- Drag cards to reorder. Rope and card colors (themes) can be switched over IPC without restarting.
- Drag an image from a browser or file manager onto the middle of the top edge: the rope comes down and takes it in.
- Both platforms: right-click a card › 另存为… to save a copy elsewhere (the original stays in the gallery). On Linux the save dialog needs `zenity` or `kdialog`.
- Both platforms: the rope comes down only when the pointer rests for 0.3 s on the middle of the top edge, the stretch where the photos hang (at least 320 px wide). The corners and the rest of the top edge (tabs, title bars, workspace buttons) no longer trigger it.

## Building Windows from source

Requires the .NET 8 SDK.

```
dotnet build windows/
```

Building on a non-Windows machine needs `-p:EnableWindowsTargeting=true`:

```
dotnet build windows/ -p:EnableWindowsTargeting=true
```

Publish (single file with the .NET runtime bundled, same as the release download):

```
dotnet publish windows/ -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

To cut a release, push a `v*` tag (`git tag v0.2.0 && git push origin v0.2.0`). `.github/workflows/release.yml` builds the Windows zip and the Linux tarball, smoke-tests both, and publishes a GitHub Release.

Regenerate the icon:

```
dotnet run --project windows/tools/make-icon -- windows/Assets
```

Runtime files (source: `windows/AppPaths.cs`):

- Log: `%LOCALAPPDATA%\Xitu\log.txt`
- Rope state: `%LOCALAPPDATA%\Xitu\state.json`
- Hotkey and gallery settings: `%LOCALAPPDATA%\Xitu\settings.json`
- Clipboard staging: `%LOCALAPPDATA%\Xitu\Inbox\`
- Gallery: `%USERPROFILE%\Pictures\系图\` (default; changeable in settings)

## Running Linux from source

Dependencies — only what the code actually calls:

- Quickshell (`qs`): shell runtime. The repository does not pin a version.
- A Wayland compositor with layer-shell. Focus and fullscreen tracking live in `linux/Desktop.qml`: Quickshell's Hyprland integration on Hyprland, its I3 integration on Sway, wlr-foreign-toplevel elsewhere, and the screen the pointer last touched when none of those is available.
- Python 3 with Pillow: file and clipboard helper (`linux/xitu_helper.py`).
- wl-clipboard: `wl-paste` (clipboard watch) and `wl-copy` (single-click copy).
- inotify-tools: `inotifywait` (screenshot folder watch).
- xdg-utils and gio: `xdg-open`, `xdg-user-dir PICTURES`, and `gio trash` (gallery deletion goes to the trash).

The steps below run Xitu straight from the repository without the install script (for development).

Tests for the helper script (need pytest and Pillow, plus node for the shared `layout.js` cases; no Wayland or desktop required):

```
python3 -m pytest linux/tests
```

Run from the repository root:

```
qs -p linux
```

Keyboard shortcuts and menu entries talk over IPC:

```
qs ipc -p linux call xitu toggle
```

Built-in capture, like Windows Ctrl+Shift+A (needs `grim` and `slurp`; drag a region, click a window, or click a screen). Bind it in Hyprland (on Sway: `bindsym Ctrl+Shift+a exec …`):

```
bind = CTRL SHIFT, A, exec, qs ipc -p /path/to/xitu/linux call xitu capture
```

The shot flies from where you selected it onto the rope (as on Windows), goes into the gallery and, unless `CopyToClipboard` is `false`, is copied too. Without grim/slurp a notification says to use your system screenshot key instead (shots saved to the screenshot folder still hang up).

Functions on IPC target `xitu`: `toggle`, `peek`, `clear`, `hang <path>`, `openLibrary`, `gallery`, `closeGallery`, `settings`, `capture`, `quit`, `setTheme <id>`, `themes`, `nextTheme`.

## Caelestia integration (optional)

Xitu runs on its own with `qs -p linux`; Caelestia is not required.

- Rope theme: `shared/themes/caelestia.json` is a rope theme in Caelestia's palette; switch to it with `qs ipc -p linux call xitu setTheme caelestia`.
- Embedding: the background logic lives in `linux/XituCore.qml`, separate from the windows, so another Quickshell shell can host it.

Optional accent for rope themes whose color is `accent`: point two environment variables at files provided by your desktop shell. If either is unset or unreadable, the theme's own accent is used (default `#3D6FE0`).

- `XITU_ACCENT_QML` — a QML file containing an `activeWorkspaceBackground` color
- `XITU_ACCENT_STATE` — an env file containing a `theme=light|dark` line

## Config and data files

Windows paths are listed under "Building Windows from source". Linux paths, with the source file that defines each:

| Path | Purpose | Defined in |
|---|---|---|
| `${XDG_DATA_HOME:-~/.local/share}/Xitu/state.json` | what is on the rope (same format as Windows) | `linux/xitu_helper.py` |
| `${XDG_DATA_HOME:-~/.local/share}/Xitu/settings.json` | settings (`Theme`, `LibraryPath`, `RetentionDays`, `Collect`, …) | `linux/xitu_helper.py` |
| `${XDG_DATA_HOME:-~/.local/share}/Xitu/log.txt` | helper log | `linux/xitu_helper.py` |
| `${XDG_DATA_HOME:-~/.local/share}/Xitu/themes/<id>.json` | custom themes (same id overrides the built-in) | `shared/themes/README.md` |
| gallery root | `系图/` under `xdg-user-dir PICTURES` (falls back to `~/Pictures`), overridable via `LibraryPath` | `linux/xitu_helper.py` |
| screenshot folder | watched for new screenshots: only `$XITU_SCREENSHOT_DIR` if set; else `XDG_SCREENSHOTS_DIR`; else existing `Screenshots/` and `屏幕截图/` under Pictures plus the Pictures folder itself (where grim, grimblast and hyprshot save by default; there only files named like screenshots are taken). Only existing folders are watched; none are created | `linux/xitu_helper.py` (`screenshot-dirs`) |

## Acknowledgments and license

- Xitu is a port of `alejandrobujan/tendedero` by Alejandro Buján, MIT licensed; its license is reproduced in `NOTICE`.
- This repository is MIT licensed (`LICENSE`, copyright line `Copyright (c) 2026 quin-nqin46`); see also `NOTICE`.
- The name Xitu and the icon are not part of the original license, and this program is not called Tendedero.
