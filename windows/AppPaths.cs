using System.IO;

namespace Clothesline;

internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Xitu");

    public static string LogFile => Path.Combine(Root, "log.txt");
    public static string StateFile => Path.Combine(Root, "state.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    /// 剪贴板位图推迟落盘后写到这里。目录在第一次写入时创建。
    public static string Inbox => Path.Combine(Root, "Inbox");
}
