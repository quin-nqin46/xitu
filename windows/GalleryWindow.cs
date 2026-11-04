// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Clothesline;

/// 图库窗口。只开一个。
internal sealed class GalleryWindow : Window
{
    private static GalleryWindow? _open;
    private readonly Line _line;
    private readonly ScreenHost? _screens;
    private readonly StackPanel _filters;
    private readonly Grid _filterHost;
    private readonly Border _filterHi;
    private readonly TranslateTransform _filterHiT = new();
    private readonly TextBlock _foot;
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _stack;
    private readonly Grid _right;
    private readonly TextBlock _empty;
    private readonly Dictionary<string, FilterRow> _filterRows = new();
    private readonly Dictionary<string, GalleryCard> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<DateTime, WrapPanel> _groups = new();
    private readonly Dictionary<DateTime, TextBlock> _headers = new();
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = new();
    private LibrarySource? _filter;
    private string? _anchor;
    private bool _ready;
    private bool _favoritesOnly;
    private bool _busyLeave;

    private GalleryWindow(Line line, ScreenHost? screens)
    {
        _line = line;
        _screens = screens;
        Title = "系图 · 图库";
        Width = AppSettings.Current.GalleryWidth > 200 ? AppSettings.Current.GalleryWidth : 1000;
        Height = AppSettings.Current.GalleryHeight > 200 ? AppSettings.Current.GalleryHeight : 680;
        MinWidth = 720;
        MinHeight = 480;
        Background = Theme.PaperBrush;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        Foreground = Theme.InkBrush;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (!double.IsNaN(AppSettings.Current.GalleryLeft)) Left = AppSettings.Current.GalleryLeft;
        if (!double.IsNaN(AppSettings.Current.GalleryTop)) Top = AppSettings.Current.GalleryTop;

        _filterHi = new Border
        {
            Background = Theme.MenuHoverBrush,
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Height = 36,
            Margin = new Thickness(8, 0, 8, 0),
            IsHitTestVisible = false,
            RenderTransform = _filterHiT,
        };
        _filters = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (key, label, source) in Filters())
        {
            var id = key;
            var row = new FilterRow(label, () => SwitchFilter(id, source));
            _filterRows[key] = row;
            _filters.Children.Add(row);
        }
        _filterHost = new Grid();
        _filterHost.Children.Add(_filterHi);
        _filterHost.Children.Add(_filters);
        _filters.Loaded += (_, _) => SlideHighlight(snap: true);

