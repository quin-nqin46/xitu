using System.Text.RegularExpressions;

namespace Clothesline;

/// 设置 Collect 的取值，和 Linux 版 xitu_helper.collect_mode 一致。拖进来的不受影响。
internal static class CollectMode
{
    /// 截图文件夹、剪贴板截图、复制的图片（如浏览器「复制图片」）都收。默认。
    public const string All = "all";
    /// 只收截图文件夹和剪贴板截图，复制的图片不收。
    public const string Screenshots = "screenshots";
    /// 只收系图自己截的图（截图快捷键）；截图文件夹和剪贴板都不看。
    public const string Own = "own";

    public static readonly (string Value, string Label)[] Options =
    {
        (All, "截图和复制的图片"),
        (Screenshots, "只要截图"),
        (Own, "只要系图自己截的图"),
    };

    public static string Normalize(string? value) => value is All or Screenshots or Own ? value : All;
}

/// 剪贴板上有哪些格式。由 ScreenshotWatcher 从 Win32 取，这里不碰 Win32，方便单测。
internal readonly record struct ClipboardFormats(bool Bitmap, bool FileDrop, bool Rtf, bool Html, bool Text);

internal enum ClipboardVerdict
{
    NoBitmap,
    /// 只有位图：截图工具放的。收。
    Screenshot,
    /// HTML 里只有一张图：浏览器「复制图片」。收。
    CopiedImage,
    /// 是复制的图片，但 Collect 不是 all。
    CopiedImageOff,
    /// Collect 为 own：剪贴板一概不看。
    OwnOnly,
    FileCopy,
    RichText,
    TextOrTable,
}

/// 剪贴板位图收不收。规则和 Linux 版 xitu_helper.clip_should_take 一致：
/// 只有位图的收；带文件列表（复制文件）、RTF（Word/WPS）的不收；
/// 带 HTML（可能还带文字）的，HTML 里只有一张图才算复制的图片，Collect 为 all 时收；
/// HTML 里有表格或文字（Excel/WPS 复制单元格）、或只带文字没有 HTML 的不收。
internal static class ClipboardRule
{
    public static ClipboardVerdict Decide(ClipboardFormats f, string collect, Func<string> readHtml)
    {
        if (!f.Bitmap) return ClipboardVerdict.NoBitmap;
        if (collect == CollectMode.Own) return ClipboardVerdict.OwnOnly;
        if (f.FileDrop) return ClipboardVerdict.FileCopy;
        if (f.Rtf) return ClipboardVerdict.RichText;
        if (!f.Html && !f.Text) return ClipboardVerdict.Screenshot;
        if (!f.Html || !HtmlIsLoneImage(readHtml())) return ClipboardVerdict.TextOrTable;
        return collect == CollectMode.All ? ClipboardVerdict.CopiedImage : ClipboardVerdict.CopiedImageOff;
    }

    public static bool Takes(ClipboardVerdict v) => v is ClipboardVerdict.Screenshot or ClipboardVerdict.CopiedImage;

    public static string Describe(ClipboardVerdict v) => v switch
    {
        ClipboardVerdict.CopiedImage => "复制的图片，挂上",
        ClipboardVerdict.CopiedImageOff => "复制的图片，设置为只要截图，忽略",
        ClipboardVerdict.OwnOnly => "设置为只要系图自己截的图，忽略",
        ClipboardVerdict.FileCopy => "同时有文件列表和位图，按文件复制忽略",
        ClipboardVerdict.RichText => "位图附带 RTF，按复制内容忽略",
        ClipboardVerdict.TextOrTable => "位图附带文字/表格，按复制内容忽略",
        ClipboardVerdict.Screenshot => "截图",
        _ => "没有位图",
    };

    private static readonly Regex ImgTag = new(@"<img\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TableTag = new(@"<(table|td|tr)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AnyTag = new(@"<[^>]*>", RegexOptions.CultureInvariant);
    private static readonly Regex Nbsp = new(@"&nbsp;|&#160;", RegexOptions.CultureInvariant);

    /// HTML 里只有一张图、没有表格和文字。CF_HTML 开头的 Version:/StartHTML: 头不算正文。
    public static bool HtmlIsLoneImage(string html)
    {
        var start = html.IndexOf('<');
        if (start < 0) return false;
        var body = html[start..];
        if (!ImgTag.IsMatch(body) || TableTag.IsMatch(body)) return false;
        var rest = Nbsp.Replace(AnyTag.Replace(body, ""), "");
        return rest.Trim().Length == 0;
    }
}
