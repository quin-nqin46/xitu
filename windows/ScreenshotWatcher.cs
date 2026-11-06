using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Clothesline;

/// 监视截图文件夹，并监听剪贴板里的位图。文件夹那张优先。
internal sealed class ScreenshotWatcher : IDisposable
{
    private const string ShellFoldersKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
    private const string ScreenshotsGuid = "{B7BEDE81-DF94-4682-A7D8-57A52620B86F}";

    private static readonly TimeSpan ReadInterval = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan FolderPoll = TimeSpan.FromSeconds(2);
    private static readonly uint HtmlFormat = NativeMethods.RegisterClipboardFormat("HTML Format");
    private static readonly uint RtfFormat = NativeMethods.RegisterClipboardFormat("Rich Text Format");

    private readonly Line _line;
    private readonly Action _peek;
    private readonly Dispatcher _dispatcher;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lastLength = new(StringComparer.OrdinalIgnoreCase);

    private DispatcherTimer? _poll;
    private FileSystemWatcher? _watcher;
    private string? _folder;
    private HwndSource? _clipboardWindow;
    private bool _listeningClipboard;
    private bool _disposed;

    private PendingClip? _pendingClip;
    private RecentFolderShot? _recentFolder;

    public ScreenshotWatcher(Line line, Action peek, Dispatcher dispatcher)
    {
        _line = line;
        _peek = peek;
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        _folder = ResolveScreenshotFolder();
        var exists = Directory.Exists(_folder);
        Log.Info("截图文件夹 " + _folder + (exists ? "" : "（尚未创建，等待出现）"));
        if (exists) AttachFolderWatcher();

        _poll = new DispatcherTimer(FolderPoll, DispatcherPriority.Background, (_, _) => PollFolder(), _dispatcher);
        _poll.Start();
        ListenClipboard();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _poll?.Stop();
        DetachFolderWatcher();
        CancelPendingClip();
        if (_listeningClipboard && _clipboardWindow != null)
        {
            NativeMethods.RemoveClipboardFormatListener(_clipboardWindow.Handle);
            _listeningClipboard = false;
        }
        if (_clipboardWindow != null)
        {
            _clipboardWindow.RemoveHook(ClipboardHook);
            _clipboardWindow.Dispose();
            _clipboardWindow = null;
        }
    }

