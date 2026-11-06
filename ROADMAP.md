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
- Linux 自带截图：`ipc capture`（Hyprland 绑 Ctrl+Shift+A），slurp 框选 + grim；没装就提示改用系统截图键
- 修顶部误触发：只有顶边中间挂照片的那一段感应，两角和标签页、标题栏不再触发（Windows 窄带从 20 降到 4 DIP）
- 卡片右键「另存为…」，两边都有
- 共用测试用例：`shared/tests/*.json`（剪贴板规则、绳子容量、顶边感应段、保存时间文字），xunit 和 pytest 都跑；CI 上 Linux 也跑 pytest

v0.1 的项目都做完了。

## v0.2 第一次正式发布

- 正式下载包（GitHub Releases）
- Windows 不用另装 .NET
- Linux 安装脚本，缺依赖时提醒
- 修人人会踩的小问题：字体写死、截图文件夹猜错、musl 系统上的兼容

## v0.3 Linux 跨桌面

- 完整支持 Hyprland、Sway、niri，KDE 部分支持；GNOME 和 X11 不支持
- 需要在这些桌面上实测，所以放在发布之后

## 以后再看

- 截图备注（v0.3 之后）

不做：悬停放大预览。
