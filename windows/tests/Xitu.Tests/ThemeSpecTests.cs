using Xunit;

namespace Clothesline.Tests;

/// 主题文件在 shared/themes，Windows 和 Linux 共用。这里读仓库里的那份。
public class ThemeSpecTests
{
    private static string ThemesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "shared", "themes");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("找不到 shared/themes");
    }

    private static string Json(string id) => File.ReadAllText(Path.Combine(ThemesDir(), id + ".json"));

    private static ThemeLook Load(string id) => ThemeSpec.Parse(id, Json(id), Json(ThemeSpec.DefaultId));

    public static IEnumerable<object[]> AllThemes() =>
        Directory.GetFiles(ThemesDir(), "*.json").Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

    [Theory]
    [MemberData(nameof(AllThemes))]
    public void EveryBuiltinThemeParses(string id)
    {
        Assert.True(ThemeSpec.IsValidId(id));
        var t = Load(id);
        Assert.False(string.IsNullOrWhiteSpace(t.Name));
        Assert.Contains(t.Ends.Style, new[] { "pin", "knot", "none" });
        Assert.Contains(t.Rope.Texture, new[] { "none", "twist" });
        Assert.True(t.Rope.Width > 0);
        Assert.True(t.Clip.Width > 0 && t.Clip.Height > 0);
    }

    [Fact]
    public void PreferredThemesExist()
    {
        foreach (var id in ThemeSpec.PreferredOrder)
            Assert.True(File.Exists(Path.Combine(ThemesDir(), id + ".json")), id);
    }

    [Fact]
    public void AccentStrongMatchesLinux()
    {
        // linux/tools/render-rope.qml 注明 #3D6FE0 的 accent-strong 是 #2C62DD
        Assert.Equal(new Argb(255, 0x2C, 0x62, 0xDD), ThemeSpec.Strengthen(ThemeSpec.ParseColor("#3D6FE0")));
    }

    [Fact]
    public void DefaultThemeIsBlueRopeWithPins()
    {
        var t = Load(ThemeSpec.DefaultId);
        Assert.Equal("pin", t.Ends.Style);
        Assert.Equal(t.AccentStrong, t.Rope.Color);
        Assert.Equal(t.AccentStrong, t.Clip.Color);
        Assert.Equal(22, t.Frame.PadBottom);
    }

    [Fact]
    public void FallbackMatchesPolaroidBlueFile()
    {
        var file = Load(ThemeSpec.DefaultId);
        var fallback = ThemeSpec.Parse(ThemeSpec.DefaultId, ThemeSpec.FallbackJson);
        Assert.Equal(file.Rope, fallback.Rope);
        Assert.Equal(file.Ends, fallback.Ends);
        Assert.Equal(file.Clip, fallback.Clip);
        Assert.Equal(file.Frame, fallback.Frame);
        Assert.Equal(file.Hint, fallback.Hint);
        Assert.Equal(file.GalleryBg, fallback.GalleryBg);
    }

    [Fact]
    public void RedRopeLooksLikeTheOldWindowsRope()
    {
        // 换主题之前 Windows 写死的样子（Theme.cs / Layout.cs / Clothespin）
        var t = Load("red-rope");
        Assert.Equal(new Argb(255, 0xB5, 0x30, 0x2A), t.Rope.Color);
        Assert.Equal(2.5, t.Rope.Width);
        Assert.Equal("knot", t.Ends.Style);
        Assert.Equal(90, t.Ends.Inset);
        Assert.Equal(14, t.Ends.Radius);
        Assert.Equal(new Argb(255, 0xC8, 0xA5, 0x7A), t.Clip.Color);
        Assert.Equal(new Argb(255, 0x94, 0x74, 0x4C), t.Clip.Border);
        Assert.Equal(10, t.Clip.Width);
        Assert.Equal(28, t.Clip.Height);
        Assert.Equal(9.5, t.Clip.Above);
        Assert.Equal(16, t.FrameTop);   // 老的 Clothespin.HeightDip - 12
        Assert.Equal(5, t.Frame.PadLeft);
        Assert.Equal(10, t.Frame.Radius);
        Assert.Equal(6, t.Frame.PhotoRadius);
        Assert.True(t.Clip.Highlight.IsTransparent);
    }

    [Fact]
    public void MissingAndBadFieldsFallBackToTemplate()
    {
        var t = ThemeSpec.Parse("x", """{ "name": "  ", "rope": { "color": "not-a-color", "width": "4" }, "ends": { "style": "weird" } }""");
        var d = ThemeSpec.Parse("x", ThemeSpec.FallbackJson);
        Assert.Equal(d.Name, t.Name);
        Assert.Equal(d.Rope.Color, t.Rope.Color);
        Assert.Equal(4, t.Rope.Width);
        Assert.Equal("pin", t.Ends.Style);
        Assert.Equal(d.Clip, t.Clip);
    }

    [Fact]
    public void EmptyBorderMeansNone()
    {
        var t = ThemeSpec.Parse("x", """{ "clip": { "border": "" }, "hint": { "border": "" } }""");
        Assert.True(t.Clip.Border.IsTransparent);
        Assert.True(t.Hint.Border.IsTransparent);
    }

    [Fact]
    public void NonObjectThrows()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ThemeSpec.Parse("x", "[1,2]"));
    }

    [Theory]
    [InlineData("#3D6FE0", 255, 0x3D, 0x6F, 0xE0)]
    [InlineData("#803D6FE0", 0x80, 0x3D, 0x6F, 0xE0)]   // 和 Qt 一样 alpha 在前
    [InlineData("rgba(20,30,60,0.20)", 51, 20, 30, 60)]
    [InlineData("rgb(1, 2, 3)", 255, 1, 2, 3)]
    public void ParseColor(string s, int a, int r, int g, int b)
    {
        Assert.Equal(new Argb((byte)a, (byte)r, (byte)g, (byte)b), ThemeSpec.ParseColor(s));
    }

    [Theory]
    [InlineData("polaroid-blue", true)]
    [InlineData("my_theme-2", true)]
    [InlineData("../etc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IdValidation(string? id, bool ok)
    {
        Assert.Equal(ok, ThemeSpec.IsValidId(id));
    }
}