    private static string ResolveScreenshotFolder()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ShellFoldersKey);
            var raw = key?.GetValue(ScreenshotsGuid) as string;
            if (!string.IsNullOrWhiteSpace(raw))
                return Environment.ExpandEnvironmentVariables(raw);
        }
        catch (Exception ex)
        {
            Log.Error("读取截图文件夹注册表失败: " + ex.Message);
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, "Pictures", "Screenshots");
    }

    private void PollFolder()
    {
        if (_disposed || string.IsNullOrEmpty(_folder)) return;
        if (_watcher == null)
        {
            if (Directory.Exists(_folder)) AttachFolderWatcher();
            return;
        }
        if (!Directory.Exists(_folder))
        {
            Log.Info("截图文件夹暂时不在，停止监听");
            DetachFolderWatcher();
        }
    }

    private void AttachFolderWatcher()
    {
        if (_watcher != null || string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder)) return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(_folder))
            {
                if (IsImageName(path)) _seen.Add(path);
            }

            var watcher = new FileSystemWatcher(_folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Created += OnFolderEvent;
            watcher.Changed += OnFolderEvent;
            watcher.Renamed += OnRenamed;
            watcher.Error += (_, e) =>
            {
                Log.Error("截图文件夹监听中断: " + e.GetException().Message);
                _dispatcher.BeginInvoke(DetachFolderWatcher);
            };
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
            Log.Info("开始监听截图文件夹，已有 " + _seen.Count + " 张不重复挂");
        }
        catch (Exception ex)
        {
            Log.Error("无法监听截图文件夹: " + ex.Message);
            DetachFolderWatcher();
        }
    }

    private void DetachFolderWatcher()
    {
        if (_watcher == null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFolderEvent;
        _watcher.Changed -= OnFolderEvent;
        _watcher.Renamed -= OnRenamed;
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnFolderEvent(object sender, FileSystemEventArgs e) => QueueFolderPath(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e) => QueueFolderPath(e.FullPath);

    private void QueueFolderPath(string? path)
    {
        if (path == null || !IsImageName(path)) return;
        _dispatcher.BeginInvoke(() => ConsiderFolderFile(path));
    }

    private static bool IsImageName(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private void ConsiderFolderFile(string path)
    {
        if (_disposed || _seen.Contains(path) || _inflight.Contains(path)) return;
        _inflight.Add(path);
        _lastLength.Remove(path);
        ScheduleRead(path, DateTime.UtcNow);
    }

    private void ScheduleRead(string path, DateTime startedUtc)
    {
        var timer = new DispatcherTimer(ReadInterval, DispatcherPriority.Background, (_, _) => { }, _dispatcher);
        EventHandler? tick = null;
        tick = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= tick;
            TryReadFolderFile(path, startedUtc);
        };
        timer.Tick += tick;
        timer.Start();
    }

    private void TryReadFolderFile(string path, DateTime startedUtc)
    {
        if (_disposed)
        {
            _inflight.Remove(path);
            return;
        }

        if (!TryDecodeFile(path, out var width, out var height, out var length))
        {
            if (DateTime.UtcNow - startedUtc >= ReadDeadline)
            {
                _inflight.Remove(path);
                _lastLength.Remove(path);
                Log.Error("3 秒内没有读完 " + path);
                return;
            }
            ScheduleRead(path, startedUtc);
            return;
        }

        if (!_lastLength.TryGetValue(path, out var previous) || previous != length)
        {
            _lastLength[path] = length;
            if (DateTime.UtcNow - startedUtc >= ReadDeadline)
            {
                _inflight.Remove(path);
                _lastLength.Remove(path);
                Log.Error("文件仍在写入 " + path);
                return;
            }
            ScheduleRead(path, startedUtc);
            return;
        }

        _lastLength.Remove(path);
        _inflight.Remove(path);
        _seen.Add(path);
        PreferFolderOverClipboard(width, height);
        _recentFolder = new RecentFolderShot(width, height, DateTime.UtcNow);
        HangAndPeek(path);
    }

    private static bool TryDecodeFile(string path, out int width, out int height, out int length)
    {
        width = 0;
        height = 0;
        length = 0;
        try
        {
            var bytes = File.ReadAllBytes(path);
            length = bytes.Length;
            if (length == 0) return false;
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) return false;
            var frame = decoder.Frames[0];
            width = frame.PixelWidth;
            height = frame.PixelHeight;
            return width > 0 && height > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void PreferFolderOverClipboard(int width, int height)
    {
        if (_pendingClip == null) return;
        if (_pendingClip.Width != width || _pendingClip.Height != height) return;
        if (DateTime.UtcNow - _pendingClip.ArrivedUtc > DedupWindow) return;
        CancelPendingClip();
        Log.Info($"1.5 秒内剪贴板有同尺寸图片 {width}x{height}，只挂文件夹这张");
    }

    private void ListenClipboard()
    {
        var parameters = new HwndSourceParameters("Clothesline.Clipboard")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = NativeMethods.HwndMessage,
        };
        _clipboardWindow = new HwndSource(parameters);
        _clipboardWindow.AddHook(ClipboardHook);
        _listeningClipboard = NativeMethods.AddClipboardFormatListener(_clipboardWindow.Handle);
        if (_listeningClipboard) Log.Info("已监听剪贴板");
        else Log.Error("AddClipboardFormatListener 失败，错误码 " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }

    private IntPtr ClipboardHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmClipboardUpdate)
        {
            handled = true;
            OnClipboardUpdate();
        }
        return IntPtr.Zero;
    }

    private void OnClipboardUpdate()
    {
        if (_disposed) return;
        if (ClipboardGuard.ConsumeIfOwnUpdate())
        {
            Log.Info("忽略自己写入的剪贴板");
            return;
        }
        if (!ShouldTakeClipboard()) return;

        BitmapSource? image;
        try
        {
            image = Clipboard.GetImage();
        }
        catch (Exception ex)
        {
            Log.Error("读取剪贴板图片失败: " + ex.Message);
            return;
        }
        if (image == null || image.PixelWidth <= 0 || image.PixelHeight <= 0) return;

        var width = image.PixelWidth;
        var height = image.PixelHeight;
        if (MatchesRecentFolder(width, height))
        {
            Log.Info($"1.5 秒内文件夹已有同尺寸图片 {width}x{height}，跳过剪贴板");
            return;
        }

        byte[] png;
        try
        {
            png = EncodePng(image);
        }
        catch (Exception ex)
        {
            Log.Error("剪贴板图片转 png 失败: " + ex.Message);
            return;
        }

        if (_pendingClip != null && _pendingClip.Width == width && _pendingClip.Height == height)
            return;

        CancelPendingClip();
        var pending = new PendingClip(png, width, height, DateTime.UtcNow);
        pending.Timer = new DispatcherTimer(DedupWindow, DispatcherPriority.Background, (_, _) => CommitPendingClip(), _dispatcher);
        pending.Timer.Start();
        _pendingClip = pending;
        Log.Info($"剪贴板位图 {width}x{height}，推迟 {DedupWindow.TotalSeconds:0.0} 秒再落盘");
    }

    /// 规则同 Linux 版 xitu_helper.clip_should_take：
    /// 只有位图的是截图，收。带文件列表的是复制文件，带 RTF 的是 Word/WPS 复制，都不收。
    /// 带 HTML（可能还带文字）的：HTML 里只有一张图是浏览器「复制图片」，按设置 HangCopiedImages 决定；
    /// HTML 里有表格或文字（Excel/WPS 复制单元格）不收。
    private static bool ShouldTakeClipboard()
    {
        var fileDrop = NativeMethods.IsClipboardFormatAvailable(NativeMethods.CfHdrop);
        var bitmap = NativeMethods.IsClipboardFormatAvailable(NativeMethods.CfBitmap)
            || NativeMethods.IsClipboardFormatAvailable(NativeMethods.CfDib)
            || NativeMethods.IsClipboardFormatAvailable(NativeMethods.CfDibV5);
        if (!bitmap) return false;
        if (fileDrop)
        {
            Log.Info("剪贴板同时有文件列表和位图，按文件复制忽略");
            return false;
        }
        if (NativeMethods.IsClipboardFormatAvailable(RtfFormat))
        {
            Log.Info("剪贴板位图附带 RTF，按复制内容忽略");
            return false;
        }
        var html = NativeMethods.IsClipboardFormatAvailable(HtmlFormat);
        var text = NativeMethods.IsClipboardFormatAvailable(NativeMethods.CfText)
            || NativeMethods.IsClipboardFormatAvailable(NativeMethods.CfUnicodeText);
        if (!html && !text) return true;
        if (html && AppSettings.Current.HangCopiedImages && HtmlIsLoneImage(ReadClipboardHtml()))
        {
            Log.Info("剪贴板是复制的图片，挂上");
            return true;
        }
        Log.Info("剪贴板位图附带文字/HTML，按复制内容忽略");
        return false;
    }

    private static string ReadClipboardHtml()
    {
        try
        {
            var html = Clipboard.GetData(DataFormats.Html) as string ?? "";
            return html.Length > 200_000 ? html[..200_000] : html;
        }
        catch (Exception ex)
        {
            Log.Info("读取剪贴板 HTML 失败: " + ex.Message);
            return "";
        }
    }

    private static readonly Regex ImgTag = new(@"<img\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TableTag = new(@"<(table|td|tr)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AnyTag = new(@"<[^>]*>", RegexOptions.CultureInvariant);
    private static readonly Regex Nbsp = new(@"&nbsp;|&#160;", RegexOptions.CultureInvariant);

    /// HTML 里只有一张图、没有表格和文字。CF_HTML 开头的 Version:/StartHTML: 头不算正文。
    internal static bool HtmlIsLoneImage(string html)
    {
        var start = html.IndexOf('<');
        if (start < 0) return false;
        var body = html[start..];
        if (!ImgTag.IsMatch(body) || TableTag.IsMatch(body)) return false;
        var rest = Nbsp.Replace(AnyTag.Replace(body, ""), "");
        return rest.Trim().Length == 0;
    }

    private bool MatchesRecentFolder(int width, int height)
    {
        if (_recentFolder == null) return false;
        if (DateTime.UtcNow - _recentFolder.ArrivedUtc > DedupWindow) return false;
        return _recentFolder.Width == width && _recentFolder.Height == height;
    }

    private void CommitPendingClip()
    {
        var pending = _pendingClip;
        _pendingClip = null;
        if (pending == null || _disposed) return;
        pending.Timer?.Stop();
        try
        {
            var path = Library.ImportBytes(pending.Png, LibrarySource.Clipboard);
            HangAndPeek(path);
        }
        catch (Exception ex)
        {
            Log.Error("写入图库失败: " + ex.Message);
        }
    }

    private void CancelPendingClip()
    {
        if (_pendingClip == null) return;
        _pendingClip.Timer?.Stop();
        _pendingClip = null;
    }

    private void HangAndPeek(string path)
    {
        try
        {
            var lib = Library.IsUnderRoot(path) ? path : Library.ImportFile(path, LibrarySource.File);
            if (_line.Hang(lib)) _peek();
        }
        catch (Exception ex)
        {
            Log.Error("挂图失败: " + ex.Message);
        }
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        BitmapSource normalized = source;
        if (source.Format != PixelFormats.Bgra32 && source.Format != PixelFormats.Pbgra32 && source.Format != PixelFormats.Bgr32)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            normalized = converted;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(normalized));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private sealed class PendingClip
    {
        public PendingClip(byte[] png, int width, int height, DateTime arrivedUtc)
        {
            Png = png;
            Width = width;
            Height = height;
            ArrivedUtc = arrivedUtc;
        }

        public byte[] Png { get; }
        public int Width { get; }
        public int Height { get; }
        public DateTime ArrivedUtc { get; }
        public DispatcherTimer? Timer { get; set; }
    }

    private sealed record RecentFolderShot(int Width, int Height, DateTime ArrivedUtc);
}
