using Xunit;

namespace Clothesline.Tests;

public class TriggerZoneTests
{
    [Theory]
    // 没照片 / 一张：中间 320 宽
    [InlineData(0, 1920, 800, 1120)]
    [InlineData(1, 1920, 800, 1120)]
    // 三张：(2×174)/2 + 75 + 14 = 263
    [InlineData(3, 1920, 697, 1223)]
    // 十张：(9×174)/2 + 89 = 872，在 1920 宽里是 88..1832
    [InlineData(10, 1920, 88, 1832)]
    // 屏比感应段还窄：截到屏幕两边
    [InlineData(12, 1280, 0, 1280)]
    public void SpanIsCenteredOnTheCards(int count, double width, double left, double right)
    {
        var (l, r) = TriggerZone.Span(count, width);
        Assert.Equal(left, l, 6);
        Assert.Equal(right, r, 6);
    }

    [Fact]
    public void ZeroWidthHasNoSpan()
    {
        Assert.Equal((0d, 0d), TriggerZone.Span(3, 0));
    }

    [Theory]
    [InlineData(960, 0, true)]      // 正中贴顶
    [InlineData(960, 3.9, true)]
    [InlineData(960, 4, false)]     // 窄带以下，比如浏览器标签页
    [InlineData(960, 15, false)]    // 以前的 20 DIP 窄带里，现在不算
    [InlineData(100, 0, false)]     // 左上角（菜单、标签页）
    [InlineData(1900, 0, false)]    // 右上角（关闭按钮）
    [InlineData(800, 0, true)]      // 左端含
    [InlineData(1120, 0, false)]    // 右端不含
    [InlineData(960, -1, false)]
    public void ContainsOnlyTheCenterOfTheTopEdge(double x, double y, bool expected)
    {
        Assert.Equal(expected, TriggerZone.Contains(0, 1920, x, y));
    }
}
