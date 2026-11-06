using Xunit;

namespace Clothesline.Tests;

/// 输入和 linux/tests/test_library.py 的剪贴板用例一一对应，两端规则不能分叉。
public class ClipboardRuleTests
{
    private const string BrowserHtml = "<meta charset=\"utf-8\"><img src=\"https://example.com/a.png\">&nbsp;";

    // Chrome/Edge「复制图片」放进 CF_HTML 的样子：先是 Version/StartHTML 头，再是 HTML
    private const string ChromeCfHtml =
        "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000219\r\nStartFragment:0000000141\r\nEndFragment:0000000183\r\n" +
        "<html><body>\r\n<!--StartFragment--><img src=\"https://example.com/a.png\"/><!--EndFragment-->\r\n</body></html>";

    private const string ExcelHtml = "<table><tr><td>1</td></tr></table>";

    private static readonly ClipboardFormats BitmapOnly = new(Bitmap: true, FileDrop: false, Rtf: false, Html: false, Text: false);

    private static ClipboardVerdict Decide(ClipboardFormats f, string collect = CollectMode.All, string html = "")
        => ClipboardRule.Decide(f, collect, () => html);

    [Fact]
    public void BitmapOnlyIsScreenshot()
    {
        Assert.Equal(ClipboardVerdict.Screenshot, Decide(BitmapOnly));
        Assert.True(ClipboardRule.Takes(Decide(BitmapOnly)));
    }

    [Fact]
    public void NoBitmapIsIgnored()
    {
        var textOnly = BitmapOnly with { Bitmap = false, Text = true };
        Assert.Equal(ClipboardVerdict.NoBitmap, Decide(textOnly));
    }

    [Theory]
    [InlineData(BrowserHtml, false)]
    [InlineData(BrowserHtml, true)]  // Firefox 还会带一份纯文字
    [InlineData(ChromeCfHtml, false)]
    public void BrowserCopyImageHangsByDefault(string html, bool text)
    {
        var f = BitmapOnly with { Html = true, Text = text };
        Assert.Equal(ClipboardVerdict.CopiedImage, Decide(f, html: html));
        Assert.True(ClipboardRule.Takes(Decide(f, html: html)));
    }

    [Fact]
    public void BrowserCopyImageSkippedInScreenshotsMode()
    {
        var f = BitmapOnly with { Html = true };
        var v = Decide(f, CollectMode.Screenshots, BrowserHtml);
        Assert.Equal(ClipboardVerdict.CopiedImageOff, v);
        Assert.False(ClipboardRule.Takes(v));
    }

    [Fact]
    public void ScreenshotsModeStillTakesPlainBitmap()
    {
        Assert.True(ClipboardRule.Takes(Decide(BitmapOnly, CollectMode.Screenshots)));
    }

    [Fact]
    public void OwnModeTakesNothingFromClipboard()
    {
        var v = Decide(BitmapOnly, CollectMode.Own);
        Assert.Equal(ClipboardVerdict.OwnOnly, v);
        Assert.False(ClipboardRule.Takes(v));
    }

    [Fact]
    public void ExcelCellCopyIsIgnored()
    {
        var f = BitmapOnly with { Html = true, Text = true };
        Assert.Equal(ClipboardVerdict.TextOrTable, Decide(f, html: ExcelHtml));
    }

    [Fact]
    public void TextWithoutHtmlIsIgnored()
    {
        Assert.Equal(ClipboardVerdict.TextOrTable, Decide(BitmapOnly with { Text = true }, html: BrowserHtml));
    }

    [Fact]
    public void RtfIsIgnoredEvenWithLoneImageHtml()
    {
        var f = BitmapOnly with { Html = true, Rtf = true };
        Assert.Equal(ClipboardVerdict.RichText, Decide(f, html: BrowserHtml));
    }

    [Fact]
    public void FileCopyIsIgnored()
    {
        Assert.Equal(ClipboardVerdict.FileCopy, Decide(BitmapOnly with { FileDrop = true }));
    }

    [Fact]
    public void HtmlIsNotReadUnlessNeeded()
    {
        var reads = 0;
        ClipboardRule.Decide(BitmapOnly, CollectMode.All, () => { reads++; return ""; });
        Assert.Equal(0, reads);
    }

    [Theory]
    [InlineData(BrowserHtml, true)]
    [InlineData(ChromeCfHtml, true)]
    [InlineData("<IMG SRC=x>", true)]
    [InlineData(ExcelHtml, false)]
    [InlineData("<p>hello</p><img src=x>", false)]
    [InlineData("<p>no image</p>", false)]
    [InlineData("", false)]
    [InlineData("Version:0.9\r\nStartHTML:0", false)]
    public void HtmlIsLoneImage(string html, bool expected)
    {
        Assert.Equal(expected, ClipboardRule.HtmlIsLoneImage(html));
    }

    [Theory]
    [InlineData(null, CollectMode.All)]
    [InlineData("", CollectMode.All)]
    [InlineData("bogus", CollectMode.All)]
    [InlineData("all", CollectMode.All)]
    [InlineData("screenshots", CollectMode.Screenshots)]
    [InlineData("own", CollectMode.Own)]
    public void CollectModeNormalize(string? input, string expected)
    {
        Assert.Equal(expected, CollectMode.Normalize(input));
    }
}
