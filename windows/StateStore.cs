using System.IO;
using System.Text.Json;

namespace Clothesline;

internal static class StateStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(AppPaths.StateFile)) return Array.Empty<string>();
            var file = JsonSerializer.Deserialize<StateFile>(File.ReadAllText(AppPaths.StateFile));
            return file?.Paths ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (Exception ex)
        {
            Log.Error("读取 state.json 失败: " + ex.Message);
            return Array.Empty<string>();
        }
    }

    public static void Save(IEnumerable<string> paths)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var json = JsonSerializer.Serialize(new StateFile { Paths = paths.ToList() }, Json);
            var tmp = AppPaths.StateFile + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, AppPaths.StateFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("写入 state.json 失败: " + ex.Message);
        }
    }

    private sealed class StateFile
    {
        public List<string> Paths { get; set; } = new();
    }
}
