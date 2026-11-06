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

/// 当前绳子主题（shared/themes/*.json，和 Linux 版共用）。内置主题打包在程序里，
/// 用户主题放 %LOCALAPPDATA%\Xitu\themes\<id>.json，同 id 用户优先。当前用哪套写在 settings.json 的 Theme。
internal static class ThemeStore
{
    private static ThemeLook? _current;

    public static event Action? Changed;

    public static ThemeLook Current => _current ??= LoadOrDefault(AppSettings.Current.Theme);

    public static string UserDir => System.IO.Path.Combine(AppPaths.Root, "themes");

    public static void Load()
    {
        _current = LoadOrDefault(AppSettings.Current.Theme);
        Log.Info($"主题 {_current.Id} {_current.Name}");
    }

    /// 切换主题并写进 settings.json。没有这个主题时返回 false。
    public static bool Set(string id)
    {
        var look = TryLoad(id);
        if (look == null)
        {
            Log.Error("没有主题 " + id);
            return false;
        }
        AppSettings.Current.Theme = id;
        AppSettings.Current.Save();
        _current = look;
        Log.Info($"切换主题 {id}");
        Changed?.Invoke();
        return true;
    }

    /// 顺序同 Linux 版 list_themes：四个常用的在前，其余内置按名字，再是用户目录里多出来的。
    public static IReadOnlyList<(string Id, string Name)> List()
    {
        var ids = new List<string>();
        void Add(string id)
        {
            if (ThemeSpec.IsValidId(id) && !ids.Contains(id) && ReadJson(id) != null) ids.Add(id);
        }
        foreach (var id in ThemeSpec.PreferredOrder) Add(id);
        foreach (var id in BuiltinIds().OrderBy(s => s, StringComparer.Ordinal)) Add(id);
        try
        {
            if (System.IO.Directory.Exists(UserDir))
                foreach (var f in System.IO.Directory.GetFiles(UserDir, "*.json").OrderBy(s => s, StringComparer.Ordinal))
                    Add(System.IO.Path.GetFileNameWithoutExtension(f));
        }
        catch (Exception ex) { Log.Error("读用户主题目录失败: " + ex.Message); }
        return ids.Select(id => (id, TryLoad(id)?.Name ?? id)).ToList();
    }

    public static ThemeLook? TryLoad(string? id)
    {
        if (!ThemeSpec.IsValidId(id)) return null;
        var json = ReadJson(id!);
        if (json == null) return null;
        try { return ThemeSpec.Parse(id!, json, Template()); }
        catch (Exception ex)
        {
            Log.Error($"主题 {id} 读不了：{ex.Message}，改用默认");
            return null;
        }
    }

    private static ThemeLook LoadOrDefault(string? id)
    {
        var look = TryLoad(id) ?? TryLoad(ThemeSpec.DefaultId);
        return look ?? ThemeSpec.Parse(ThemeSpec.DefaultId, ThemeSpec.FallbackJson);
    }

    private static string? Template() => Builtin(ThemeSpec.DefaultId);

    private static string? ReadJson(string id)
    {
        try
        {
            var user = System.IO.Path.Combine(UserDir, id + ".json");
            if (System.IO.File.Exists(user)) return System.IO.File.ReadAllText(user);
        }
        catch (Exception ex) { Log.Error("读用户主题失败: " + ex.Message); }
        return Builtin(id);
    }

    private const string ResourcePrefix = "themes/";

    private static IEnumerable<string> BuiltinIds() =>
        typeof(ThemeStore).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..^".json".Length]);

    private static string? Builtin(string id)
    {
        using var stream = typeof(ThemeStore).Assembly.GetManifestResourceStream(ResourcePrefix + id + ".json");
        if (stream == null) return null;
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static Color ToColor(Argb c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    public static SolidColorBrush Brush(Argb c)
    {
        var b = new SolidColorBrush(ToColor(c));
        b.Freeze();
        return b;
    }

    public static Pen Pen(Argb c, double thickness, DashStyle? dash = null)
    {
        var p = new Pen(Brush(c), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, DashCap = PenLineCap.Round };
        if (dash != null) p.DashStyle = dash;
        p.Freeze();
        return p;
    }
}
