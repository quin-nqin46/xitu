# 系图主题

一根绳子的外观（颜色、线宽、绳端、夹子、相框、空绳提示）放在一个 JSON 文件里。Windows 和 Linux 都读这个目录（Windows 打包进程序，见 `windows/Clothesline.csproj`）。几何和物理不在这里：照片间距、卡片宽度、照片最大高度、绳子下垂、弹簧，在 `linux/layout.js` 和 `windows/Layout.cs`。

Windows 上自己的主题放 `%LOCALAPPDATA%\Xitu\themes\<id>.json`，在托盘菜单「换主题」里切换。

内置主题在这个目录，文件名就是 id（`polaroid-blue.json`）。自己的主题放到 `~/.local/share/Xitu/themes/<id>.json`，id 相同的话用你的那份。id 只用字母、数字、`-` 和 `_`。

当前用哪一套写在 `~/.local/share/Xitu/settings.json` 的 `Theme` 字段，和 `LibraryPath` 在同一个文件里。切换时只改这个字段，别的字段会留着。

```bash
qs ipc -p <系图 linux 目录> call xitu themes
qs ipc -p <系图 linux 目录> call xitu setTheme linen
qs ipc -p <系图 linux 目录> call xitu nextTheme
```

`themes` 每行是 `id 显示名`。切换立刻生效，不用重启。

缺了的字段会用默认主题「主题蓝绳 + 拍立得」的值补上。整个文件不是合法 JSON 时，退回默认主题，并在 `~/.local/share/Xitu/log.txt` 记一笔。

## 字段

- `name`：显示名。
- `accent`：跟系统主题色失败时用的备用色，默认 `#3D6FE0`。下面凡是写 `"accent"` 的颜色都会先去读一份外部 qml 里的 `activeWorkspaceBackground` 当强调色，明暗看另一份 env 文件里的 `theme=`：环境变量 `XITU_ACCENT_QML` 指向含 `activeWorkspaceBackground` 的 qml 文件，`XITU_ACCENT_STATE` 指向含 `theme=light|dark` 行的 env 文件。两个变量都没设（或文件读不到）时直接用这里的值。
- `"accent-strong"`：同 `"accent"` 的色相，再把颜色收实。饱和度低于 0.65 就提到 0.65，HSL 亮度高于 0.52 就压到 0.52，原本更暗或更饱和就保持。浅色工作区高亮 `#8EB6EC` 会变成接近 `#3D6FE0` 的蓝（实测约 `#2D78DC`）。暗色主题同样走这一步。写在绳子、圆点、夹子这类要在白底上看清的地方。
- `rope.color`：绳子颜色。可以是 `#RRGGBB`、`rgba(r,g,b,a)`、`"accent"` 或 `"accent-strong"`。
- `rope.width`：线宽。
- `rope.shadowColor` / `shadowWidth` / `shadowOffset`：绳子底下那条阴影的颜色、线宽、向下偏多少。
- `rope.texture`：`none` 或 `twist`。`twist` 是在绳子上再叠一条白色半透明虚线，3 像素一段、空 5 像素，当作麻绳纹。
- `ends.style`：`pin` 圆钉，`knot` 菱形结加流苏，`none` 什么都不画。
- `ends.inset`：端点中心离屏幕左右边的距离。`pin` 时绳子只从左钉画到右钉；`knot` 和 `none` 时绳子仍然铺满整宽。
- `ends.pinColor` / `pinRing` / `dotColor`：圆钉的填充、1 像素描边、中心点。`dotColor` 可以是 `"accent"` 或 `"accent-strong"`。
- `ends.radius`：`pin` 时是圆钉半径；`knot` 时是菱形结的边长（红绳是 14）。
- `ends.dotRadius`：圆钉中心点的半径。`knot` 不用。
- `clip.color`：夹子颜色，可以是 `"accent"` 或 `"accent-strong"`。图库里的强调色（选中框、星星、设置链接、删除）也用解析后的这个颜色。
- `clip.width` / `height` / `radius`：夹子宽、高、圆角。
- `clip.above` / `below`：夹子顶端在绳子上方多少、底端在绳子下方多少。两者相加应等于 `height`。
- `clip.highlight`：夹子上的高光颜色。空字符串表示不画。
- `clip.highlightWidth` / `highlightHeight` / `highlightRadius` / `highlightShift`：高光条的宽、高、圆角，以及相对夹子中线的水平偏移（负数往左）。缺省是 2、16、1、`-2.5`（拍立得那条偏左的高光）。麻绳的中线是宽 1、高 22、偏移 0。
- `clip.border`：夹子描边。空字符串表示不描。有颜色时描 1 像素。
- `clip.shadow`：要不要在夹子下加一小块阴影（模糊约 5、下移 2、不透明度 0.25）。默认 `false`。
- `frame.paper`：相框纸色。
- `frame.padLeft` / `padTop` / `padRight` / `padBottom`：照片到相框外沿的边距。
- `frame.radius`：相框外圆角。
- `frame.photoRadius`：照片圆角。
- `hint.bg` / `fg` / `border`：绳子上没有照片时「截图会挂在这里」胶囊的底色、字色、描边。描边空字符串表示不描，有颜色时描 1 像素。
- `gallery.bg`：图库浮动面板的底色。缺了就用 `#F6F1E7`（原来整窗的底色）。面板圆角、描边和投影不在主题里。

颜色字符串用 `#RRGGBB`、`#AARRGGBB`、`rgba(r,g,b,a)`、`"accent"` 或 `"accent-strong"`。`a` 是 0 到 1。

## 最小示例

只改绳子颜色，其余都跟默认主题走：

```json
{
  "name": "我的蓝",
  "rope": { "color": "#3D6FE0" }
}
```

存成 `~/.local/share/Xitu/themes/my-blue.json`，然后 `setTheme my-blue`。
