using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clothesline;

/// 一个颜色，ARGB。不依赖 WPF，方便单测。
internal readonly record struct Argb(byte A, byte R, byte G, byte B)
{
    public static readonly Argb Transparent = new(0, 0, 0, 0);
    public bool IsTransparent => A == 0;
}

internal sealed record RopeLook(Argb Color, double Width, Argb ShadowColor, double ShadowWidth, double ShadowOffset, string Texture);
internal sealed record EndsLook(string Style, double Inset, Argb PinColor, Argb PinRing, Argb DotColor, double Radius, double DotRadius);
internal sealed record ClipLook(
    Argb Color, double Width, double Height, double Radius,
    Argb Highlight, double HighlightWidth, double HighlightHeight, double HighlightRadius, double HighlightShift,
    double Above, double Below, Argb Border, bool Shadow);
internal sealed record FrameLook(Argb Paper, double PadLeft, double PadTop, double PadRight, double PadBottom, double Radius, double PhotoRadius);
internal sealed record HintLook(Argb Bg, Argb Fg, Argb Border);

/// 主题解析完、颜色都算好的结果。字段和 shared/themes/README.md 一致。
internal sealed record ThemeLook(
    string Id, string Name, Argb Accent, Argb AccentStrong,
    RopeLook Rope, EndsLook Ends, ClipLook Clip, FrameLook Frame, HintLook Hint, Argb GalleryBg)
{
    /// 相框上沿在夹子顶下多少。和 Linux Card.qml 的 frameDrop 一样：老的 PinHeight 28 − 12 − PinAbove 9.5。
    public const double FrameDrop = 28 - 12 - 9.5;

    public double FrameTop => Clip.Above + FrameDrop;
}

/// 读主题 JSON。规则和 Linux 版 xitu_helper.normalize_theme 一致：缺的、不合法的字段用模板（polaroid-blue）补，
/// 颜色 "accent" / "accent-strong" 按 ThemeEngine.qml 换算。
internal static class ThemeSpec
{
    public const string DefaultId = "polaroid-blue";
    public static readonly string[] PreferredOrder = { "polaroid-blue", "red-rope", "graphite", "linen" };

