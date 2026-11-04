namespace Clothesline;

/// 区分「用户新截了一张图」和「程序自己把图片写进剪贴板」。
/// 单击复制：写入剪贴板之后立刻调用 <see cref="IgnoreCurrentSequence"/>。
internal static class ClipboardGuard
{
    private static uint _ignoredSequence;
    private static int _armed;

    /// 写剪贴板之前调用。更新通知若在写入过程中同步到达，也能认出来。
    public static void BeginOwnWrite() => _armed++;

    public static void CancelOwnWrite()
    {
        if (_armed > 0) _armed--;
    }

    public static void IgnoreCurrentSequence()
    {
        _ignoredSequence = NativeMethods.GetClipboardSequenceNumber();
    }

    /// 这次 WM_CLIPBOARDUPDATE 是不是刚才自己写入的。命中后只忽略一次。
    public static bool ConsumeIfOwnUpdate()
    {
        if (_armed > 0)
        {
            _armed--;
            _ignoredSequence = 0;
            return true;
        }
        if (_ignoredSequence == 0) return false;
        var now = NativeMethods.GetClipboardSequenceNumber();
        if (now != _ignoredSequence) return false;
        _ignoredSequence = 0;
        return true;
    }
}
