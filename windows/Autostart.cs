using Microsoft.Win32;

namespace Clothesline;

internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Xitu";
    private const string LegacyName = "Clothesline";

    public static void ClearLegacy()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;
        if (key.GetValue(LegacyName) == null) return;
        key.DeleteValue(LegacyName, throwOnMissingValue: false);
        Log.Info("已去掉旧的开机自启项 Clothesline");
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool on)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.DeleteValue(LegacyName, throwOnMissingValue: false);
        if (on)
        {
            var exe = Environment.ProcessPath ?? "";
            key.SetValue(ValueName, $"\"{exe}\"");
            Log.Info("开机自启已打开");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            Log.Info("开机自启已关闭");
        }
    }
}