    private static readonly Regex IdPattern = new(@"^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex Hex6 = new(@"^#([0-9A-Fa-f]{6})$", RegexOptions.CultureInvariant);
    private static readonly Regex Hex8 = new(@"^#([0-9A-Fa-f]{8})$", RegexOptions.CultureInvariant);
    private static readonly Regex Rgba = new(
        @"^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([0-9.]+)\s*)?\)$", RegexOptions.CultureInvariant);

    /// 和 xitu_helper.THEME_FALLBACK 一致。内置 polaroid-blue.json 读不了才用。
    public const string FallbackJson = """
        {
          "name": "主题蓝绳 + 拍立得",
          "accent": "#3D6FE0",
          "rope": { "color": "accent-strong", "width": 3, "shadowColor": "rgba(20,30,60,0.20)", "shadowWidth": 4, "shadowOffset": 2, "texture": "none" },
          "ends": { "style": "pin", "inset": 40, "pinColor": "#FFFFFF", "pinRing": "rgba(30,40,70,0.20)", "dotColor": "accent-strong", "radius": 8, "dotRadius": 3.5 },
          "clip": { "color": "accent-strong", "width": 12, "height": 22, "radius": 3, "highlight": "rgba(255,255,255,0.55)", "highlightWidth": 2,
                    "highlightHeight": 16, "highlightRadius": 1, "highlightShift": -2.5, "above": 10, "below": 12, "border": "", "shadow": true },
          "frame": { "paper": "#FFFFFF", "padLeft": 7, "padTop": 7, "padRight": 7, "padBottom": 22, "radius": 4, "photoRadius": 2 },
          "hint": { "bg": "#FFFFFF", "fg": "#2E3440", "border": "rgba(30,40,70,0.20)" },
          "gallery": { "bg": "#F6F1E7" }
        }
        """;

    public static bool IsValidId(string? id) => id != null && IdPattern.IsMatch(id);

    /// 解析一份主题。json 不是对象时抛 JsonException。template 为空时用 FallbackJson。
    public static ThemeLook Parse(string id, string json, string? templateJson = null)
    {
        using var doc = JsonDocument.Parse(json);
        using var tpl = JsonDocument.Parse(templateJson ?? FallbackJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("主题不是对象");
        return Build(id, doc.RootElement, tpl.RootElement);
    }

    private static ThemeLook Build(string id, JsonElement src, JsonElement tpl)
    {
        var name = Str(src, "name");
        if (string.IsNullOrWhiteSpace(name)) name = Str(tpl, "name") ?? "主题蓝绳 + 拍立得";
        var accentRaw = RawColor(Get(src, "accent"), RawColor(Get(tpl, "accent"), "#3D6FE0", false) ?? "#3D6FE0", false)!;
        // accent 本身写成 token 时没有意义，按 ThemeEngine.fallbackAccent 退回默认
        var accent = accentRaw is "accent" or "accent-strong" ? ParseColor("#3D6FE0") : ParseColor(accentRaw);
        var strong = Strengthen(accent);

        Argb C(JsonElement? s, JsonElement? t, string key, bool allowEmpty = false)
        {
            var fallback = RawColor(Get(t, key), "", allowEmpty) ?? "";
            var raw = RawColor(Get(s, key), fallback, allowEmpty) ?? "";
            return raw switch
            {
                "accent" => accent,
                "accent-strong" => strong,
                "" => Argb.Transparent,
                _ => ParseColor(raw),
            };
        }

        double N(JsonElement? s, JsonElement? t, string key) => Num(Get(s, key), Num(Get(t, key), 0));

        var sr = Obj(src, "rope"); var tr = Obj(tpl, "rope");
        var se = Obj(src, "ends"); var te = Obj(tpl, "ends");
        var sc = Obj(src, "clip"); var tc = Obj(tpl, "clip");
        var sf = Obj(src, "frame"); var tf = Obj(tpl, "frame");
        var sh = Obj(src, "hint"); var th = Obj(tpl, "hint");
        var sg = Obj(src, "gallery"); var tg = Obj(tpl, "gallery");

        var texture = OneOf(Str(sr, "texture"), new[] { "none", "twist" }) ?? OneOf(Str(tr, "texture"), new[] { "none", "twist" }) ?? "none";
        var style = OneOf(Str(se, "style"), new[] { "pin", "knot", "none" }) ?? OneOf(Str(te, "style"), new[] { "pin", "knot", "none" }) ?? "pin";
        var shadow = Bool(Get(sc, "shadow")) ?? Bool(Get(tc, "shadow")) ?? true;

        var galleryBg = C(sg, tg, "bg");
        if (galleryBg.IsTransparent) galleryBg = ParseColor("#F6F1E7");

        return new ThemeLook(
            id, name!.Trim(), accent, strong,
            new RopeLook(C(sr, tr, "color"), N(sr, tr, "width"), C(sr, tr, "shadowColor"), N(sr, tr, "shadowWidth"), N(sr, tr, "shadowOffset"), texture),
            new EndsLook(style, N(se, te, "inset"), C(se, te, "pinColor"), C(se, te, "pinRing"), C(se, te, "dotColor"), N(se, te, "radius"), N(se, te, "dotRadius")),
            new ClipLook(
                C(sc, tc, "color"), N(sc, tc, "width"), N(sc, tc, "height"), N(sc, tc, "radius"),
                C(sc, tc, "highlight", allowEmpty: true), N(sc, tc, "highlightWidth"), N(sc, tc, "highlightHeight"),
                N(sc, tc, "highlightRadius"), N(sc, tc, "highlightShift"),
                N(sc, tc, "above"), N(sc, tc, "below"), C(sc, tc, "border", allowEmpty: true), shadow),
            new FrameLook(C(sf, tf, "paper"), N(sf, tf, "padLeft"), N(sf, tf, "padTop"), N(sf, tf, "padRight"), N(sf, tf, "padBottom"),
                N(sf, tf, "radius"), N(sf, tf, "photoRadius")),
            new HintLook(C(sh, th, "bg"), C(sh, th, "fg"), C(sh, th, "border", allowEmpty: true)),
            galleryBg);
    }

    /// 颜色字面量：#RRGGBB、#AARRGGBB（和 Qt 一样 alpha 在前）、rgb()/rgba()。读不出来按不透明黑。
    public static Argb ParseColor(string value)
    {
        var v = value.Trim();
        var m = Hex6.Match(v);
        if (m.Success)
        {
            var n = uint.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Argb(255, (byte)(n >> 16), (byte)(n >> 8), (byte)n);
        }
        m = Hex8.Match(v);
        if (m.Success)
        {
            var n = uint.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Argb((byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n);
        }
        m = Rgba.Match(v);
        if (m.Success)
        {
            byte Ch(int i) => (byte)Math.Clamp(int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture), 0, 255);
            var a = m.Groups[4].Success ? double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 1.0;
            return new Argb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), Ch(1), Ch(2), Ch(3));
        }
        return new Argb(255, 0, 0, 0);
    }

