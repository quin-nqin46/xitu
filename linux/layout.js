.pragma library
// 尺寸、时间、物理。配色和夹子、相框的样式尺寸在 shared/themes/*.json。
// 下面的 Red / Wood 等常量还留着，绳子和夹子不再用它们。

var PanelHeight = 210;
var RopeTop = 10;
var Spacing = 174;
var CardWidth = 150;
var PinAbove = 9.5;
var PinHeight = 28;
var FrameRadius = 10;
var FrameInset = 5;
var PhotoMaxHeight = 104;
var PhotoRadius = 6;
var RevealMs = 350;
var HideMs = 220;

var MinItems = 3;
var MaxItemsCap = 12;

var RevealDelay = 300;
// 顶边感应只在中间挂照片的那一段（同 Windows TriggerZone.cs）：照片两头再各加 TriggerMargin，至少 2×TriggerMinHalf 宽
var TriggerMinHalf = 160;
var TriggerMargin = 14;
var RetractDelay = 500;
var PeekDuration = 2500;

// 抽屉界面（DrawerWindow / DrawerPanel）。顶边右上角悬停抽屉，样式跟随 Caelestia。
var DrawerW = 376;        // 12 + 大图 136 + 8 + 小格 3×64 + 2×8 + 12（大图占 2×2 格，和小格对齐）
var DrawerH = 120;        // 两行小格 2×44 + 8 + 上下各 12
var DrawerBigW = 136;     // 2 格宽
var DrawerBigH = 96;      // 2 格高
var DrawerTileW = 64;
var DrawerTileH = 44;
var DrawerGap = 8;
var DrawerPad = 12;
var DrawerCols = 3;
var DrawerRows = 2;
var DrawerConcave = 24;   // 和边框相接的内凹圆角
var DrawerCorner = 20;    // 左下角大圆角
var DrawerTopRight = 25;  // 右上角：和 Caelestia 边框内侧圆角一致（border.rounding 默认 25）
var BorderInset = 0;      // 抽屉窗口用 ExclusionMode.Normal，合成器已把它放到边框保留区内侧
var DrawerSensorW = 260;  // 顶边感应区：右边起 260 逻辑像素
var DrawerSensorH = 4;
var DrawerRevealDelay = 250;
var DrawerRetractDelay = 500;
var DrawerRevealMs = 350;
var DrawerHideMs = 220;
var DrawerPeekMs = 1500;  // 新图到达自动展开的时长
var DrawerTileCount = DrawerCols * DrawerRows;
var HoldToEdit = 500;
var CopiedHint = 1200;
var NudgeOut = 0.3;   // 秒
var ClickDelay = 300; // 单击延后复制，给双击让路

var ArriveDrop = 46;
var FallSeconds = 0.6;
var FallDistance = 180;
var HoverScale = 1.035;
var PressedScale = 0.95;
var LiftScale = 1.06;
var DragOpacity = 0.45;
var PositionResponse = 0.55;
var PositionDamping = 0.78;
var ArriveResponse = 0.42;
var ArriveDamping = 0.72;

var KnotInset = 90;
var KnotSize = 14;

var Red = "#B5302A";
var Wood = "#C8A57A";
var WoodStroke = "#94744C";
var Paper = "#F6F1E7";
var Ink = "#2E3440";
var Card = "#FFFFFF";
var RopeShadow = Qt.rgba(0, 0, 0, 0.35);

function sag(width) { return Math.min(30, width * 0.018); }

function ropeY(x, width) {
    if (width <= 0) return RopeTop;
    var f = x / width;
    return RopeTop + 4 * sag(width) * f * (1 - f);
}

function slotX(index, count, width) {
    var total = Math.max(count - 1, 0) * Spacing;
    return width / 2 - total / 2 + index * Spacing;
}

// 顶边感应段，返回 {left, right}，相对屏幕左边。以屏幕中线对称，同 slotX 的摆放
function triggerSpan(count, width) {
    if (width <= 0) return { left: 0, right: 0 };
    var half = Math.max(TriggerMinHalf, Math.max(count - 1, 0) * Spacing / 2 + CardWidth / 2 + TriggerMargin);
    return { left: Math.max(0, width / 2 - half), right: Math.min(width, width / 2 + half) };
}

// 返回 {w, h}
function photoSize(pixelWidth, pixelHeight) {
    var maxW = CardWidth - 14;
    var maxH = PhotoMaxHeight;
    if (!(pixelWidth > 0) || !(pixelHeight > 0)) return { w: maxW, h: maxH };
    var s = Math.min(maxW / pixelWidth, maxH / pixelHeight);
    return { w: pixelWidth * s, h: pixelHeight * s };
}

