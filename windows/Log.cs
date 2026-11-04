using System.IO;
using System.Text;

namespace Clothesline;

internal static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.AppendAllText(AppPaths.LogFile, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志写失败时不再抛，避免托盘进程因此退出。
        }
    }
}
