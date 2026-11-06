namespace Clothesline;

/// 顶边感应只在绳子挂照片的那一段（屏幕中间），不再是整条顶边，免得去点浏览器标签页、标题栏时绳子跟着下来。
/// 和 Linux 版 layout.js 的 triggerSpan 一致，数值有单测对照。
internal static class TriggerZone
{
    /// 没有照片或照片很少时，中间至少留这么宽的一半。
    public const double MinHalf = 160;
    /// 两头照片外面再多出的余量。
    public const double Margin = 14;
    /// 顶边窄带高度（DIP）。指针撞到屏幕顶就停在 y=0，几像素就够了。
    public const double BandDip = 4;

    /// 返回感应段相对屏幕左边的 [左, 右)（DIP）。照片摆放同 Layout.X：以屏幕中线对称。
    public static (double Left, double Right) Span(int count, double widthDip)
    {
        if (widthDip <= 0) return (0, 0);
        var cards = Math.Max(count - 1, 0) * LineCapacity.Spacing / 2 + LineCapacity.CardWidth / 2 + Margin;
        var half = Math.Max(MinHalf, cards);
        var mid = widthDip / 2;
        return (Math.Max(0, mid - half), Math.Min(widthDip, mid + half));
    }

    public static bool Contains(int count, double widthDip, double xDip, double yDip, double bandDip = BandDip)
    {
        if (yDip < 0 || yDip >= bandDip) return false;
        var (left, right) = Span(count, widthDip);
        return xDip >= left && xDip < right;
    }
}
