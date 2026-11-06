// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.Windows.Threading;

namespace Clothesline;

/// 过期图进回收站。收藏的和正挂在绳上的不清理。
internal static class Retention
{
    public static (int Days, string Label)[] Options => RetentionRule.Options;

    private static DispatcherTimer? _timer;
    private static Line? _line;
    private static bool _first = true;

    public static string Label(int days) => RetentionRule.Label(days);

    public static void Start(Line line, Dispatcher dispatcher)
    {
        _line = line;
        _timer?.Stop();
        _first = true;
        _timer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => Tick(), dispatcher);
        _timer.Start();
    }

    public static int SweepNow(Line line)
    {
        var days = AppSettings.Current.RetentionDays;
        if (days <= 0)
        {
            Log.Info("保存时间为永久，跳过清理");
            return 0;
        }
        var cutoff = DateTime.Now.AddDays(-days);
        var keep = new HashSet<string>(line.Items.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        var n = 0;
        foreach (var item in Library.List())
        {
            if (item.Favorite) continue;
            if (keep.Contains(item.Path)) continue;
            if (item.Taken >= cutoff) continue;
            if (Library.Recycle(item.Path)) n++;
        }
        Log.Info("清理过期 " + n + " 张");
        return n;
    }

    private static void Tick()
    {
        if (_line == null) return;
        SweepNow(_line);
        if (!_first) return;
        _first = false;
        if (_timer != null) _timer.Interval = TimeSpan.FromHours(6);
    }
}