        _foot = new TextBlock
        {
            Foreground = Theme.MuteBrush,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 0, 16, 4),
        };
        var settingsLink = new TextBlock
        {
            Text = "设置…",
            Foreground = Theme.RedBrush,
            FontSize = 12,
            Margin = new Thickness(16, 0, 16, 16),
            Cursor = Cursors.Hand,
        };
        settingsLink.MouseLeftButtonUp += (_, _) => SettingsWindow.ShowPage(SettingsPage.Library);

        var side = new DockPanel { Width = 180, Background = Theme.PaperBrush };
        var title = new TextBlock
        {
            Text = "图库",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(16, 16, 16, 8),
        };
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(settingsLink, Dock.Bottom);
        DockPanel.SetDock(_foot, Dock.Bottom);
        side.Children.Add(title);
        side.Children.Add(settingsLink);
        side.Children.Add(_foot);
        side.Children.Add(_filterHost);

        _stack = new StackPanel { Margin = new Thickness(8, 12, 16, 24) };
        _empty = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Theme.MuteBrush,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _scroll = new ScrollViewer
        {
            Content = _stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
        };
        _right = new Grid();
        _right.Children.Add(_scroll);
        _right.Children.Add(_empty);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rule = new Border { Background = new SolidColorBrush(Color.FromRgb(0xE3, 0xDA, 0xC8)) };
        Grid.SetColumn(rule, 1);
        Grid.SetColumn(_right, 2);
        root.Children.Add(side);
        root.Children.Add(rule);
        root.Children.Add(_right);
        Content = root;

        PreviewKeyDown += OnKey;
        Closing += (_, _) => { if (_screens != null) PersistBounds(); };
        Closed += (_, _) =>
        {
            Library.Changed -= OnLibraryChanged;
            if (ReferenceEquals(_open, this)) _open = null;
            Memory.ReleaseSoon();
        };
        Library.Changed += OnLibraryChanged;
        _ready = true;
        RebuildImmediate();
        SlideHighlight(snap: true);
    }

    public static void Show(Line line, ScreenHost screens)
    {
        if (_open != null)
        {
            _open.Activate();
            if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
            return;
        }
        _open = new GalleryWindow(line, screens);
        _open.Show();
    }

    public static GalleryWindow Preview(Line line)
    {
        var window = new GalleryWindow(line, null);
        _open = window;
        return window;
    }

    public IReadOnlyList<GalleryCard> PreviewCards()
        => _order.Select(p => _cards.TryGetValue(p, out var c) ? c : null).OfType<GalleryCard>().ToList();

    private static (string Key, string Label, LibrarySource? Source)[] Filters() =>
    [
        ("all", "全部", null),
        ("capture", "截图", LibrarySource.Capture),
        ("clip", "剪贴板", LibrarySource.Clipboard),
        ("drop", "拖入", LibrarySource.Drop),
        ("file", "文件", LibrarySource.File),
        ("fav", "收藏", null),
    ];

    private void SwitchFilter(string id, LibrarySource? source)
    {
        if (id == "fav") { _favoritesOnly = true; _filter = null; }
        else { _favoritesOnly = false; _filter = source; }
        SlideHighlight(snap: false);
        void Apply()
        {
            RebuildImmediate();
            Motion.Fade(_right, 1, 150);
        }
        if (!Motion.Enabled)
        {
            RebuildImmediate();
            return;
        }
        Motion.Fade(_right, 0, 100, Apply);
    }

    private void OnLibraryChanged()
    {
        if (!_ready) return;
        Dispatcher.BeginInvoke(() =>
        {
            var offset = _scroll.VerticalOffset;
            Incremental();
            _scroll.ScrollToVerticalOffset(offset);
        });
    }

    private List<LibraryItem> CurrentList()
    {
        var all = Library.List();
        IEnumerable<LibraryItem> view = all;
        if (_favoritesOnly) view = all.Where(i => i.Favorite);
        else if (_filter != null) view = all.Where(i => i.Source == _filter);
        return view.OrderByDescending(i => i.Taken).ToList();
    }

    private void RefreshCounts()
    {
        var all = Library.List();
        foreach (var (key, _, source) in Filters())
        {
            int n;
            if (key == "all") n = all.Count;
            else if (key == "fav") n = all.Count(i => i.Favorite);
            else n = all.Count(i => i.Source == source);
            _filterRows[key].SetCount(n);
        }
        _foot.Text = "保存 " + Retention.Label(AppSettings.Current.RetentionDays) + " · 占用 " + Library.FormatSize(Library.OccupiedBytes());
        _empty.Text = "还没有图。截一张试试：" + AppSettings.Current.Capture.Format();
    }

    private void RebuildImmediate()
    {
        RefreshCounts();
        var list = CurrentList();
        _empty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        _stack.Children.Clear();
        _cards.Clear();
        _order.Clear();
        _groups.Clear();
        _headers.Clear();
        foreach (var group in list.GroupBy(i => i.Taken.Date))
        {
            var wrap = EnsureGroup(group.Key);
            foreach (var item in group)
            {
                var card = MakeCard(item);
                wrap.Children.Add(card);
                _cards[item.Path] = card;
                _order.Add(item.Path);
                QueueThumb(card, item.Path);
            }
        }
        _selected.RemoveWhere(p => !_cards.ContainsKey(p));
        PaintSelection();
        SlideHighlight(snap: true);
    }

    private WrapPanel EnsureGroup(DateTime day)
    {
        if (_groups.TryGetValue(day, out var wrap)) return wrap;
        var header = new TextBlock
        {
            Text = DateTitle(day),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 10, 8, 8),
        };
        wrap = new WrapPanel();
        _headers[day] = header;
        _groups[day] = wrap;
        if (day == DateTime.Today)
        {
            _stack.Children.Insert(0, header);
            _stack.Children.Insert(1, wrap);
        }
        else
        {
            _stack.Children.Add(header);
            _stack.Children.Add(wrap);
        }
        return wrap;
    }

    private GalleryCard MakeCard(LibraryItem item)
    {
        var card = new GalleryCard(item);
        card.MouseLeftButtonDown += (_, e) => OnCardDown(card, e);
        card.MouseMove += OnCardMove;
        card.MouseLeftButtonUp += OnCardUp;
        card.MouseRightButtonDown += (_, _) =>
        {
            if (!_selected.Contains(card.Item.Path)) SelectOnly(card.Item.Path);
            card.ContextMenu = BuildMenu();
        };
        card.StarClicked += OnStarClicked;
        if (_selected.Contains(item.Path)) card.SetSelected(true);
        return card;
    }

    private void OnStarClicked(GalleryCard card)
    {
        var path = card.Item.Path;
        var next = Library.SetFavorite(path, !Library.IsFavorite(path));
        _line.Relocate(path, next);
    }

    private void Incremental()
    {
        if (_busyLeave) return;
        RefreshCounts();
        var list = CurrentList();
        _empty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        var wanted = list.Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var leaving = _cards.Keys.Where(p => !wanted.Contains(p)).ToList();
        var entering = list.Where(i => !_cards.ContainsKey(i.Path)).ToList();

        foreach (var old in leaving.ToList())
        {
            var name = Path.GetFileName(old);
            var hit = entering.FirstOrDefault(i => Path.GetFileName(i.Path).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (hit == null) continue;
            var card = _cards[old];
            _cards.Remove(old);
            _cards[hit.Path] = card;
            card.Bind(hit);
            leaving.Remove(old);
            entering.Remove(hit);
            var idx = _order.IndexOf(old);
            if (idx >= 0) _order[idx] = hit.Path;
        }

        _order.Clear();
        foreach (var item in list)
            if (_cards.ContainsKey(item.Path)) _order.Add(item.Path);

        if (leaving.Count > 0)
        {
            var snap = Snapshot();
            _busyLeave = true;
            var pending = leaving.Count;
            foreach (var path in leaving)
            {
                if (!_cards.TryGetValue(path, out var card))
                {
                    pending--;
                    continue;
                }
                card.PlayLeave(() =>
                {
                    DetachCard(path);
                    pending--;
                    if (pending > 0) return;
                    PruneEmptyGroups();
                    InsertNew(entering);
                    _stack.UpdateLayout();
                    Flip(snap);
                    _busyLeave = false;
                    PaintSelection();
                });
            }
            if (pending == 0)
            {
                _busyLeave = false;
                InsertNew(entering);
            }
            return;
        }

        InsertNew(entering);
        PaintSelection();
    }

    private void InsertNew(List<LibraryItem> items)
    {
        if (items.Count == 0) return;
        foreach (var item in items.OrderByDescending(i => i.Taken))
        {
            var wrap = EnsureGroup(item.Taken.Date);
            var card = MakeCard(item);
            var at = item.Taken.Date == DateTime.Today ? 0 : wrap.Children.Count;
            wrap.Children.Insert(at, card);
            _cards[item.Path] = card;
            QueueThumb(card, item.Path);
            card.PlayEnter();
        }
        _order.Clear();
        foreach (var item in CurrentList())
            if (_cards.ContainsKey(item.Path)) _order.Add(item.Path);
        _selected.RemoveWhere(p => !_cards.ContainsKey(p));
    }

    private void DetachCard(string path)
    {
        if (!_cards.TryGetValue(path, out var card)) return;
        if (card.Parent is WrapPanel wrap) wrap.Children.Remove(card);
        _cards.Remove(path);
        _order.Remove(path);
        _selected.Remove(path);
    }

    private void PruneEmptyGroups()
    {
        foreach (var day in _groups.Keys.ToList())
        {
            if (_groups[day].Children.Count > 0) continue;
            _stack.Children.Remove(_groups[day]);
            if (_headers.TryGetValue(day, out var header)) _stack.Children.Remove(header);
            _groups.Remove(day);
            _headers.Remove(day);
        }
    }

    private Dictionary<GalleryCard, Point> Snapshot()
    {
        var map = new Dictionary<GalleryCard, Point>();
        _stack.UpdateLayout();
        foreach (var card in _cards.Values)
        {
            try { map[card] = card.TranslatePoint(new Point(0, 0), _stack); }
            catch (InvalidOperationException) { }
        }
        return map;
    }

    private void Flip(Dictionary<GalleryCard, Point> old)
    {
        _stack.UpdateLayout();
        foreach (var card in _cards.Values)
        {
            if (!old.TryGetValue(card, out var from)) continue;
            Point to;
            try { to = card.TranslatePoint(new Point(0, 0), _stack); }
            catch (InvalidOperationException) { continue; }
            var dx = from.X - to.X;
            var dy = from.Y - to.Y;
            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) continue;
            card.PlayFlip(dx, dy);
        }
    }

    private void SlideHighlight(bool snap)
    {
        var key = _favoritesOnly ? "fav"
            : _filter == LibrarySource.Capture ? "capture"
            : _filter == LibrarySource.Clipboard ? "clip"
            : _filter == LibrarySource.Drop ? "drop"
            : _filter == LibrarySource.File ? "file"
            : "all";
        if (!_filterRows.TryGetValue(key, out var row)) return;
        row.UpdateLayout();
        _filterHost.UpdateLayout();
        Point p;
        try { p = row.TranslatePoint(new Point(0, 0), _filterHost); }
        catch (InvalidOperationException) { return; }
        if (row.ActualHeight > 1) _filterHi.Height = row.ActualHeight;
        if (snap || !Motion.Enabled)
        {
            Motion.Clear(_filterHiT, TranslateTransform.YProperty, p.Y);
            return;
        }
        Motion.Move(_filterHiT, 0, p.Y, 180);
    }

    private Point _dragStart;
    private bool _dragArmed;
    private void OnCardDown(GalleryCard card, MouseButtonEventArgs e)
    {
        Focus();
        _dragStart = e.GetPosition(this);
        _dragArmed = true;
        var path = card.Item.Path;
        if (e.ClickCount == 2)
        {
            _dragArmed = false;
            Open(path);
            e.Handled = true;
            return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (!_selected.Add(path)) _selected.Remove(path);
            _anchor = path;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift && _anchor != null)
        {
            SelectRange(_anchor, path);
        }
        else
        {
            SelectOnly(path);
            _anchor = path;
        }
        PaintSelection();
        e.Handled = true;
    }

    private void OnCardMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed || _selected.Count == 0) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _dragArmed = false;
        var files = new StringCollection();
        foreach (var path in _selected)
            if (File.Exists(path)) files.Add(path);
        if (files.Count == 0) return;
        var data = new DataObject();
        data.SetFileDropList(files);
        var first = files[0];
        if (!string.IsNullOrEmpty(first) && TryLoadBitmap(first, out var bitmap) && bitmap != null)
            data.SetImage(bitmap);
        DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
    }

    private void OnCardUp(object sender, MouseButtonEventArgs e) => _dragArmed = false;

    private void SelectOnly(string path)
    {
        _selected.Clear();
        _selected.Add(path);
        PaintSelection();
    }

    private void SelectRange(string from, string to)
    {
        var a = _order.IndexOf(from);
        var b = _order.IndexOf(to);
        if (a < 0 || b < 0) { SelectOnly(to); return; }
        if (a > b) (a, b) = (b, a);
        _selected.Clear();
        for (var i = a; i <= b; i++) _selected.Add(_order[i]);
    }

    private void PaintSelection()
    {
        foreach (var card in _cards.Values)
            card.SetSelected(_selected.Contains(card.Item.Path));
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopyFirst();
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _selected.Clear();
            foreach (var path in _order) _selected.Add(path);
            PaintSelection();
            e.Handled = true;
        }
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        var paths = _selected.ToList();
        var one = paths.Count == 1 ? paths[0] : null;
        menu.Items.Add(Item("挂回绳上", HangBack));
        menu.Items.Add(Item("复制", CopyFirst));
        menu.Items.Add(Item("打开", () => { if (one != null) Open(one); }));
        var fav = one != null && Library.IsFavorite(one);
        menu.Items.Add(Item(fav ? "取消收藏" : "收藏", ToggleFavorite));
        menu.Items.Add(Item("在资源管理器中显示", Reveal));
        menu.Items.Add(new Separator());
        var del = Item("删除", DeleteSelected);
        del.Foreground = Theme.RedBrush;
        menu.Items.Add(del);
        return menu;
    }

    private static MenuItem Item(string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => action();
        return item;
    }

    private void HangBack()
    {
        foreach (var path in _selected.ToList())
            _line.Hang(path);
        _screens?.PeekUnderCursor();
    }

    private void ToggleFavorite()
    {
        foreach (var path in _selected.ToList())
        {
            var next = Library.SetFavorite(path, !Library.IsFavorite(path));
            _line.Relocate(path, next);
        }
    }

    private void Reveal()
    {
        var path = _selected.FirstOrDefault();
        if (path == null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + path + "\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { Log.Error(ex.Message); }
    }

    private void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("打开失败: " + ex.Message); }
    }

    private void CopyFirst()
    {
        var path = _selected.FirstOrDefault();
        if (path == null || !File.Exists(path)) return;
        try
        {
            ClipboardGuard.BeginOwnWrite();
            var data = new DataObject();
            data.SetFileDropList(new StringCollection { path });
            if (TryLoadBitmap(path, out var bitmap) && bitmap != null) data.SetImage(bitmap);
            Clipboard.SetDataObject(data, copy: true);
            _line.NoteOwnClipboardWrite();
        }
        catch (Exception ex)
        {
            ClipboardGuard.CancelOwnWrite();
            Log.Error("复制失败: " + ex.Message);
        }
    }

    private void DeleteSelected()
    {
        var n = _selected.Count;
        if (n == 0) return;
        var ask = MessageBox.Show(
            this,
            n == 1 ? "这张图会进入回收站，可以从回收站还原。确定删除？" : $"这 {n} 张图会进入回收站，可以从回收站还原。确定删除？",
            "删除",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (ask != MessageBoxResult.OK) return;
        foreach (var path in _selected.ToList())
        {
            _line.RemoveByPath(path);
            Library.Recycle(path);
        }
        _selected.Clear();
    }

    private void QueueThumb(GalleryCard card, string path)
    {
        var dispatcher = Dispatcher;
        Task.Run(() =>
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = 336;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                dispatcher.BeginInvoke(() => { if (card.Item.Path == path) card.SetThumb(bmp); });
            }
            catch { }
        });
    }

    private static bool TryLoadBitmap(string path, out BitmapSource? bitmap)
    {
        bitmap = null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            bitmap = image;
            return true;
        }
        catch { return false; }
    }

    private void PersistBounds()
    {
        AppSettings.Current.GalleryLeft = Left;
        AppSettings.Current.GalleryTop = Top;
        AppSettings.Current.GalleryWidth = Width;
        AppSettings.Current.GalleryHeight = Height;
        AppSettings.Current.Save();
    }

    private static string DateTitle(DateTime day)
    {
        var today = DateTime.Today;
        if (day == today) return "今天";
        if (day == today.AddDays(-1)) return "昨天";
        var week = "日一二三四五六"[(int)day.DayOfWeek];
        var core = $"{day.Month}月{day.Day}日 周{week}";
        return day.Year == today.Year ? core : day.Year + "年" + core;
    }

    private sealed class FilterRow : Grid
    {
        private readonly TextBlock _count;
        private readonly Border _hover;

        public FilterRow(string label, Action click)
        {
            Margin = new Thickness(8, 1, 8, 1);
            _hover = new Border
            {
                Background = Motion.WashBrush,
                CornerRadius = new CornerRadius(8),
                Opacity = 0,
                IsHitTestVisible = false,
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            _count = new TextBlock { Foreground = Theme.MuteBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_count, 1);
            grid.Children.Add(_count);
            var content = new Border
            {
                Padding = new Thickness(12, 8, 12, 8),
                Background = Brushes.Transparent,
                Child = grid,
            };
            Children.Add(_hover);
            Children.Add(content);
            MouseEnter += (_, _) => Motion.Fade(_hover, 1, 120);
            MouseLeave += (_, _) => Motion.Fade(_hover, 0, 120);
            MouseLeftButtonUp += (_, _) => click();
        }

        public void SetCount(int n) => _count.Text = n.ToString();
    }
}

