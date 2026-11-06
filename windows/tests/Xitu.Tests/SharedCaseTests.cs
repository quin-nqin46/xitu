using System.Text.Json;
using Xunit;

namespace Clothesline.Tests;

/// 跑 shared/tests/*.json 里两端共用的用例。Linux 那边 linux/tests/test_shared_cases.py 读同一份文件。
public class SharedCaseTests
{
    private static JsonElement Load(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, "shared", "tests", name);
            if (File.Exists(path)) return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
            dir = dir.Parent;
        }
        throw new FileNotFoundException("找不到 shared/tests/" + name);
    }

    public static IEnumerable<object[]> ClipboardCases() =>
        Load("clipboard.json").GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("name").GetString()!, c.GetRawText() });

    [Theory]
    [MemberData(nameof(ClipboardCases))]
    public void Clipboard(string name, string json)
    {
        var c = JsonDocument.Parse(json).RootElement;
        var formats = c.GetProperty("formats").EnumerateArray().Select(f => f.GetString()).ToHashSet();
        var f = new ClipboardFormats(
            Bitmap: formats.Contains("bitmap"),
            FileDrop: formats.Contains("files"),
            Rtf: formats.Contains("rtf"),
            Html: formats.Contains("html"),
            Text: formats.Contains("text"));
        var html = c.TryGetProperty("html", out var h) ? h.GetString() ?? "" : "";
        var verdict = ClipboardRule.Decide(f, c.GetProperty("collect").GetString()!, () => html);
        Assert.True(c.GetProperty("take").GetBoolean() == ClipboardRule.Takes(verdict), name + " → " + verdict);
    }

    [Fact]
    public void Capacity()
    {
        foreach (var c in Load("layout.json").GetProperty("capacity").EnumerateArray())
            Assert.Equal(c.GetProperty("items").GetInt32(), LineCapacity.ForWidth(c.GetProperty("width").GetDouble()));
    }

    [Fact]
    public void TriggerSpan()
    {
        foreach (var c in Load("layout.json").GetProperty("triggerSpan").EnumerateArray())
        {
            var (left, right) = TriggerZone.Span(c.GetProperty("count").GetInt32(), c.GetProperty("width").GetDouble());
            Assert.Equal(c.GetProperty("left").GetDouble(), left, 6);
            Assert.Equal(c.GetProperty("right").GetDouble(), right, 6);
        }
    }

    [Fact]
    public void RetentionLabel()
    {
        foreach (var c in Load("layout.json").GetProperty("retentionLabel").EnumerateArray())
            Assert.Equal(c.GetProperty("label").GetString(), RetentionRule.Label(c.GetProperty("days").GetInt32()));
    }
}
