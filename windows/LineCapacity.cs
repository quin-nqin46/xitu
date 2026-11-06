namespace Clothesline;

/// 绳上最多挂几张。和 Linux 版 layout.js 的 capacity / XituCore.capacity 一致，数值有单测对照。
internal static class LineCapacity
{
    public const double Spacing = 174;
    public const int MinItems = 3;
    public const int MaxItemsCap = 12;

    public static int ForWidth(double widthDip)
    {
        var n = (int)Math.Floor((widthDip - 200) / Spacing);
        return Math.Clamp(n, MinItems, MaxItemsCap);
    }

    /// 每块屏的绳子都挂同一串，按最窄那块算，免得窄屏两头挂出屏幕。没有屏时按上限。
    public static int ForScreens(IEnumerable<double> widthsDip)
    {
        var n = MaxItemsCap;
        foreach (var w in widthsDip) n = Math.Min(n, ForWidth(w));
        return n;
    }
}