internal sealed class GalleryCard : Border
{
    public LibraryItem Item { get; private set; }
    public event Action<GalleryCard>? StarClicked;

    private readonly Image _image;
    private readonly Border _place;
    private readonly Border _stroke;
    private readonly Border _starHit;
    private readonly Border _starDisk;
    private readonly System.Windows.Shapes.Path _star;
    private readonly TextBlock _time;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _hoverT = new();
    private readonly TranslateTransform _flipT = new();
    private readonly ScaleTransform _starScale = new(1, 1);
    private readonly SoftShadow _shadow;
    private bool _selected;
    private bool _hover;
    private bool _fav;
    private bool _leaving;

    public GalleryCard(LibraryItem item)
    {
        Item = item;
        _fav = item.Favorite;
        Width = 168;
        Margin = new Thickness(8);
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        ClipToBounds = false;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new TransformGroup { Children = { _scale, _hoverT, _flipT } };
        // 白卡底画在内层，外层先垫预渲染投影（见 SoftShadow）；卡高要排版后才知道，尺寸变了再重画
        _shadow = new SoftShadow(168, 1, 10, blur: 14, depth: 6, opacity: 0.18); // 先给 1 高，免得投影反过来把卡片撑高
        SizeChanged += (_, e) => _shadow.Resize(e.NewSize.Width, e.NewSize.Height, 10, 14);

        _image = new Image
        {
            Height = 120,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0,
        };
        _place = new Border
        {
            Height = 120,
            Background = Theme.PlaceBrush,
            CornerRadius = new CornerRadius(6),
            Child = _image,
        };
        _time = new TextBlock
        {
            Text = item.Taken.ToString("HH:mm", CultureInfo.InvariantCulture),
            Foreground = Theme.MuteBrush,
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(_place);
        stack.Children.Add(_time);
        var body = new Border
        {
            Padding = new Thickness(8, 8, 8, 6),
            Child = stack,
            Background = Brushes.Transparent,
        };

        _stroke = new Border
        {
            BorderBrush = Theme.RedBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Opacity = 0,
            IsHitTestVisible = false,
        };

        _star = Motion.MakeStar(18, filled: item.Favorite);
        _star.RenderTransformOrigin = new Point(0.5, 0.5);
        _star.RenderTransform = _starScale;
        _starDisk = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = Motion.StarDisk,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _starHit = new Border
        {
            Width = 26,
            Height = 26,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0),
            Cursor = Cursors.Hand,
        };
        var starLayer = new Grid { Width = 26, Height = 26 };
        starLayer.Children.Add(_starDisk);
        starLayer.Children.Add(_star);
        _starHit.Child = starLayer;
        _starHit.MouseLeftButtonDown += OnStarDown;
        _starHit.MouseEnter += (_, _) => { if (_fav) Motion.Scale(_starScale, 1.15, 120); };
        _starHit.MouseLeave += (_, _) => { if (_fav) Motion.Scale(_starScale, 1, 120); };

        var root = new Grid();
        root.Children.Add(body);
        root.Children.Add(_stroke);
        root.Children.Add(_starHit);
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = Theme.CardBrush,
            Child = root,
        };
        Child = new Grid { Children = { _shadow, card } };

