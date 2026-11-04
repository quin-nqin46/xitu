using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Clothesline;

internal sealed class Pegged
{
    public Pegged(string path, ImageSource thumb, double pixelWidth, double pixelHeight, bool flying = false)
    {
        Path = path;
        Thumb = thumb;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Tilt = Random.Shared.NextDouble() * 5 - 2.5;
        IsFlying = flying;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Path { get; private set; }

    public void SetPath(string path) => Path = path;
    public ImageSource Thumb { get; private set; }
    public double PixelWidth { get; private set; }
    public double PixelHeight { get; private set; }

    /// 静止时的微倾斜（度）。摆动叠在上面。
    public double Tilt { get; }

    /// 截图正在飞向绳子，卡片先藏着，落地后再轻轻摆一下。
    public bool IsFlying { get; private set; }

    public void StopFlying() => IsFlying = false;

    public void ReplaceThumb(ImageSource thumb, double pixelWidth, double pixelHeight)
    {
        Thumb = thumb;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }
}

/// 绳上有哪些照片。文件本身不移动。
internal sealed class Line
{
    private readonly List<Pegged> _items = new();
    private DispatcherTimer? _prune;

    public event EventHandler? Changed;

    /// 照片已经从列表拿掉。视图应接着播下落，播完再从界面移除。
    public event Action<Pegged>? Removing;

    public event Action<Guid>? Copied;

    /// 收藏或搬家改了路径，视图刷新星标。
    public event Action<Pegged>? Relocated;

    /// 飞行动画结束，卡片可以出现。
    public event Action<Guid>? Landed;

    public IReadOnlyList<Pegged> Items => _items;

    public int MaxItems { get; set; } = 8;

    public int Count => _items.Count;

    public void StartPruneTimer(Dispatcher dispatcher)
    {
        _prune = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Prune(), dispatcher);
        _prune.Start();
    }

    public void Restore()
    {
        var paths = StateStore.Load();
        var kept = 0;
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                Log.Info("恢复时文件已不在，跳过 " + path);
                continue;
            }
            string lib;
            try { lib = Library.Ensure(path); }
            catch (Exception ex)
            {
                Log.Error("恢复时无法进图库: " + ex.Message);
                continue;
            }
            if (Hang(lib, quiet: true)) kept++;
        }
        Log.Info($"从 state.json 恢复 {kept} 张");
    }

    public void Save() => StateStore.Save(_items.Select(i => i.Path));

    public bool Hang(string path, bool quiet = false) => TryHang(path, quiet, flying: false) != null;

    /// flying 为真时卡片先隐藏，等 <see cref="Land"/>。
    public Guid? TryHang(string path, bool quiet = false, bool flying = false)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex)
        {
            Log.Error("路径无效: " + ex.Message);
            return null;
        }

        if (_items.Any(i => string.Equals(i.Path, full, StringComparison.OrdinalIgnoreCase)))
        {
            if (!quiet) Log.Info("已在绳上，跳过 " + full);
            return null;
        }

        if (!TryLoadThumb(full, out var thumb, out var w, out var h) || thumb == null)
            return null;

        var item = new Pegged(full, thumb, w, h, flying);
        _items.Add(item);
        while (_items.Count > MaxItems)
        {
            var oldest = _items[0];
            TakeOff(oldest);
            Log.Info("超出容量，取下最老的 " + oldest.Path);
        }

        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        if (!quiet) Log.Info("挂上 " + full);
        return _items.Contains(item) ? item.Id : null;
    }

    public void Land(Guid id)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item == null || !item.IsFlying) return;
        item.StopFlying();
        Landed?.Invoke(id);
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        foreach (var item in _items.ToList()) TakeOff(item);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        Log.Info("清空绳子");
    }

    /// 从绳上拿掉，不删文件。视图播放下落后再消失。
    public void Remove(Guid id)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item == null) return;
        TakeOff(item);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        Log.Info("取下 " + item.Path);
    }

    /// 点叉或菜单「取下」。文件留在图库。
    public void Discard(Guid id) => Remove(id);

    public void RemoveByPath(string path)
    {
        var item = _items.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        if (item != null) Remove(item.Id);
    }

    public void NotifyCopied(Guid id) => Copied?.Invoke(id);

    /// 在绳上拖动换位置。
    public void Move(Guid id, int index)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item == null) return;
        index = Math.Clamp(index, 0, _items.Count - 1);
        var from = _items.IndexOf(item);
        if (from == index) return;
        _items.RemoveAt(from);
        _items.Insert(index, item);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        Log.Info($"换位置 {from} → {index} {item.Path}");
    }

    /// 收藏或搬家时更新绳上这条的路径。
    public void Relocate(string oldPath, string newPath)
    {
        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)) return;
        var item = _items.FirstOrDefault(i => string.Equals(i.Path, oldPath, StringComparison.OrdinalIgnoreCase));
        if (item == null) return;
        item.SetPath(newPath);
        Save();
        Relocated?.Invoke(item);
        Changed?.Invoke(this, EventArgs.Empty);
        Log.Info("绳上路径改为 " + newPath);
    }

    /// 文件被删或移走时从绳上拿掉，并播放下落。
    public void Prune()
    {
        var gone = _items.Where(i => !File.Exists(i.Path)).ToList();
        if (gone.Count == 0) return;
        foreach (var item in gone)
        {
            TakeOff(item);
            Log.Info("文件不见了，取下 " + item.Path);
        }
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void TakeOff(Pegged item)
    {
        _items.Remove(item);
        Removing?.Invoke(item);
    }

    /// 单击复制：位图写入剪贴板之后调用，截图监听会跳过这一次更新。
    public void NoteOwnClipboardWrite() => ClipboardGuard.IgnoreCurrentSequence();

    /// 编辑保存后刷新缩略图。
    public void ReloadThumbnail(string path)
    {
        var item = _items.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        if (item == null) return;
        if (!TryLoadThumb(item.Path, out var thumb, out var w, out var h) || thumb == null) return;
        item.ReplaceThumb(thumb, w, h);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool TryLoadThumb(string path, out ImageSource? thumb, out double width, out double height)
    {
        thumb = null;
        width = 0;
        height = 0;
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.StreamSource = stream;
            bmp.DecodePixelWidth = 480;
            bmp.EndInit();
            bmp.Freeze();
            thumb = bmp;
            width = bmp.PixelWidth;
            height = bmp.PixelHeight;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("读图失败 " + path + ": " + ex.Message);
            return false;
        }
    }
}
