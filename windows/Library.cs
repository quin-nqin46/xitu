// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Clothesline;

internal enum LibrarySource
{
    Capture,
    Clipboard,
    Drop,
    File,
    Other,
}

internal sealed class LibraryItem
{
    public required string Path { get; init; }
    public required LibrarySource Source { get; init; }
    public required DateTime Taken { get; init; }
    public required bool Favorite { get; init; }
    public required long Bytes { get; init; }
}

/// 图库：文件系统是唯一数据源。绳子上挂的都是这里的副本。
internal static class Library
{
    public const string FavoritesName = "收藏";
    private static readonly Regex NamePattern = new(
        @"^(截图|剪贴板|拖入|文件|其他)_(\d{8})_(\d{6})(?:_(\d+))?\.png$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> HashToPath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> PathToHash = new(StringComparer.OrdinalIgnoreCase);
    private static bool _scanned;
    private static FileSystemWatcher? _watch;
    private static DispatcherTimer? _debounce;
    private static Dispatcher? _dispatcher;

    public static event Action? Changed;

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "系图");

    public static string Root
    {
        get
        {
            var set = AppSettings.Current.LibraryPath;
            return string.IsNullOrWhiteSpace(set) ? DefaultRoot : set;
        }
    }

    public static string FavoritesDir => Path.Combine(Root, FavoritesName);

    public static void Start(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(FavoritesDir);
        MigrateInbox();
        Task.Run(EnsureScan);
        Watch(dispatcher);
    }

    public static void ScanNow() => EnsureScan();

