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
  <img src="https://img.shields.io/badge/Linux-Hyprland%20%2B%20Quickshell-c8a57a" alt="Linux">
</p>

<p align="center"><b>English</b> · <a href="README.zh-CN.md">简体中文</a></p>

<p align="center">
  <img src="docs/demo-rope.gif" alt="Screenshots flying onto the rope, then switching rope themes">
</p>

## What it is

Screenshots usually vanish into a folder. Xitu keeps the last few in sight: anything that lands in your screenshot folder or clipboard drops onto a rope along the top edge of the screen, sways for a moment, and stays there until you take it down. Click to copy, double-click to open, drag a photo straight into a chat window.

On Windows it is a tray app with one rope per screen. On Linux it runs on Hyprland with Quickshell, as a top-right drawer by default or as the same rope.

<p align="center">
  <img src="docs/demo-drawer.gif" width="640" alt="Linux drawer mode: a new screenshot takes the big slot">
</p>

Xitu started as a port of [Tendedero](https://github.com/alejandrobujan/tendedero) for macOS (MIT, see `NOTICE`). It has its own name and icon and is not Tendedero.

| Path | Contents |
|---|---|
| `windows/` | Windows source (`Clothesline.csproj`, WPF; `Assets/`, `Capture/`, `tools/make-icon/`) |
| `linux/` | Linux source (Quickshell shell: QML, `xitu_helper.py`, theme JSON) |
| `docs/` | demo images |
| `LICENSE`, `NOTICE` | licensing |

## Features

Both platforms:

- Screenshots that appear in your screenshot folder or clipboard hang up on their own. Images copied in a browser ("Copy image") hang up too; turn that off with `HangCopiedImages: false` in `settings.json` (on Windows: Settings › General). Bitmaps copied together with table cells, text or files are ignored.
- Single click copies a photo, double click opens it. Hover and click the X to take it down; the file stays in the gallery.
- A taken-down photo falls off; in rope mode photos sway gently and every so often a breeze passes.
- A gallery window keeps everything that has been hung up.

Windows:

- Tray menu, one rope per screen, slides out from the top edge.
- Global hotkey Ctrl+Shift+A by default: box-select, click a window, or press Enter for the whole screen; the photo flies onto the rope.
- Press and hold to edit in Paint. Dragging a photo off the line only copies it; the photo stays on the rope.

Linux (Hyprland + Quickshell):

- Two UI modes: `drawer` (top-right drawer, default) and `rope` (top edge). The environment variable `XITU_UI=drawer|rope` takes priority over the `ui` field in `settings.json`.
- No tray menu; expand, gallery and quit are triggered with `qs ipc` (see below) or from your bar or key bindings.
- Drag cards to reorder. Rope and card colors (themes) can be switched over IPC without restarting.
- Drag an image from a browser or file manager onto the top edge: the drawer opens and takes it in.

<p align="center">
  <img src="docs/demo-drawer-drop.png" width="420" alt="Dragging an image over the drawer">
</p>

## Windows build and install

Requires the .NET 8 SDK.

```
dotnet build windows/
```

Building on a non-Windows machine needs `-p:EnableWindowsTargeting=true`:

```
dotnet build windows/ -p:EnableWindowsTargeting=true
```

Publish example (single file, uses the installed .NET runtime):

```
dotnet publish windows/ -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

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

## Linux dependencies and running

Dependencies — only what the code actually calls:

- Quickshell (`qs`): shell runtime. The repository does not pin a version.
- Hyprland: per-screen windows and focus tracking go through Quickshell's Hyprland integration.
- Python 3 with Pillow: file and clipboard helper (`linux/xitu_helper.py`).
- wl-clipboard: `wl-paste` (clipboard watch) and `wl-copy` (single-click copy).
- inotify-tools: `inotifywait` (screenshot folder watch).
- xdg-utils and gio: `xdg-open`, `xdg-user-dir PICTURES`, and `gio trash` (gallery deletion goes to the trash).

The Linux version does not capture screenshots itself; it picks them up from the screenshot folder and the clipboard.

Tests for the helper script (need pytest and Pillow; no Wayland or desktop required):

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

Functions on IPC target `xitu`: `toggle`, `peek`, `clear`, `hang <path>`, `openLibrary`, `gallery`, `closeGallery`, `quit`, `setTheme <id>`, `themes`, `nextTheme`.

## Caelestia integration (optional)

Xitu runs on its own with `qs -p linux`; Caelestia is not required.

- Drawer colors: in `drawer` mode the drawer reads Caelestia's color scheme live from `${XDG_STATE_HOME:-~/.local/state}/caelestia/scheme.json`. Without that file it uses a built-in dark scheme. (`linux/ThemeEngine.qml`)
- Rope theme: `linux/themes/caelestia.json` is a rope theme in Caelestia's palette; switch to it with `qs ipc -p linux call xitu setTheme caelestia`.
- Embedding: the background logic lives in `linux/XituCore.qml`, separate from the windows, so another Quickshell shell (for example a Caelestia config) can host the drawer itself. In that setup only `rope` mode still needs this process.

Optional accent for rope themes whose color is `accent`: point two environment variables at files provided by your desktop shell. If either is unset or unreadable, the theme's own accent is used (default `#3D6FE0`).

- `XITU_ACCENT_QML` — a QML file containing an `activeWorkspaceBackground` color
- `XITU_ACCENT_STATE` — an env file containing a `theme=light|dark` line

## Config and data files

Windows paths are listed under "Windows build and install". Linux paths, with the source file that defines each:

| Path | Purpose | Defined in |
|---|---|---|
| `${XDG_DATA_HOME:-~/.local/share}/Xitu/state.json` | what is on the rope (same format as Windows) | `linux/xitu_helper.py` |
| `${XDG_DATA_HOME:-~/.local/share}/Xitu/settings.json` | settings (`ui`, `Theme`, `LibraryPath`, `RetentionDays`, `HangCopiedImages`, …) | `linux/xitu_helper.py`; `linux/shell.qml` reads the `ui` field from the same file |
| `${XDG_DATA_HOME:-~/.local/share}/Xitu/log.txt` | helper log | `linux/xitu_helper.py` |
| `${XDG_DATA_HOME:-~/.local/share}/Xitu/themes/<id>.json` | custom themes (same id overrides the built-in) | `linux/themes/README.md` |
| gallery root | `系图/` under `xdg-user-dir PICTURES` (falls back to `~/Pictures`), overridable via `LibraryPath` | `linux/xitu_helper.py` |
| screenshot folder | watched for new screenshots: `$XITU_SCREENSHOT_DIR` if set, otherwise `Screenshots/` (or an existing `屏幕截图/`) under the Pictures folder | `linux/xitu_helper.py` (`screenshot-dir`) |

## Acknowledgments and license

- Xitu is a port of `alejandrobujan/tendedero` by Alejandro Buján, MIT licensed; its license is reproduced in `NOTICE`.
- This repository is MIT licensed (`LICENSE`, copyright line `Copyright (c) 2026 quin-nqin46`); see also `NOTICE`.
- The name Xitu and the icon are not part of the original license, and this program is not called Tendedero.
