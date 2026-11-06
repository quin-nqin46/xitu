using System.Text.RegularExpressions;
using Xunit;

namespace Clothesline.Tests;

/// 读 linux/ 下的源文件，核对两端共用的常量和取值没有分叉。改一边忘了另一边，这里会红。
public class ParityTests
{
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("找不到仓库里的 " + relative);
    }

    private static double JsNumber(string js, string name)
    {
        var m = Regex.Match(js, @"^var " + name + @"\s*=\s*([0-9.]+)\s*;", RegexOptions.Multiline);
        Assert.True(m.Success, "layout.js 里没有 " + name);
        return double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void CapacityConstantsMatchLinux()
    {
        var js = RepoFile(Path.Combine("linux", "layout.js"));
        Assert.Equal((double)LineCapacity.Spacing, JsNumber(js, "Spacing"));
        Assert.Equal((double)LineCapacity.MinItems, JsNumber(js, "MinItems"));
        Assert.Equal((double)LineCapacity.MaxItemsCap, JsNumber(js, "MaxItemsCap"));
    }

    [Fact]
    public void TriggerZoneMatchesLinux()
    {
        var js = RepoFile(Path.Combine("linux", "layout.js"));
        Assert.Equal(LineCapacity.CardWidth, JsNumber(js, "CardWidth"));
        Assert.Equal(TriggerZone.MinHalf, JsNumber(js, "TriggerMinHalf"));
        Assert.Equal(TriggerZone.Margin, JsNumber(js, "TriggerMargin"));
    }

    [Fact]
    public void CollectModesMatchLinux()
    {
        var py = RepoFile(Path.Combine("linux", "xitu_helper.py"));
        var m = Regex.Match(py, @"^COLLECT_MODES\s*=\s*\(([^)]*)\)", RegexOptions.Multiline);
        Assert.True(m.Success, "xitu_helper.py 里没有 COLLECT_MODES");
        var linux = Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"").Select(x => x.Groups[1].Value).ToArray();
        var windows = CollectMode.Options.Select(o => o.Value).ToArray();
        Assert.Equal(linux, windows);
    }

    [Fact]
    public void HtmlRuleMatchesLinux()
    {
        // 两端判断「HTML 里只有一张图」用的是同样的正则
        var py = RepoFile(Path.Combine("linux", "xitu_helper.py"));
        Assert.Contains("r\"<img\\b\"", py);
        Assert.Contains("r\"<(table|td|tr)\\b\"", py);
        Assert.Contains("&nbsp;|&#160;", py);
    }
}
