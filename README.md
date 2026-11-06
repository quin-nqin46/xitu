## Linux 界面测试（无头 Sway + Quickshell）

| 步骤 | 结果 | 说明 | 截图 |
|---|---|---|---|
| 启动：空绳 + 提示 | ✅ | 主题列表：polaroid-blue 主题蓝绳 + 拍立得 / red-rope 红绳 / graphite 石墨细线 / linen 柔和麻绳 / caelestia Caelestia | 01-空绳提示.png |
| 顶边感应：只在中间一段 | ✅ | 感应段 x=800..1120 | 02-顶边悬停展开.png |
| ipc hang 挂 3 张 | ✅ | state.json 有 3 条 | 03-挂了三张.png |
| 截图文件夹：新文件自动挂上 | ✅ |  | 04-截图文件夹新图.png |
| 悬停照片（红叉、抬起） | ✅ |  | 05-悬停第一张.png |
| 单击复制 | ✅ | 剪贴板 image/png | 06-已复制提示.png |
| 长按编辑 → 保存后卡片刷新 | ✅ | 编辑器收到 /tmp/xitu-ui-pgjqxszd/home/Pictures/系图/2026-11/文件_20261106_084819.png | 07-编辑后的缩略图.png |
| 横拖换位置 | ✅ |  | 08-拖动中.png |
| 右键菜单 → 另存为… | ✅ | zenity 参数：--file-selection --save --confirm-overwrite --title=另存为 --filename=/tmp/xitu-ui- | 09-卡片右键菜单.png |
| 右键菜单 → 编辑 | ✅ |  |  |
| 自带截图：ipc capture + slurp 框选 + grim | ✅ | 截到 801x481 | 10-slurp 框选层.png, 11-截图飞向绳子.png, 12-截图挂上.png |
| 图库窗口 | ✅ |  | 13-图库.png |
| 设置窗口 | ✅ | 窗口：['系图设置'] | 14-设置.png |
| 每个主题出一张 | ✅ | 5 个主题 | 15-主题-polaroid-blue.png, 16-主题-red-rope.png, 17-主题-graphite.png, 18-主题-linen.png, 19-主题-caelestia.png |
| 取下一张（悬停红叉） | ✅ |  | 20-红叉.png |
| ipc clear 清空 | ✅ |  | 21-清空后.png |

日志里的 QML 警告 / 错误：0 行

## 截图

### 01-空绳提示.png

![01-空绳提示.png](<01-空绳提示.png>)

### 02-顶边悬停展开.png

![02-顶边悬停展开.png](<02-顶边悬停展开.png>)

### 03-挂了三张.png

![03-挂了三张.png](<03-挂了三张.png>)

### 04-截图文件夹新图.png

![04-截图文件夹新图.png](<04-截图文件夹新图.png>)

### 05-悬停第一张.png

![05-悬停第一张.png](<05-悬停第一张.png>)

### 06-已复制提示.png

![06-已复制提示.png](<06-已复制提示.png>)

### 07-编辑后的缩略图.png

![07-编辑后的缩略图.png](<07-编辑后的缩略图.png>)

### 08-拖动中.png

![08-拖动中.png](<08-拖动中.png>)

### 09-卡片右键菜单.png

![09-卡片右键菜单.png](<09-卡片右键菜单.png>)

### 10-slurp 框选层.png

![10-slurp 框选层.png](<10-slurp 框选层.png>)

### 11-截图飞向绳子.png

![11-截图飞向绳子.png](<11-截图飞向绳子.png>)

### 12-截图挂上.png

![12-截图挂上.png](<12-截图挂上.png>)

### 13-图库.png

![13-图库.png](<13-图库.png>)

### 14-设置.png

![14-设置.png](<14-设置.png>)

### 15-主题-polaroid-blue.png

![15-主题-polaroid-blue.png](<15-主题-polaroid-blue.png>)

### 16-主题-red-rope.png

![16-主题-red-rope.png](<16-主题-red-rope.png>)

### 17-主题-graphite.png

![17-主题-graphite.png](<17-主题-graphite.png>)

### 18-主题-linen.png

![18-主题-linen.png](<18-主题-linen.png>)

### 19-主题-caelestia.png

![19-主题-caelestia.png](<19-主题-caelestia.png>)

### 20-红叉.png

![20-红叉.png](<20-红叉.png>)

### 21-清空后.png

![21-清空后.png](<21-清空后.png>)

日志：[quickshell.log](quickshell.log) · [sway.log](sway.log)
