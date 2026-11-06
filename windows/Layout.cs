using System.Windows;

namespace Clothesline;

/// 尺寸与时间，对齐参考项目 LineView.Layout / PeggedView / AppDelegate。
internal static class Layout
{
    public const double PanelHeight = 210;
    public const double RopeTop = 10;
    public const double Spacing = LineCapacity.Spacing;
    public const double CardWidth = 150;
    public const double PinAbove = 9.5;
    public const double FrameRadius = 10;
    public const double FrameInset = 5;
    public const double PhotoMaxHeight = 104;
    public const double TuckGap = 12;
    public const double RevealMs = 350;
    public const double HideMs = 220;

    /// 顶边热区高度（DIP）。Windows 没有菜单栏，用一条贴着屏幕顶边的窄带代替。
    public const double TopBandDip = 20;

    public const int MinItems = LineCapacity.MinItems;
    public const int MaxItemsCap = LineCapacity.MaxItemsCap;

    public static readonly TimeSpan RevealDelay = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan RetractDelay = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan PeekDuration = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan HoldToEdit = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan CopiedHint = TimeSpan.FromSeconds(1.2);
    public static readonly TimeSpan NudgeOut = TimeSpan.FromMilliseconds(300);

    public const double ArriveDrop = 46;
    public const double FallSeconds = 0.6;
    public const double FallDistance = 180;
    public const double HoverScale = 1.035;
    public const double PressedScale = 0.95;
    public const double LiftScale = 1.06;
    public const double DragOpacity = 0.45;
    public const double PositionResponse = 0.55;
    public const double PositionDamping = 0.78;
    public const double ArriveResponse = 0.42;
    public const double ArriveDamping = 0.72;

    /// 截图飞向卡片。原版时长 0.65、弧高 30；这里落在 0.5–0.6 秒。
    public const double FlightSeconds = 0.56;
    public const double FlightArc = 30;

    public const double PhotoRadius = 6;

    public const double KnotInset = 90;
    public const double KnotSize = 14;

    public static double Sag(double width) => Math.Min(30, width * 0.018);

    public static double RopeY(double x, double width)
    {
        if (width <= 0) return RopeTop;
        var f = x / width;
        return RopeTop + 4 * Sag(width) * f * (1 - f);
    }

    public static double X(int index, int count, double width)
    {
        var total = Math.Max(count - 1, 0) * Spacing;
        return width / 2 - total / 2 + index * Spacing;
    }

    public static Size PhotoSize(double pixelWidth, double pixelHeight)
    {
        var maxW = CardWidth - 14;
        const double maxH = PhotoMaxHeight;
        if (pixelWidth <= 0 || pixelHeight <= 0) return new Size(maxW, maxH);
        var scale = Math.Min(maxW / pixelWidth, maxH / pixelHeight);
        return new Size(pixelWidth * scale, pixelHeight * scale);
    }

    /// 卡片在绳子视图里的位置（不含夹子），和 PeggedView 的摆放一致。
    public static Rect CardRect(int index, int count, double width, double pixelWidth, double pixelHeight)
    {
        var x = X(index, count, width);
        var pegLeft = x - CardWidth / 2;
        var pegTop = RopeY(x, width) - PinAbove;
        var photo = PhotoSize(pixelWidth, pixelHeight);
        var cardW = photo.Width + FrameInset * 2;
        var cardH = photo.Height + FrameInset * 2;
        var cardLeft = pegLeft + (CardWidth - cardW) / 2;
        var cardTop = pegTop + Clothespin.HeightDip - 12;
        return new Rect(cardLeft, cardTop, cardW, cardH);
    }

    public static int Capacity(double widthDip) => LineCapacity.ForWidth(widthDip);
}
