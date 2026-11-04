// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.Windows.Media;

namespace Clothesline;

/// 系图配色。刷子都冻住，给绳子和卡片共用。
internal static class Theme
{
    public static readonly Color Red = Color.FromRgb(0xB5, 0x30, 0x2A);
    public static readonly Color Wood = Color.FromRgb(0xC8, 0xA5, 0x7A);
    public static readonly Color WoodStroke = Color.FromRgb(0x94, 0x74, 0x4C);
    public static readonly Color Paper = Color.FromRgb(0xF6, 0xF1, 0xE7);
    public static readonly Color Ink = Color.FromRgb(0x2E, 0x34, 0x40);
    public static readonly Color Card = Colors.White;
    public static readonly Color RopeShadow = Color.FromArgb(89, 0, 0, 0); // 0.35 * 255
    public static readonly Color MenuHover = Color.FromRgb(0xEF, 0xDD, 0xD6);
    public static readonly Color Mute = Color.FromRgb(0x8A, 0x84, 0x78);
    public static readonly Color Place = Color.FromRgb(0xE8, 0xE0, 0xD4);

    public static readonly SolidColorBrush RedBrush = Freeze(Red);
    public static readonly SolidColorBrush WoodBrush = Freeze(Wood);
    public static readonly SolidColorBrush WoodStrokeBrush = Freeze(WoodStroke);
    public static readonly SolidColorBrush PaperBrush = Freeze(Paper);
    public static readonly SolidColorBrush InkBrush = Freeze(Ink);
    public static readonly SolidColorBrush CardBrush = Freeze(Card);
    public static readonly SolidColorBrush WhiteBrush = Freeze(Colors.White);
    public static readonly SolidColorBrush RopeShadowBrush = Freeze(RopeShadow);
    public static readonly SolidColorBrush MenuHoverBrush = Freeze(MenuHover);
    public static readonly SolidColorBrush MuteBrush = Freeze(Mute);
    public static readonly SolidColorBrush PlaceBrush = Freeze(Place);

    public static readonly Pen RopePen = FreezePen(RedBrush, 2.5);
    public static readonly Pen RopeShadowPen = FreezePen(RopeShadowBrush, 3);

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FreezePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        return pen;
    }
}
