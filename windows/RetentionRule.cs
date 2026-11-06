namespace Clothesline;

/// 保存时间的可选项和显示文字。不碰 WPF，方便单测；和 Linux 版 xitu_helper.py 的 RETENTION_OPTIONS 一致。
internal static class RetentionRule
{
    public static readonly (int Days, string Label)[] Options =
    {
        (7, "7 天"),
        (30, "30 天"),
        (90, "90 天"),
        (365, "1 年"),
        (0, "永久"),
    };

    public static string Label(int days)
    {
        foreach (var option in Options)
            if (option.Days == days) return option.Label;
        return days + " 天";
    }
}
