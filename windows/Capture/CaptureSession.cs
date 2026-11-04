// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Clothesline;

/// 定格、框选或点窗口、存进图库，再飞到截图所在的那条绳子上。
internal static class CaptureSession
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int VkReturn = 0x0D;
    private const int VkEscape = 0x1B;

    private static bool _open;
    private static bool _done;
    private static Line? _line;
    private static ScreenHost? _screens;
    private static WindowList? _windows;
    private static List<OverlayWindow> _overlays = new();
    private static readonly List<(MonitorDesc Monitor, BitmapSource Image)> _frames = new();
    private static NativeMethods.RECT? _highlight;
    private static IntPtr _hook;
    private static HookProc? _hookProc;

    public static bool IsOpen => _open;

    public static void Begin(Line line, ScreenHost screens)
    {
        if (_open) return;
        var monitors = Monitors.All();
        if (monitors.Count == 0)
        {
            Log.Error("没有屏幕可截");
            return;
        }

        _open = true;
        _done = false;
        _line = line;
        _screens = screens;
        _frames.Clear();
        _highlight = null;
        _windows = WindowList.Take(screens.LineHandles());

        foreach (var monitor in monitors)
        {
            var image = ScreenGrab.Capture(monitor);
            if (image == null)
            {
                Log.Error("定格失败 " + monitor.Device);
                continue;
            }
            _frames.Add((monitor, image));
        }
        if (_frames.Count == 0)
        {
            Cancel();
            return;
        }

        _overlays = new List<OverlayWindow>();
        foreach (var frame in _frames)
        {
            var overlay = new OverlayWindow(frame.Monitor, frame.Image);
            overlay.Hover += OnHover;
            overlay.RegionChosen += rect => Complete(rect);
            overlay.WindowChosen += ChooseWindow;
            overlay.CancelRequested += Cancel;
            overlay.Show();
            _overlays.Add(overlay);
        }

        var cursorOverlay = OverlayUnderCursor();
        cursorOverlay?.FocusForKeys();
        InstallHook();
        if (NativeMethods.GetCursorPos(out var point)) OnHover(point.X, point.Y);
        Log.Info("截图覆盖 " + _overlays.Count + " 块屏幕");
    }

    private static void OnHover(int x, int y)
    {
        if (_done || _windows == null) return;
        var overlay = _overlays.FirstOrDefault(item => item.Monitor.Contains(x, y));
        if (overlay == null)
        {
            foreach (var item in _overlays) item.ShowBox(null);
            _highlight = null;
            return;
        }

        var exclude = LiveHandles();
        _highlight = _windows.Hit(x, y, overlay.Monitor.Bounds, exclude);
        foreach (var item in _overlays)
            item.ShowBox(ReferenceEquals(item, overlay) ? _highlight : null);
    }

    private static void ChooseWindow()
    {
        if (_highlight is { } rect) Complete(rect);
    }

    private static void Complete(NativeMethods.RECT region)
    {
        if (_done || _line == null || _screens == null) return;
        _done = true;
        var overlay = _overlays.FirstOrDefault(item => item.Monitor.Contains(region.Left, region.Top))
            ?? _overlays.FirstOrDefault();
        if (overlay != null)
        {
            overlay.Flash(region, () => FinishCapture(region));
            return;
        }
        FinishCapture(region);
    }

    private static void FinishCapture(NativeMethods.RECT region)
    {
        var line = _line;
        var screens = _screens;
        var frames = _frames.ToList();
        CloseUi();
        if (line == null || screens == null || frames.Count == 0) return;

        var monitor = frames.Select(frame => frame.Monitor).FirstOrDefault(item => item.Contains(region.Left, region.Top))
            ?? frames[0].Monitor;
        var frameImage = frames.First(frame => frame.Monitor.Device == monitor.Device).Image;
        if (!TryCrop(frameImage, monitor, region, out var crop, out var pixels) || crop == null)
        {
            Log.Error("裁剪失败");
            return;
        }

        string path;
        try
        {
            path = SavePng(crop);
        }
        catch (Exception ex)
        {
            Log.Error("保存截图失败: " + ex.Message);
            return;
        }

        Log.Info($"截图 {pixels.Width}x{pixels.Height} {path}");
        if (AppSettings.Current.CopyToClipboard) Copy(line, path, crop);

        var fullscreen = FullScreen.IsActive(monitor);
        var id = line.TryHang(path, flying: !fullscreen);
        if (id == null) return;
        if (fullscreen)
        {
            Log.Info("全屏中，截图后不飞、不弹出 " + monitor.Device);
            return;
        }

        var item = line.Items.FirstOrDefault(peg => peg.Id == id.Value);
        if (item == null || !screens.RevealOn(monitor))
        {
            line.Land(id.Value);
            return;
        }

        var width = screens.DipWidth(monitor);
        var index = IndexOf(line, id.Value);
        var to = Layout.CardRect(index, line.Count, width, item.PixelWidth, item.PixelHeight);
        var from = new Rect(
            (pixels.Left - monitor.Bounds.Left) / monitor.Scale,
            (pixels.Top - monitor.Bounds.Top) / monitor.Scale,
            pixels.Width / monitor.Scale,
            pixels.Height / monitor.Scale);
        var landed = id.Value;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (line.Items.All(peg => peg.Id != landed)) return;
            var freshIndex = IndexOf(line, landed);
            var fresh = line.Items.First(peg => peg.Id == landed);
            var freshTo = Layout.CardRect(freshIndex, line.Count, screens.DipWidth(monitor), fresh.PixelWidth, fresh.PixelHeight);
            CaptureFlight.Play(crop, from, freshTo, fresh.Tilt, monitor, () => line.Land(landed));
        });
    }

    private static void Cancel()
    {
        if (_done && !_open) return;
        _done = true;
        CloseUi();
        Log.Info("截图已取消");
    }

    private static void CloseUi()
    {
        RemoveHook();
        foreach (var overlay in _overlays)
        {
            try { overlay.Close(); }
            catch (Exception ex) { Log.Error("关闭覆盖窗口失败: " + ex.Message); }
        }
        _overlays = new List<OverlayWindow>();
        _frames.Clear();
        _windows = null;
        _highlight = null;
        _line = null;
        _screens = null;
        _open = false;
        Memory.ReleaseSoon();
    }

    private static void Copy(Line line, string path, BitmapSource image)
    {
        try
        {
            ClipboardGuard.BeginOwnWrite();
            try
            {
                var data = new System.Windows.DataObject();
                var files = new StringCollection { path };
                data.SetFileDropList(files);
                data.SetImage(image);
                System.Windows.Clipboard.SetDataObject(data, copy: true);
            }
            catch
            {
                ClipboardGuard.CancelOwnWrite();
                throw;
            }
            line.NoteOwnClipboardWrite();
            Log.Info("截图已复制");
        }
        catch (Exception ex)
        {
            Log.Error("截图复制失败: " + ex.Message);
        }
    }

    private static string SavePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return Library.ImportBytes(stream.ToArray(), LibrarySource.Capture);
    }

    private static bool TryCrop(BitmapSource source, MonitorDesc monitor, NativeMethods.RECT region, out BitmapSource? crop, out NativeMethods.RECT pixels)
    {
        crop = null;
        var left = Math.Max(0, region.Left - monitor.Bounds.Left);
        var top = Math.Max(0, region.Top - monitor.Bounds.Top);
        var right = Math.Min(source.PixelWidth, region.Right - monitor.Bounds.Left);
        var bottom = Math.Min(source.PixelHeight, region.Bottom - monitor.Bounds.Top);
        var width = right - left;
        var height = bottom - top;
        pixels = new NativeMethods.RECT
        {
            Left = monitor.Bounds.Left + left,
            Top = monitor.Bounds.Top + top,
            Right = monitor.Bounds.Left + left + Math.Max(0, width),
            Bottom = monitor.Bounds.Top + top + Math.Max(0, height),
        };
        if (width < 2 || height < 2) return false;
        var cropped = new CroppedBitmap(source, new Int32Rect(left, top, width, height));
        cropped.Freeze();
        crop = cropped;
        return true;
    }

    private static int IndexOf(Line line, Guid id)
    {
        for (var i = 0; i < line.Items.Count; i++)
            if (line.Items[i].Id == id) return i;
        return 0;
    }

    private static OverlayWindow? OverlayUnderCursor()
    {
        if (!NativeMethods.GetCursorPos(out var point)) return _overlays.FirstOrDefault();
        return _overlays.FirstOrDefault(item => item.Monitor.Contains(point.X, point.Y)) ?? _overlays.FirstOrDefault();
    }

    private static List<IntPtr> LiveHandles()
    {
        var handles = new List<IntPtr>();
        if (_screens != null) handles.AddRange(_screens.LineHandles());
        foreach (var overlay in _overlays)
        {
            var handle = overlay.Handle;
            if (handle != IntPtr.Zero) handles.Add(handle);
        }
        return handles;
    }

    private static void InstallHook()
    {
        _hookProc = Hook;
        _hook = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) Log.Error("截图键盘钩子失败，错误码 " + Marshal.GetLastWin32Error());
    }

    private static void RemoveHook()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _hookProc = null;
    }

    private static IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown) && lParam != IntPtr.Zero)
        {
            var vk = Marshal.ReadInt32(lParam);
            if (vk == VkReturn)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(AcceptFullScreen);
                return (IntPtr)1;
            }
            if (vk == VkEscape)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(Cancel);
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static void AcceptFullScreen()
    {
        if (_done || !NativeMethods.GetCursorPos(out var point)) return;
        var overlay = _overlays.FirstOrDefault(item => item.Monitor.Contains(point.X, point.Y)) ?? _overlays.FirstOrDefault();
        if (overlay == null) return;
        Complete(overlay.Monitor.Bounds);
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
