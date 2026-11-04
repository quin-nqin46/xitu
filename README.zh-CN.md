# 系图

系图（拼音 Xitu）是一个截图晾衣绳。Windows 版把每张截图挂到每块屏幕顶边的一条绳子上；Linux 版跑在 Hyprland + Quickshell 上，默认右上角抽屉，可切换成顶边绳子。从 `alejandrobujan/tendedero`（MIT，见 `NOTICE`）移植；程序名和图标不在该许可证范围内，本程序不叫 Tendedero。

## 是什么

系图把截图挂到一根绳子上，像晾衣绳挂照片。截图文件夹和剪贴板里新出现的截图会自动挂上，在绳子上可以直接复制、打开或取下。

下面的演示图由程序用占位图渲染（非真实截图）：

![抽屉展开](docs/demo-drawer.png)

![抽屉拖入悬停](docs/demo-drawer-drop.png)

![绳子模式](docs/demo-rope.png)

| 路径 | 内容 |
|---|---|
| `windows/` | Windows 源码（`Clothesline.csproj`，WPF；`Assets/`、`Capture/`、`tools/make-icon/`） |
| `linux/` | Linux 源码（Quickshell 壳：QML、`xitu_helper.py`、主题 JSON） |
| `docs/` | 演示图 |
| `LICENSE`、`NOTICE` | 许可与致谢 |

## 功能

两边共有：

- 截图文件夹和剪贴板里出现的截图自动挂上。
- 单击复制，双击打开；悬停点叉取下，文件留在图库。
- 取下时照片落下；绳子模式里照片轻摆，偶尔有微风。
- 图库窗口保存挂过的所有照片。

Windows：

- 托盘菜单，每屏一条绳，从顶边滑出。
- 默认全局热键 Ctrl+Shift+A：框选、点窗口或按 Enter 整屏，照片飞上绳。
- 长按用画图编辑。把照片拖出绳子只复制，照片留在绳上。

Linux（Hyprland + Quickshell）：

- 两种界面模式：`drawer`（右上角抽屉，默认）和 `rope`（顶边）。环境变量 `XITU_UI=drawer|rope` 优先于 `settings.json` 的 `ui` 字段。
- 没有托盘菜单；展开、图库、退出用 `qs ipc`（见下）或由顶栏、按键绑定触发。
- 拖动卡片换位置。绳子和卡片的颜色（主题）可通过 IPC 切换，不用重启。

## Windows 构建与安装

需要 .NET 8 SDK。

```
dotnet build windows/
```

在非 Windows 机器上编译要加 `-p:EnableWindowsTargeting=true`：

```
dotnet build windows/ -p:EnableWindowsTargeting=true
```

发布示例（单文件，用已装的 .NET 运行时）：

```
dotnet publish windows/ -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

重新生成图标：

```
dotnet run --project windows/tools/make-icon -- windows/Assets
```

运行时文件（出处：`windows/AppPaths.cs`）：

- 日志：`%LOCALAPPDATA%\Xitu\log.txt`
- 绳上条目：`%LOCALAPPDATA%\Xitu\state.json`
- 快捷键与图库设置：`%LOCALAPPDATA%\Xitu\settings.json`
- 剪贴板暂存：`%LOCALAPPDATA%\Xitu\Inbox\`
- 图库：`%USERPROFILE%\Pictures\系图\`（默认；设置里可改）

## Linux 依赖与运行

依赖（只列代码里实际调用的）：

- Quickshell（`qs`）：壳的运行时。仓库未钉版本。
- Hyprland：每屏窗口与聚焦屏判断走 Quickshell 的 Hyprland 集成。
- Python 3 + Pillow：文件与剪贴板助手（`linux/xitu_helper.py`）。
- wl-clipboard：`wl-paste`（剪贴板监听）和 `wl-copy`（单击复制）。
- inotify-tools：`inotifywait`（截图文件夹监听）。
- xdg-utils 和 gio：`xdg-open`、`xdg-user-dir PICTURES`、`gio trash`（图库删除进回收站）。

Linux 版自己不截图，靠截图文件夹和剪贴板监听收图。

在仓库根目录运行：

```
qs -p linux
```

按键和菜单入口走 IPC：

```
qs ipc -p linux call xitu toggle
```

IPC target `xitu` 上的函数：`toggle`、`peek`、`hang <路径>`、`openLibrary`、`gallery`、`closeGallery`、`quit`、`setTheme <id>`、`themes`、`nextTheme`。

## 可选：与 Caelestia 整合

系图用 `qs -p linux` 就能独立运行，不依赖 Caelestia。

- 抽屉配色：`drawer` 模式下，抽屉实时读取 Caelestia 的配色文件 `${XDG_STATE_HOME:-~/.local/state}/caelestia/scheme.json`；没有这个文件时用内置的一套深色。（`linux/ThemeEngine.qml`）
- 绳子主题：`linux/themes/caelestia.json` 是按 Caelestia 配色做的一套绳子主题，用 `qs ipc -p linux call xitu setTheme caelestia` 切换。
- 嵌入：后台逻辑单独放在 `linux/XituCore.qml`，与窗口分开，别的 Quickshell 外壳（比如 Caelestia 的配置）可以自己承载抽屉。这样用时，只有 `rope` 模式还需要本进程。

绳子主题里颜色写成 `accent` 的部分，可以跟随桌面外壳的强调色：用两个环境变量指向外壳提供的文件。任一个没设或读不到，就用主题自带的强调色（默认 `#3D6FE0`）。

- `XITU_ACCENT_QML`：含 `activeWorkspaceBackground` 颜色的 QML 文件
- `XITU_ACCENT_STATE`：含 `theme=light|dark` 一行的 env 文件

## 配置与数据文件

Windows 路径见上文「Windows 构建与安装」。Linux 路径，每条注明定义它的源文件：

| 路径 | 用途 | 出处 |
|---|---|---|
| `${XDG_DATA_HOME:-~/.local/share}/Xitu/state.json` | 绳上条目（与 Windows 版同格式） | `linux/xitu_helper.py` |
| `${XDG_DATA_HOME:-~/.local/share}/Xitu/settings.json` | 设置（`ui`、`Theme`、`LibraryPath` 等） | `linux/xitu_helper.py`；`linux/shell.qml` 从同一个文件读 `ui` 字段 |
| `${XDG_DATA_HOME:-~/.local/share}/Xitu/log.txt` | 助手日志 | `linux/xitu_helper.py` |
| `${XDG_DATA_HOME:-~/.local/share}/Xitu/themes/<id>.json` | 自定义主题（id 相同则覆盖内置） | `linux/themes/README.md` |
| 图库根目录 | `xdg-user-dir PICTURES`（取不到时用 `~/Pictures`）下的 `系图/`，可用 `LibraryPath` 改 | `linux/xitu_helper.py` |
| 截图文件夹 | 监听新截图：设了 `$XITU_SCREENSHOT_DIR` 就用它，否则是「图片」目录下的 `Screenshots/`（已有 `屏幕截图/` 时用它） | `linux/xitu_helper.py`（`screenshot-dir`） |

## 致谢与许可

- 系图从 Alejandro Buján 的 tendedero（MIT）移植，原仓库 `alejandrobujan/tendedero`，许可证全文附在 `NOTICE` 末尾。
- 本仓库按 MIT 许可发布（`LICENSE`，版权行 `Copyright (c) 2026 quin-nqin46`），另见 `NOTICE`。
- 「系图」这个名字和图标不在原许可证范围内，本程序不叫 Tendedero。