function capacity(widthDip) {
    var n = Math.floor((widthDip - 200) / Spacing);
    return Math.max(MinItems, Math.min(MaxItemsCap, n));
}

// 「N 分钟前」小字：从图库文件名（来源_yyyyMMdd_HHmmss[_n].png）抠时间，抠不出就不显示。
function timeAgo(path) {
    var m = /_(\d{8})_(\d{6})(?:_\d+)?\.png$/i.exec(String(path));
    if (!m) return "";
    var s = m[1] + "T" + m[2].slice(0, 2) + ":" + m[2].slice(2, 4) + ":" + m[2].slice(4, 6);
    var t = new Date(s);
    if (isNaN(t.getTime())) return "";
    var sec = Math.max(0, (Date.now() - t.getTime()) / 1000);
    if (sec < 60) return "刚刚";
    if (sec < 3600) return Math.floor(sec / 60) + " 分钟前";
    if (sec < 86400) return Math.floor(sec / 3600) + " 小时前";
    return Math.floor(sec / 86400) + " 天前";
}

// 阻尼弹簧参数：response 是周期（秒），dampingFraction 是阻尼比
function spring(response, dampingFraction) {
    var omega = 2 * Math.PI / response;
    return { k: omega * omega, c: 2 * dampingFraction * omega };
}

// ArrayBuffer → base64。拖放里的 image/* 字节经助手 stdin 进图库，QML 写不了二进制文件。
function base64(buffer) {
    var bytes = new Uint8Array(buffer);
    var table = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    var out = "";
    var i = 0;
    for (; i + 2 < bytes.length; i += 3) {
        var n = (bytes[i] << 16) | (bytes[i + 1] << 8) | bytes[i + 2];
        out += table[(n >> 18) & 63] + table[(n >> 12) & 63] + table[(n >> 6) & 63] + table[n & 63];
    }
    if (i < bytes.length) {
        var b = i + 1 < bytes.length ? bytes[i + 1] : 0;
        var n2 = (bytes[i] << 16) | (b << 8);
        out += table[(n2 >> 18) & 63] + table[(n2 >> 12) & 63];
        out += i + 1 < bytes.length ? table[(n2 >> 6) & 63] : "=";
        out += "=";
    }
    return out;
}

// 拖放数据里取地址和图像字节（DrawerWindow 和 Caelestia 的 xitu 抽屉共用）。log 是记日志的函数。
function dropUrls(drop, log) {
    var out = [];
    if (drop.hasUrls && drop.urls) {
        for (var i = 0; i < drop.urls.length; i++) out.push(String(drop.urls[i]));
    }
    try {
        if (out.length === 0 && drop.formats && drop.getDataAsString) {
            for (var j = 0; j < drop.formats.length; j++) {
                var f = String(drop.formats[j]).toLowerCase();
                if (f !== "text/uri-list" && f !== "text/x-moz-url" && f !== "text/plain") continue;
                var text = drop.getDataAsString(String(drop.formats[j]));
                if (!text) continue;
                if (f === "text/x-moz-url") {
                    var first = text.split("\n")[0].replace(/\0/g, "").trim();
                    if (first !== "") out.push(first);
                } else {
                    var lines = text.split(/\r?\n/);
                    for (var k = 0; k < lines.length; k++) {
                        var line = lines[k].trim();
                        if (line !== "" && line.charAt(0) !== "#") out.push(line);
                    }
                }
            }
        }
    } catch (e) { log("读拖放地址失败 " + e); }
    if (out.length === 0 && drop.hasText && drop.text) {
        var t = String(drop.text).trim();
        if (/^(file:|https?:|data:image\/)/i.test(t)) out.push(t);
    }
    return out;
}

function dropImageB64(drop, log) {
    if (!drop.formats || !drop.getDataAsArrayBuffer) return "";
    var prefer = ["image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp", "image/bmp", "image/tiff"];
    var found = "";
    var i;
    for (i = 0; i < drop.formats.length; i++) {
        if (prefer.indexOf(String(drop.formats[i]).toLowerCase()) >= 0) {
            found = String(drop.formats[i]);
            break;
        }
    }
    if (found === "") {
        for (i = 0; i < drop.formats.length; i++) {
            var g = String(drop.formats[i]).toLowerCase();
            if (g.indexOf("image/") === 0) { found = String(drop.formats[i]); break; }
        }
    }
    if (found === "") return "";
    try {
        var buf = drop.getDataAsArrayBuffer(found);
        if (!buf || !(buf.byteLength > 0)) return "";
        if (buf.byteLength > 30 * 1024 * 1024) {
            log("拖入图片超过 30 MB");
            return "";
        }
        return base64(buf);
    } catch (e) {
        log("读拖放图像失败 " + e);
        return "";
    }
}