        MouseEnter += (_, _) => SetHover(true);
        MouseLeave += (_, _) => SetHover(false);
        ApplyStar(animate: false);
    }

    public void Bind(LibraryItem item)
    {
        Item = item;
        _time.Text = item.Taken.ToString("HH:mm", CultureInfo.InvariantCulture);
        SetFavorite(item.Favorite, animate: true);
    }

    public void SetThumb(ImageSource source)
    {
        _image.Source = source;
        Motion.Fade(_image, 1, 180, () => { _place.Background = Brushes.Transparent; });
        if (!Motion.Enabled) _place.Background = Brushes.Transparent;
    }

    public void SetSelected(bool on)
    {
        if (_selected == on) return;
        _selected = on;
        Motion.Fade(_stroke, on ? 1 : 0, 120);
    }

    public void SetFavorite(bool fav, bool animate)
    {
        if (_fav == fav && !animate) { ApplyStar(false); return; }
        _fav = fav;
        ApplyStar(animate);
    }

    public void SnapHover()
    {
        _hover = true;
        Motion.Clear(_hoverT, TranslateTransform.YProperty, -3);
        Motion.Clear(_scale, ScaleTransform.ScaleXProperty, 1.02);
        Motion.Clear(_scale, ScaleTransform.ScaleYProperty, 1.02);
        _shadow.Snap(8, 0.32);
        ApplyStar(false);
    }

    public void SnapSelected()
    {
        _selected = true;
        Motion.Clear(_stroke, OpacityProperty, 1);
    }

    public void PlayLeave(Action done)
    {
        _leaving = true;
        Motion.Scale(_scale, 0.9, 200);
        Motion.Fade(this, 0, 200, done);
    }

    public void PlayEnter()
    {
        Opacity = 0;
        Motion.Clear(_scale, ScaleTransform.ScaleXProperty, 0.96);
        Motion.Clear(_scale, ScaleTransform.ScaleYProperty, 0.96);
        Motion.Fade(this, 1, 200);
        Motion.Scale(_scale, 1, 200);
    }

    public void PlayFlip(double dx, double dy)
    {
        Motion.Clear(_flipT, TranslateTransform.XProperty, dx);
        Motion.Clear(_flipT, TranslateTransform.YProperty, dy);
        Motion.Move(_flipT, 0, 0, 180);
    }

    private void SetHover(bool on)
    {
        if (_leaving) return;
        _hover = on;
        Motion.Move(_hoverT, 0, on ? -3 : 0, 150);
        Motion.Scale(_scale, on ? 1.02 : 1, 150);
        _shadow.To(on ? 8 : 6, on ? 0.32 : 0.18, 150);
        if (!_fav) ApplyStar(false);
    }

    private void OnStarDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        StarClicked?.Invoke(this);
    }

    private void ApplyStar(bool animate)
    {
        if (_fav)
        {
            _star.Fill = Theme.RedBrush;
            _star.Stroke = null;
            _star.StrokeThickness = 0;
            _star.Opacity = 1;
            _starDisk.Opacity = 0;
            _starHit.IsHitTestVisible = true;
            if (animate) Motion.Pop(_starScale);
            else
            {
                Motion.Clear(_starScale, ScaleTransform.ScaleXProperty, 1);
                Motion.Clear(_starScale, ScaleTransform.ScaleYProperty, 1);
            }
            return;
        }

        void Rest()
        {
            _star.Fill = Brushes.Transparent;
            _star.Stroke = Theme.MuteBrush;
            _star.StrokeThickness = 1.4;
            Motion.Clear(_starScale, ScaleTransform.ScaleXProperty, 1);
            Motion.Clear(_starScale, ScaleTransform.ScaleYProperty, 1);
            var show = _hover;
            _star.Opacity = show ? 1 : 0;
            _starDisk.Opacity = show ? 1 : 0;
            _starHit.IsHitTestVisible = show;
        }

        if (animate)
        {
            Motion.Scale(_starScale, 0, 150);
            Motion.Fade(_star, 0, 150, Rest);
            return;
        }
        Rest();
    }
}
