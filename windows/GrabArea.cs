using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Clothesline;

/// 单击复制、双击打开、长按画图、拖放、悬停取下、右键菜单。
internal static class GrabArea
{
    private static int _hold;
    private static int _drag;
    private static int _menu;

    public static bool Busy => _hold > 0 || _drag > 0 || _menu > 0;

    internal static void PushHold() => _hold++;
    internal static void PopHold() { if (_hold > 0) _hold--; }
    internal static void PushDrag() => _drag++;
    internal static void PopDrag() { if (_drag > 0) _drag--; }
    internal static void PushMenu() => _menu++;
    internal static void PopMenu() { if (_menu > 0) _menu--; }
}

internal sealed class GrabSession
{
    private readonly PeggedView _view;
    private readonly Line _line;
    private readonly DispatcherTimer _holdTimer;
    private Point _down;
    private bool _holding;
    private bool _dragged;
    private bool _paintOpened;
    private bool _menuOpen;
    private bool _retired;
    private long _lastDownAt;
    private Point _lastDownPos;
    private bool _skipNextUp;
    private bool _moving;      // 正沿绳子拖动换位置
    private bool _moved;       // 这次按下拖过位置，松开不算单击
    private double _grabOffsetX;

    /// 单击等这么久没有第二下才复制；取系统双击间隔，但不超过 0.3 秒，免得单击发钝。
    private static readonly TimeSpan DoubleClickWindow = TimeSpan.FromMilliseconds(
        Math.Min(300, System.Windows.Forms.SystemInformation.DoubleClickTime));
    private readonly DispatcherTimer _clickTimer = new() { Interval = DoubleClickWindow };
    private static readonly Dictionary<string, FileSystemWatcher> Watches = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DispatcherTimer> WatchDebounce = new(StringComparer.OrdinalIgnoreCase);

