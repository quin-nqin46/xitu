.pragma library
// 界面字体：按顺序挑第一个装了的。以前写死 HarmonyOS Sans SC，没装的机器上 Qt 会随便退回一种字体，
// 中文可能是方块或别的字重。都没装时用 fontconfig 的 sans-serif，由系统选能显示中文的那种。
// 改字体只改这里。

var preferred = [
    "HarmonyOS Sans SC",
    "Noto Sans CJK SC",
    "Noto Sans SC",
    "Source Han Sans SC",
    "Source Han Sans CN",
    "Sarasa UI SC",
    "WenQuanYi Micro Hei",
    "Microsoft YaHei"
];

function pick(available) {
    for (var i = 0; i < preferred.length; i++)
        if (available.indexOf(preferred[i]) >= 0) return preferred[i];
    return "sans-serif";
}

var ui = pick(typeof Qt !== "undefined" && Qt.fontFamilies ? Qt.fontFamilies() : []);
