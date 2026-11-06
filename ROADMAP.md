# 路线图

目标：先让两边一致，再第一次正式发布给别人用，之后再扩 Linux 桌面。Windows 和 Linux 两版一直保持功能对齐，不让任何一边单独往前走。

## v0.1 两边一致

已完成：

- 默认外观统一为蓝绳 + 拍立得，Windows 支持切换主题，主题 JSON 两边共用（`shared/themes/`）
- 发行版两边都只有绳子（Linux 的抽屉只留作开发开关 `XITU_UI=drawer`）
- 剪贴板规则、自动挂上设置（`Collect`）、过期清理、清空绳子、绳子容量两边一致
- Linux 设置窗口：图库位置（可搬图）、保存时间、自动挂上、主题
- Windows 单元测试（xunit）和主题预览图在 CI 上跑

待做：

- Linux 编辑：`$XITU_EDITOR`，没有就用 satty / swappy，再没有就用 xdg-open；编辑完刷新卡片
- Linux 自带截图：grim + slurp，对应 Windows 的 Ctrl+Shift+A；没装就退回系统截图工具
- 修顶部误触发：只在画出来的绳子上停留才展开，不再是整条顶边
- 卡片右键「另存为」，两边一起做
- 共用测试用例：`shared/` 下放 JSON 用例，xunit 和 pytest 都读，改了一边忘了另一边，检查会直接报错

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