    public GrabSession(PeggedView view, Line line)
    {
        _view = view;
        _line = line;
        var card = view.Card;
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            Copy();
        };
        _holdTimer = new DispatcherTimer { Interval = Layout.HoldToEdit };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer.Stop();
            _paintOpened = true;
            Edit();
        };

        card.MouseEnter += (_, _) => _view.SetHover(true);
        card.MouseLeave += (_, _) => { if (!_holding) _view.SetHover(false); };
        card.MouseLeftButtonDown += OnDown;
        card.MouseMove += OnMove;
        card.MouseLeftButtonUp += OnUp;
        card.LostMouseCapture += (_, _) =>
        {
            if (_dragged) return;
            FinishMove(commit: true); // 拖到一半被别的窗口抢走鼠标：停在当前预想的位置
            EndHold();
        };

        var menu = new ContextMenu();
        menu.Items.Add(Item("复制", Copy));
        menu.Items.Add(Item("打开", Open));
        menu.Items.Add(Item("用画图编辑", Edit));
        var favorite = Item("收藏", ToggleFavorite);
        menu.Items.Add(favorite);
        menu.Items.Add(Item("在资源管理器中显示", Reveal));
        menu.Items.Add(new Separator());
        var takeDown = Item("取下", () => _line.Discard(_view.Item.Id));
        takeDown.Foreground = Theme.RedBrush;
        menu.Items.Add(takeDown);
        menu.Opened += (_, _) =>
        {
            favorite.Header = Library.IsFavorite(_view.Item.Path) ? "取消收藏" : "收藏";
            if (_menuOpen) return;
            _menuOpen = true;
            GrabArea.PushMenu();
            if (PresentationSource.FromVisual(menu) is HwndSource source)
            {
                NativeMethods.SetWindowPos(source.Handle, NativeMethods.HwndTopmost,
                    0, 0, 0, 0, 0x0001 | 0x0002 | NativeMethods.SwpNoActivate);
            }
        };
        menu.Closed += (_, _) =>
        {
            if (!_menuOpen) return;
            _menuOpen = false;
            GrabArea.PopMenu();
        };
        card.ContextMenu = menu;
    }

    public void Retire()
    {
        if (_retired) return;
        _retired = true;
        if (_moving) { _moving = false; (_view.Parent as LineView)?.EndMove(commit: false); }
        _holdTimer.Stop();
        _clickTimer.Stop();
        if (_menuOpen)
        {
            _menuOpen = false;
            GrabArea.PopMenu();
        }
        EndHold();
        _view.Card.ContextMenu = null;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _retired) return;
        if (FindCross(e.OriginalSource as DependencyObject) != null)
        {
            e.Handled = true;
            _line.Discard(_view.Item.Id);
            return;
        }
        // 不抢焦点的窗口上 ClickCount 不可靠，双击按系统双击时间和距离自己判断。
        var at = Environment.TickCount64;
        var where = e.GetPosition(_view.Card);
        if (at - _lastDownAt <= DoubleClickWindow.TotalMilliseconds
            && Math.Abs(where.X - _lastDownPos.X) <= SystemParameters.MinimumHorizontalDragDistance * 2
            && Math.Abs(where.Y - _lastDownPos.Y) <= SystemParameters.MinimumVerticalDragDistance * 2)
        {
            _lastDownAt = 0;
            _skipNextUp = true;
            _clickTimer.Stop(); // 双击只打开，不复制
            e.Handled = true;
            Open();
            return;
        }
        _lastDownAt = at;
        _lastDownPos = where;
        _down = e.GetPosition(_view.Card);
        _dragged = false;
        _moved = false;
        _paintOpened = false;
        _holding = true;
        GrabArea.PushHold();
        _view.SetPressed(true);
        _view.Card.CaptureMouse();
        _holdTimer.Start();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_holding || _dragged || e.LeftButton != MouseButtonState.Pressed) return;
        var lane = _view.Parent as LineView;
        if (_moving && lane != null)
        {
            // 沿绳子拖：往下拖出绳子区域，或拖出左右两端，就改成拖出一份给别的程序
            var at = e.GetPosition(lane);
            if (at.Y > lane.ActualHeight - 2 || at.X < 0 || at.X > lane.ActualWidth)
            {
                FinishMove(commit: false);
                StartDragOut();
                return;
            }
            lane.UpdateMove(at.X - _grabOffsetX);
            return;
        }

        var p = e.GetPosition(_view.Card);
        var dx = Math.Abs(p.X - _down.X);
        var dy = Math.Abs(p.Y - _down.Y);
        if (dx < SystemParameters.MinimumHorizontalDragDistance && dy < SystemParameters.MinimumVerticalDragDistance)
            return;

        // 一开始主要往下拖：照旧拖出一份（发聊天、存文件夹）；横着拖：在绳上换位置
        if (lane != null && _line.Count > 1 && p.Y - _down.Y < dx)
        {
            _moving = true;
            _moved = true;
            _holdTimer.Stop();
            _clickTimer.Stop();
            var start = e.GetPosition(lane);
            _grabOffsetX = start.X - (Canvas.GetLeft(_view) + Layout.CardWidth / 2);
            _view.SetLifted(true);
            lane.BeginMove(_view);
            lane.UpdateMove(start.X - _grabOffsetX);
            return;
        }
        StartDragOut();
    }

    private void FinishMove(bool commit)
    {
        if (!_moving) return;
        _moving = false;
        _view.SetLifted(false);
        (_view.Parent as LineView)?.EndMove(commit);
    }

    private void StartDragOut()
    {
        _dragged = true;
        _holdTimer.Stop();
        _view.SetDragging(true);
        GrabArea.PushDrag();
        DragDropEffects result;
        try
        {
            result = DragDrop.DoDragDrop(_view.Card, BuildData(), DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Log.Error("拖放失败: " + ex.Message);
            result = DragDropEffects.None;
        }
        finally
        {
            _view.SetDragging(false);
            GrabArea.PopDrag();
            EndHold();
        }
        if (result == DragDropEffects.None) return;
        Log.Info("拖出一份，照片留在绳上 " + _view.Item.Path);
        ScreenHost.HoldRevealed(Layout.PeekDuration);
        _view.Rehang();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _holdTimer.Stop();
        FinishMove(commit: true);
        var skip = _dragged || _moved || _paintOpened || _skipNextUp;
        _skipNextUp = false;
        EndHold();
        if (skip) return;
        _clickTimer.Stop();
        _clickTimer.Start();
    }

    private void EndHold()
    {
        if (!_holding) return;
        _holding = false;
        GrabArea.PopHold();
        _view.SetPressed(false);
        if (!_view.Card.IsMouseOver) _view.SetHover(false);
        if (_view.Card.IsMouseCaptured) _view.Card.ReleaseMouseCapture();
    }

    private void Copy()
    {
        try
        {
            ClipboardGuard.BeginOwnWrite();
            try
            {
                Clipboard.SetDataObject(BuildData(), copy: true);
            }
            catch
            {
                ClipboardGuard.CancelOwnWrite();
                throw;
            }
            _line.NoteOwnClipboardWrite();
            _line.NotifyCopied(_view.Item.Id);
            Log.Info("复制 " + _view.Item.Path);
        }
        catch (Exception ex)
        {
            Log.Error("复制失败: " + ex.Message);
        }
    }

    private void Open()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_view.Item.Path) { UseShellExecute = true });
            Log.Info("打开 " + _view.Item.Path);
        }
        catch (Exception ex)
        {
            Log.Error("打开失败: " + ex.Message);
        }
    }

    private void Edit()
    {
        try
        {
            Watch(_view.Item.Path, _line);
            Process.Start(new ProcessStartInfo
            {
                FileName = "mspaint.exe",
                Arguments = "\"" + _view.Item.Path + "\"",
                UseShellExecute = true,
            });
            Log.Info("画图编辑 " + _view.Item.Path);
        }
        catch (Exception ex)
        {
            Log.Error("无法打开画图: " + ex.Message);
        }
    }

    private void ToggleFavorite()
    {
        try
        {
            var path = _view.Item.Path;
            var next = Library.SetFavorite(path, !Library.IsFavorite(path));
            _line.Relocate(path, next);
        }
        catch (Exception ex)
        {
            Log.Error("收藏失败: " + ex.Message);
        }
    }

    private void Reveal()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + _view.Item.Path + "\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error("无法在资源管理器中显示: " + ex.Message);
        }
    }

    private DataObject BuildData()
    {
        var data = new DataObject();
        var files = new StringCollection { _view.Item.Path };
        data.SetFileDropList(files);
        if (TryLoadBitmap(_view.Item.Path, out var bitmap) && bitmap != null)
            data.SetImage(bitmap);
        return data;
    }

    private static bool TryLoadBitmap(string path, out BitmapSource? bitmap)
    {
        bitmap = null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            bitmap = image;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("准备拖放图片失败: " + ex.Message);
            return false;
        }
    }

    private static MenuItem Item(string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => action();
        return item;
    }

    private static Border? FindCross(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Border border && border.Child is TextBlock block && block.Text == "×")
                return border;
            source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    private static void Watch(string path, Line line)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch { return; }
        if (Watches.ContainsKey(full)) return;
        var dir = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return;
        var watcher = new FileSystemWatcher(dir, name)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        FileSystemEventHandler changed = (_, _) => Debounce(full, line);
        watcher.Changed += changed;
        watcher.Renamed += (_, e) =>
        {
            if (string.Equals(e.FullPath, full, StringComparison.OrdinalIgnoreCase))
                Debounce(full, line);
        };
        watcher.EnableRaisingEvents = true;
        Watches[full] = watcher;
    }

    private static void Debounce(string path, Line line)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        dispatcher.BeginInvoke(() =>
        {
            if (WatchDebounce.TryGetValue(path, out var existing)) existing.Stop();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                WatchDebounce.Remove(path);
                line.ReloadThumbnail(path);
            };
            WatchDebounce[path] = timer;
            timer.Start();
        });
    }
}
