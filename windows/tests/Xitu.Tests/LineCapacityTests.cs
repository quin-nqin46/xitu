using Xunit;

namespace Clothesline.Tests;

public class LineCapacityTests
{
    [Theory]
    [InlineData(0, 3)]          // 太窄也至少 3 张
    [InlineData(1280, 6)]       // (1280-200)/174 = 6.2
    [InlineData(1366, 6)]
    [InlineData(1920, 9)]       // 9.9 → 9
    [InlineData(2560, 12)]      // 13.5 → 上限 12
    [InlineData(3840, 12)]
    public void ForWidth(double width, int expected)
    {
        Assert.Equal(expected, LineCapacity.ForWidth(width));
    }

    [Fact]
    public void ForScreensUsesNarrowest()
    {
        // 笔记本 1280 + 外接 2560：按 1280 算，免得笔记本上两头挂出屏幕
        Assert.Equal(6, LineCapacity.ForScreens(new double[] { 2560, 1280 }));
    }

    [Fact]
    public void ForScreensWithNoScreensIsCap()
    {
        Assert.Equal(LineCapacity.MaxItemsCap, LineCapacity.ForScreens(Array.Empty<double>()));
    }
}