    /// 同 ThemeEngine.strengthen：色相不动，饱和度低于 0.65 提到 0.65，HSL 亮度高于 0.52 压到 0.52。
    public static Argb Strengthen(Argb c)
    {
        var (h, s, l) = ToHsl(c);
        if (s < 0.65) s = 0.65;
        if (l > 0.52) l = 0.52;
        return FromHsl(h, s, l);
    }

    public static (double H, double S, double L) ToHsl(Argb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-9) return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        return (h / 6, s, l);
    }

    public static Argb FromHsl(double h, double s, double l)
    {
        double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        double r, g, b;
        if (s <= 0) r = g = b = l;
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = Hue(p, q, h + 1.0 / 3);
            g = Hue(p, q, h);
            b = Hue(p, q, h - 1.0 / 3);
        }
        byte B(double x) => (byte)Math.Round(Math.Clamp(x, 0, 1) * 255);
        return new Argb(255, B(r), B(g), B(b));
    }

    private static JsonElement? Get(JsonElement? obj, string key)
    {
        if (obj is not { ValueKind: JsonValueKind.Object } o) return null;
        return o.TryGetProperty(key, out var v) ? v : null;
    }

    private static JsonElement? Obj(JsonElement obj, string key)
    {
        var v = Get(obj, key);
        return v is { ValueKind: JsonValueKind.Object } ? v : null;
    }

    private static string? Str(JsonElement? obj, string key)
    {
        var v = Get(obj, key);
        return v is { ValueKind: JsonValueKind.String } s ? s.GetString() : null;
    }

    private static string? OneOf(string? value, string[] allowed) => value != null && allowed.Contains(value) ? value : null;

    private static bool? Bool(JsonElement? v) => v?.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    /// 同 xitu_helper._num：数字或能转成数字的字符串，否则用 fallback。
    private static double Num(JsonElement? v, double fallback)
    {
        if (v is not { } e) return fallback;
        if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d) && !double.IsNaN(d)) return d;
        if (e.ValueKind == JsonValueKind.String
            && double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && !double.IsNaN(p)) return p;
        return fallback;
    }

    /// 同 xitu_helper._color：token、空串（允许时）、合法字面量原样返回，其余用 fallback。
    private static string? RawColor(JsonElement? v, string fallback, bool allowEmpty)
    {
        if (v is not { ValueKind: JsonValueKind.String } e) return fallback;
        var s = (e.GetString() ?? "").Trim();
        if (s is "accent" or "accent-strong") return s;
        if (s.Length == 0) return allowEmpty ? "" : fallback;
        if (Hex6.IsMatch(s) || Hex8.IsMatch(s) || Rgba.IsMatch(s)) return s;
        return fallback;
    }
}