    public static string ImportBytes(byte[] png, LibrarySource source, DateTime? when = null)
    {
        EnsureScan();
        var hash = Hash(png);
        lock (Gate)
        {
            if (HashToPath.TryGetValue(hash, out var existing) && File.Exists(existing))
            {
                Log.Info("图库已有相同内容，复用 " + existing);
                return existing;
            }
        }
        var time = when ?? DateTime.Now;
        var path = UniquePath(MonthDir(time), source, time);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, png);
        Remember(hash, path);
        Log.Info("写入图库 " + path);
        RaiseChanged();
        return path;
    }

    public static string ImportFile(string file, LibrarySource source)
    {
        var full = Path.GetFullPath(file);
        if (IsUnderRoot(full)) return full;
        byte[] bytes;
        try { bytes = File.ReadAllBytes(full); }
        catch (Exception ex)
        {
            Log.Error("读原文件失败: " + ex.Message);
            throw;
        }
        if (!LooksPng(bytes))
        {
            var png = ToPng(bytes);
            if (png == null) throw new InvalidOperationException("不是图片");
            return ImportBytes(png, source, File.GetLastWriteTime(full));
        }
        return ImportBytes(bytes, source, File.GetLastWriteTime(full));
    }

    /// 已在图库就原样返回，否则复制进来。
    public static string Ensure(string file) => ImportFile(file, LibrarySource.File);

    public static IReadOnlyList<LibraryItem> List()
    {
        var root = Root;
        if (!Directory.Exists(root)) return Array.Empty<LibraryItem>();
        var items = new List<LibraryItem>();
        foreach (var file in OwnedFiles(root))
        {
            try { items.Add(Parse(file)); }
            catch { /* 跳过读不出的 */ }
        }
        return items;
    }

    public static LibraryItem Parse(string file)
    {
        var full = Path.GetFullPath(file);
        var info = new FileInfo(full);
        var favorite = IsFavorite(full);
        var name = Path.GetFileName(full);
        var match = NamePattern.Match(name);
        LibrarySource source;
        DateTime taken;
        if (match.Success)
        {
            source = FromLabel(match.Groups[1].Value);
            var stamp = match.Groups[2].Value + match.Groups[3].Value;
            taken = DateTime.TryParseExact(stamp, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var parsed)
                ? parsed
                : info.LastWriteTime;
        }
        else
        {
            source = LibrarySource.Other;
            taken = info.LastWriteTime;
        }
        return new LibraryItem
        {
            Path = full,
            Source = source,
            Taken = taken,
            Favorite = favorite,
            Bytes = info.Exists ? info.Length : 0,
        };
    }

    public static bool IsFavorite(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var fav = Path.GetFullPath(FavoritesDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full.StartsWith(fav + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static bool IsUnderRoot(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(Root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static string SetFavorite(string path, bool favorite)
    {
        var full = Path.GetFullPath(path);
        if (favorite == IsFavorite(full)) return full;
        var item = Parse(full);
        string destDir;
        if (favorite)
        {
            destDir = FavoritesDir;
        }
        else
        {
            destDir = MonthDir(item.Taken);
        }
        Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, Path.GetFileName(full));
        if (File.Exists(dest))
            dest = UniquePath(destDir, item.Source, item.Taken);
        File.Move(full, dest);
        RelocateHash(full, dest);
        Log.Info((favorite ? "收藏 " : "取消收藏 ") + dest);
        RaiseChanged();
        return dest;
    }

    private static readonly Regex MonthFolder = new(@"^\d{4}-\d{2}$", RegexOptions.CultureInvariant);

    /// 系图自己的文件：在图库根下的 yyyy-MM 或「收藏」文件夹里，且文件名是系图的命名格式。
    /// 图库位置可能被设成「图片」「桌面」这类大文件夹，列表、统计、搬家、清理都只碰这些，别的文件一概不动。
    public static IEnumerable<string> OwnedFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(root).ToList(); }
        catch { yield break; }
        foreach (var dir in dirs)
        {
            var name = Path.GetFileName(dir);
            if (name != FavoritesName && !MonthFolder.IsMatch(name)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.png", SearchOption.TopDirectoryOnly).ToList(); }
            catch { continue; }
            foreach (var file in files)
                if (NamePattern.IsMatch(Path.GetFileName(file))) yield return file;
        }
    }

    public static bool IsOwned(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(full);
            if (dir == null || !NamePattern.IsMatch(Path.GetFileName(full))) return false;
            var folder = Path.GetFileName(dir);
            if (folder != FavoritesName && !MonthFolder.IsMatch(folder)) return false;
            var root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(Path.GetDirectoryName(dir), root, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static bool Recycle(string path)
    {
        if (!IsOwned(path))
        {
            Log.Error("不是图库自己的文件，拒绝删除 " + path);
            return false;
        }
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            Forget(path);
            Log.Info("图库进回收站 " + path);
            RaiseChanged();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("无法放入回收站: " + ex.Message);
            return false;
        }
    }

    public static long OccupiedBytes()
    {
        if (!Directory.Exists(Root)) return 0;
        long sum = 0;
        foreach (var file in OwnedFiles(Root))
        {
            try { sum += new FileInfo(file).Length; }
            catch { }
        }
        return sum;
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0") + " KB";
        return (bytes / (1024.0 * 1024.0)).ToString("0") + " MB";
    }

    public static string Label(LibrarySource source) => source switch
    {
        LibrarySource.Capture => "截图",
        LibrarySource.Clipboard => "剪贴板",
        LibrarySource.Drop => "拖入",
        LibrarySource.File => "文件",
        _ => "其他",
    };

    public static void ChangeRoot(string newRoot, bool move, Line line)
    {
        var oldRoot = Root;
        var dest = Path.GetFullPath(newRoot);
        Directory.CreateDirectory(dest);
        Directory.CreateDirectory(Path.Combine(dest, FavoritesName));
        if (move && Directory.Exists(oldRoot) && !string.Equals(oldRoot, dest, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var file in OwnedFiles(oldRoot).ToList())
            {
                var rel = Path.GetRelativePath(oldRoot, file);
                var target = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                // 重名按 _2、_3… 另起，和导入一样；加随机后缀的话名字不再合 NamePattern，搬过去就不算系图的文件了
                if (File.Exists(target))
                {
                    var item = Parse(file);
                    target = UniquePath(Path.GetDirectoryName(target)!, item.Source, item.Taken);
                }
                File.Move(file, target);
                line.Relocate(file, target);
            }
        }
        AppSettings.Current.LibraryPath = dest;
        AppSettings.Current.Save();
        lock (Gate)
        {
            HashToPath.Clear();
            PathToHash.Clear();
            _scanned = false;
        }
        Watch(_dispatcher);
        EnsureScan();
        RaiseChanged();
        Log.Info("图库位置 " + dest);
    }

    /// 旧 Inbox 里的图按修改时间搬进图库，并改写 state.json。
    public static void MigrateInbox()
    {
        var inbox = AppPaths.Inbox;
        if (!Directory.Exists(inbox)) return;
        string[] files;
        try { files = Directory.GetFiles(inbox); }
        catch { return; }
        if (files.Length == 0) return;

        var state = StateStore.Load().ToList();
        var moved = 0;
        foreach (var file in files)
        {
            try
            {
                var name = Path.GetFileName(file);
                var source = InferLegacy(name);
                var dest = ImportFile(file, source);
                for (var i = 0; i < state.Count; i++)
                {
                    if (string.Equals(state[i], file, StringComparison.OrdinalIgnoreCase))
                        state[i] = dest;
                }
                // 内容已复制进图库；旧文件也进回收站，不直接删。
                if (!string.Equals(file, dest, StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                moved++;
            }
            catch (Exception ex)
            {
                Log.Error("迁移 Inbox 失败 " + file + ": " + ex.Message);
            }
        }
        StateStore.Save(state);
        Log.Info($"已把 Inbox 里 {moved} 张移进图库");
    }

    private static LibrarySource InferLegacy(string name)
    {
        if (name.StartsWith("截图_", StringComparison.OrdinalIgnoreCase)) return LibrarySource.Capture;
        if (name.StartsWith("拖入_", StringComparison.OrdinalIgnoreCase)) return LibrarySource.Drop;
        return LibrarySource.Clipboard;
    }

    private static LibrarySource FromLabel(string label) => label switch
    {
        "截图" => LibrarySource.Capture,
        "剪贴板" => LibrarySource.Clipboard,
        "拖入" => LibrarySource.Drop,
        "文件" => LibrarySource.File,
        _ => LibrarySource.Other,
    };

    private static string MonthDir(DateTime time) => Path.Combine(Root, time.ToString("yyyy-MM"));

    private static string UniquePath(string dir, LibrarySource source, DateTime time)
    {
        var stamp = time.ToString("yyyyMMdd_HHmmss");
        var label = Label(source);
        var path = Path.Combine(dir, $"{label}_{stamp}.png");
        if (!File.Exists(path)) return path;
        for (var n = 2; n < 1000; n++)
        {
            path = Path.Combine(dir, $"{label}_{stamp}_{n}.png");
            if (!File.Exists(path)) return path;
        }
        return Path.Combine(dir, $"{label}_{stamp}_{DateTime.Now.Ticks}.png");
    }

    private static void EnsureScan()
    {
        if (_scanned) return;
        lock (Gate)
        {
            if (_scanned) return;
            HashToPath.Clear();
            PathToHash.Clear();
            if (Directory.Exists(Root))
            {
                foreach (var file in OwnedFiles(Root))
                {
                    try
                    {
                        var hash = Hash(File.ReadAllBytes(file));
                        HashToPath[hash] = file;
                        PathToHash[file] = hash;
                    }
                    catch { }
                }
            }
            _scanned = true;
            Log.Info("图库哈希 " + HashToPath.Count + " 张");
        }
    }

    private static void Remember(string hash, string path)
    {
        lock (Gate)
        {
            HashToPath[hash] = path;
            PathToHash[path] = hash;
        }
    }

    private static void Forget(string path)
    {
        lock (Gate)
        {
            if (PathToHash.Remove(path, out var hash))
                HashToPath.Remove(hash);
        }
    }

    private static void RelocateHash(string oldPath, string newPath)
    {
        lock (Gate)
        {
            if (PathToHash.Remove(oldPath, out var hash))
            {
                PathToHash[newPath] = hash;
                HashToPath[hash] = newPath;
            }
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static bool LooksPng(byte[] bytes) =>
        bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

    private static byte[]? ToPng(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return null;
            var frame = decoder.Frames[0];
            BitmapSource source = frame;
            if (source.Format != PixelFormats.Bgra32 && source.Format != PixelFormats.Pbgra32 && source.Format != PixelFormats.Bgr32)
                source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch { return null; }
    }

    private static void Watch(Dispatcher? dispatcher)
    {
        _watch?.Dispose();
        _watch = null;
        if (dispatcher == null) return;
        try
        {
            Directory.CreateDirectory(Root);
            var watcher = new FileSystemWatcher(Root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            if (_debounce == null)
            {
                _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _debounce.Tick += (_, _) =>
                {
                    _debounce.Stop();
                    lock (Gate) _scanned = false;
                    Task.Run(() =>
                    {
                        EnsureScan();
                        dispatcher.BeginInvoke(RaiseChanged);
                    });
                };
            }
            FileSystemEventHandler ping = (_, _) =>
            {
                dispatcher.BeginInvoke(() =>
                {
                    _debounce.Stop();
                    _debounce.Start();
                });
            };
            watcher.Created += ping;
            watcher.Deleted += ping;
            watcher.Renamed += (_, _) => ping(null!, null!);
            watcher.Changed += ping;
            watcher.EnableRaisingEvents = true;
            _watch = watcher;
        }
        catch (Exception ex)
        {
            Log.Error("监听图库失败: " + ex.Message);
        }
    }

    private static void RaiseChanged() => Changed?.Invoke();
}
