# 路线图

目标：先让两边一致，再第一次正式发布给别人用，之后再扩 Linux 桌面。Windows 和 Linux 两版一直保持功能对齐，不让任何一边单独往前走。

## v0.1 两边一致

已完成：

- 默认外观统一为蓝绳 + 拍立得，Windows 支持切换主题，主题 JSON 两边共用（`shared/themes/`）
- 发行版两边都只有绳子（Linux 的抽屉只留作开发开关 `XITU_UI=drawer`）
- 剪贴板规则、自动挂上设置（`Collect`）、过期清理、清空绳子、绳子容量两边一致
- Linux 设置窗口：图库位置（可搬图）、保存时间、自动挂上、主题
- Windows 单元测试（xunit）和主题预览图在 CI 上跑
- Linux 编辑：长按或右键「编辑」，`$XITU_EDITOR` → satty / swappy → xdg-open，存盘后卡片刷新
- Linux 自带截图：`ipc capture`（Hyprland 绑 Ctrl+Shift+A），slurp 框选 + grim；没装就提示改用系统截图键；截完从框选处飞上绳子，同 Windows
- 修顶部误触发：只有顶边中间挂照片的那一段感应，两角和标签页、标题栏不再触发（Windows 窄带从 20 降到 4 DIP）
- 卡片右键「另存为…」，两边都有
- 共用测试用例：`shared/tests/*.json`（剪贴板规则、绳子容量、顶边感应段、保存时间文字），xunit 和 pytest 都跑；CI 上 Linux 也跑 pytest

v0.1 的项目都做完了。

## v0.2 第一次正式发布

已完成：

- 正式下载包：推 `v*` 标签就由 `release.yml` 打包、冒烟测试并发 GitHub Release
- Windows 单文件自带 .NET 运行时，不用另装
- Linux 安装脚本 `linux/install.sh`：检查依赖并按发行版给出安装命令，装 `xitu` 命令，写 Hyprland 自启和 Ctrl+Shift+A；运行时缺必需的包会弹通知
- 小问题：界面字体按已装的挑（不再写死 HarmonyOS Sans SC）；截图文件夹认 `XDG_SCREENSHOTS_DIR` 和「图片」根目录（grim / grimblast / hyprshot 的默认位置），不再替用户建空文件夹；musl 上找得到 libc；图库空白页提示的截图键改成 Ctrl+Shift+A
- README 两个平台的下载与安装步骤

剩下：打 `v0.2.0` 标签发第一个版本。

## v0.3 Linux 跨桌面

已完成：

- 完整支持 Hyprland、Sway、niri，KDE 部分支持；GNOME 和 X11 不支持
- 聚焦屏和全屏判断抽到 `linux/Desktop.qml`：Hyprland / Sway（I3）/ wlr-foreign-toplevel / 鼠标最近停过的屏
- 自带截图点窗口：Hyprland 和 Sway 都有预设窗口框
- 安装脚本按桌面写自启和 Ctrl+Shift+A：Hyprland、Sway 自动接进配置，niri 打印要加的两行，KDE 写 XDG 自启并说明快捷键

CI 上验证过的（`.github/workflows/build.yml`）：

- Sway（无头）：整套界面测试，加上「全屏时不放下绳子」「两块屏时在聚焦的那块放下」
- niri（嵌在无头 Sway 里）：启动、放下绳子、全屏时不放下、图库
- KDE 的 KWin（`kwin_wayland --virtual`，两块虚拟屏）：启动、放下绳子、图库，没有 QML 报错；全屏时会放下（已知限制）
- 安装脚本：Hyprland、Sway、niri、KDE 的配置写入与卸载（Release 冒烟测试）

未验证（CI 做不到）：

- Hyprland 上的跨桌面改动：CI 没有 Hyprland，逻辑沿用 v0.2 的 Hyprland 代码，只是挪进了 `DesktopHyprland.qml`
- niri 多屏时的聚焦屏判断（嵌套运行只有一块屏）
- KDE 上的剪贴板监听、拖放和自带截图（KWin 不支持 wlr-screencopy，截不了图）
- 真机上的开机自启和 Ctrl+Shift+A 绑定

## 以后再看

- 截图备注（v0.3 之后）

不做：悬停放大预览。
