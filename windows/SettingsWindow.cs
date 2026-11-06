// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Clothesline;

internal enum SettingsPage { Hotkeys, Library, General }

/// 系图设置：快捷键、图库、通用。
internal sealed class SettingsWindow : Window
{
    private static SettingsWindow? _open;
    private readonly HotKey _hotkey;
    private readonly Action _capture;
    private readonly Action _toggle;
    private readonly Action _gallery;
    private readonly Line _line;
    private readonly Border _hotPane;
    private readonly Border _libPane;
    private readonly Border _genPane;
    private readonly Dictionary<SettingsPage, Border> _nav = new();
    private TextBlock _space = null!;
    private TextBlock _cleanResult = null!;
    private TextBox _pathBox = null!;
    private bool _ready;

    private SettingsWindow(HotKey hotkey, Action capture, Action toggle, Action gallery, Line line)
    {
        _hotkey = hotkey;
        _capture = capture;
        _toggle = toggle;
        _gallery = gallery;
        _line = line;

        Title = "系图 · 设置";
        Width = 620;
        Height = 460;
        MinWidth = 520;
        MinHeight = 380;
        Background = Theme.PaperBrush;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        Foreground = Theme.InkBrush;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var nav = new StackPanel { Width = 112, Margin = new Thickness(12, 16, 8, 16) };
        nav.Children.Add(Nav(SettingsPage.Hotkeys, "快捷键"));
        nav.Children.Add(Nav(SettingsPage.Library, "图库"));
        nav.Children.Add(Nav(SettingsPage.General, "通用"));

        _hotPane = BuildHotkeys();
        _libPane = BuildLibrary();
        _genPane = BuildGeneral();
        var body = new Grid();
        body.Children.Add(_hotPane);
        body.Children.Add(_libPane);
        body.Children.Add(_genPane);

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rule = new Border { Background = new SolidColorBrush(Color.FromRgb(0xE3, 0xDA, 0xC8)) };
        Grid.SetColumn(rule, 1);
        Grid.SetColumn(body, 2);
        root.Children.Add(nav);
        root.Children.Add(rule);
        root.Children.Add(body);
        Content = root;
        _ready = true;
        SwitchPage(SettingsPage.Hotkeys);
    }

    public static void Show(HotKey hotkey, ScreenHost screens, Action capture, Action toggle, Action gallery, Line line, SettingsPage page = SettingsPage.Hotkeys)
    {
        if (_open != null)
        {
            _open.Activate();
            _open.SwitchPage(page);
            return;
        }
        screens.SetTopmost(false);
        var window = new SettingsWindow(hotkey, capture, toggle, gallery, line);
        _open = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_open, window)) _open = null;
            screens.SetTopmost(true);
        };
        window.Show();
        window.SwitchPage(page);
    }

    /// App 启动时设好：没开着的时候用它新建设置窗口。
    public static Action<SettingsPage>? Launcher { get; set; }

    public static void ShowPage(SettingsPage page)
    {
        if (_open != null) { _open.Activate(); _open.SwitchPage(page); return; }
        Launcher?.Invoke(page);
    }

    public void SwitchPage(SettingsPage page)
    {
        Border target = page switch
        {
            SettingsPage.Library => _libPane,
            SettingsPage.General => _genPane,
            _ => _hotPane,
        };
        foreach (var pane in new[] { _hotPane, _libPane, _genPane })
        {
            if (ReferenceEquals(pane, target)) continue;
            pane.Visibility = Visibility.Collapsed;
            pane.Opacity = 1;
        }
        target.Visibility = Visibility.Visible;
        target.Opacity = 0;
        Motion.Fade(target, 1, 120);
        foreach (var (key, border) in _nav)
            border.Background = key == page ? Theme.MenuHoverBrush : Brushes.Transparent;
        if (page == SettingsPage.Library) RefreshSpace();
    }

    private Border Nav(SettingsPage page, string text)
    {
        var hover = new Border
        {
            Background = Motion.WashBrush,
            CornerRadius = new CornerRadius(8),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        var label = new TextBlock { Text = text, Margin = new Thickness(12, 8, 12, 8) };
        var grid = new Grid();
        grid.Children.Add(hover);
        grid.Children.Add(label);
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 2, 0, 2),
            Background = Brushes.Transparent,
            Child = grid,
        };
        border.MouseEnter += (_, _) => Motion.Fade(hover, 1, 120);
        border.MouseLeave += (_, _) => Motion.Fade(hover, 0, 120);
        border.MouseLeftButtonUp += (_, _) => SwitchPage(page);
        _nav[page] = border;
        return border;
    }

    private Border BuildHotkeys()
    {
        var captureBox = Box(AppSettings.Current.Capture);
        var toggleBox = Box(AppSettings.Current.ToggleLine);
        var galleryBox = Box(AppSettings.Current.Gallery.IsBindable ? AppSettings.Current.Gallery : default);
        if (!AppSettings.Current.Gallery.IsBindable) galleryBox.Text = "未设置";
        var captureError = ErrorLine();
        var toggleError = ErrorLine();
        var galleryError = ErrorLine();
        captureBox.PreviewKeyDown += (_, e) => OnChord(e, HotKey.CaptureId, "截图", captureBox, captureError, _capture);
        toggleBox.PreviewKeyDown += (_, e) => OnChord(e, HotKey.ToggleId, "显示/隐藏绳子", toggleBox, toggleError, _toggle);
        galleryBox.PreviewKeyDown += (_, e) => OnChord(e, HotKey.GalleryId, "打开图库", galleryBox, galleryError, _gallery);

        var grid = new Grid { Margin = new Thickness(18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < 8; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddRow(grid, 0, "截图", captureBox, DefaultButton(() => OnChordKey(HotKey.CaptureId, "截图", HotkeyChord.CaptureDefault, captureBox, captureError, _capture)));
        Grid.SetRow(captureError, 1); Grid.SetColumn(captureError, 1); grid.Children.Add(captureError);
        AddRow(grid, 2, "显示/隐藏绳子", toggleBox, DefaultButton(() => OnChordKey(HotKey.ToggleId, "显示/隐藏绳子", HotkeyChord.ToggleDefault, toggleBox, toggleError, _toggle)));
        Grid.SetRow(toggleError, 3); Grid.SetColumn(toggleError, 1); grid.Children.Add(toggleError);
        AddRow(grid, 4, "打开图库", galleryBox, DefaultButton(() =>
        {
            _hotkey.Clear(HotKey.GalleryId);
            AppSettings.Current.Gallery = default;
            AppSettings.Current.Save();
            galleryBox.Text = "未设置";
            galleryError.Visibility = Visibility.Collapsed;
        }));
        Grid.SetRow(galleryError, 5); Grid.SetColumn(galleryError, 1); grid.Children.Add(galleryError);

        var note = new TextBlock
        {
            Text = "Win+Shift+S 由系统占用无法绑定。PrtSc 需先在 Windows 设置 › 辅助功能 › 键盘 中关闭「使用 Print Screen 键打开屏幕截图」。打开图库可不设快捷键。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Theme.MuteBrush,
            FontSize = 11,
            Margin = new Thickness(18, 8, 18, 12),
        };
        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(note);
        return new Border { Child = stack };
    }

    private Border BuildLibrary()
    {
        _pathBox = new TextBox
        {
            Text = Library.Root,
            IsReadOnly = true,
            Height = 28,
            Padding = new Thickness(8, 2, 8, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.White,
            BorderBrush = Theme.WoodStrokeBrush,
            BorderThickness = new Thickness(1.5),
            Margin = new Thickness(0, 0, 8, 0),
        };
        var change = DefaultButton(() => ChangePath());
        change.Content = "更改…";
        var pathRow = new DockPanel { Margin = new Thickness(0, 6, 0, 12) };
        DockPanel.SetDock(change, Dock.Right);
        pathRow.Children.Add(change);
        pathRow.Children.Add(_pathBox);

        var combo = new ComboBox { Width = 140, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (days, label) in Retention.Options)
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = days });
        foreach (ComboBoxItem item in combo.Items)
            if ((int)item.Tag == AppSettings.Current.RetentionDays) combo.SelectedItem = item;
        if (combo.SelectedItem == null) combo.SelectedIndex = 1;
        combo.SelectionChanged += (_, _) =>
        {
            if (!_ready || combo.SelectedItem is not ComboBoxItem item) return;
            AppSettings.Current.RetentionDays = (int)item.Tag;
            AppSettings.Current.Save();
            RefreshSpace();
        };

        _space = new TextBlock { Foreground = Theme.MuteBrush, Margin = new Thickness(0, 0, 0, 8) };
        var clean = DefaultButton(() => { });
        clean.Content = "立即清理过期";
        _cleanResult = new TextBlock
        {
            Foreground = Theme.MuteBrush,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
        };
        clean.Click += (_, _) =>
        {
            clean.Content = "清理中…";
            clean.IsEnabled = false;
            Dispatcher.BeginInvoke(() =>
            {
                var n = Retention.SweepNow(_line);
                clean.Content = "立即清理过期";
                clean.IsEnabled = true;
                _cleanResult.Text = n == 0 ? "没有过期的图" : "已清理 " + n + " 张（进回收站）";
                _cleanResult.Opacity = 0;
                Motion.Fade(_cleanResult, 1, 160);
                RefreshSpace();
            }, DispatcherPriority.Background);
        };
        var cleanRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        cleanRow.Children.Add(clean);
        cleanRow.Children.Add(_cleanResult);

        var open = new TextBlock
        {
            Text = "打开文件夹",
            Foreground = Theme.RedBrush,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 4, 0, 0),
        };
        open.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Library.Root);
                Process.Start(new ProcessStartInfo { FileName = Library.Root, UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error(ex.Message); }
        };

        var stack = new StackPanel { Margin = new Thickness(18) };
        stack.Children.Add(new TextBlock { Text = "位置", FontWeight = FontWeights.SemiBold });
        stack.Children.Add(pathRow);
        stack.Children.Add(new TextBlock { Text = "保存时间", FontWeight = FontWeights.SemiBold });
        stack.Children.Add(combo);
        stack.Children.Add(_space);
        stack.Children.Add(cleanRow);
        stack.Children.Add(open);
        return new Border { Child = stack };
    }

    private Border BuildGeneral()
    {
        var start = new CheckBox
        {
            Content = "开机自启",
            IsChecked = Autostart.IsEnabled(),
            Margin = new Thickness(0, 8, 0, 8),
        };
        start.Checked += (_, _) => { if (_ready) Autostart.SetEnabled(true); };
        start.Unchecked += (_, _) => { if (_ready) Autostart.SetEnabled(false); };
        var copy = new CheckBox
        {
            Content = "截完复制到剪贴板",
            IsChecked = AppSettings.Current.CopyToClipboard,
            Margin = new Thickness(0, 8, 0, 8),
        };
        copy.Checked += (_, _) => { if (_ready) { AppSettings.Current.CopyToClipboard = true; AppSettings.Current.Save(); } };
        copy.Unchecked += (_, _) => { if (_ready) { AppSettings.Current.CopyToClipboard = false; AppSettings.Current.Save(); } };
        var collect = new ComboBox { Margin = new Thickness(0, 4, 0, 8), MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (value, label) in CollectMode.Options)
            collect.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        collect.SelectedIndex = Math.Max(0, Array.FindIndex(CollectMode.Options, o => o.Value == AppSettings.Current.Collect));
        collect.SelectionChanged += (_, _) =>
        {
            if (!_ready || collect.SelectedItem is not ComboBoxItem { Tag: string value }) return;
            AppSettings.Current.Collect = value;
            AppSettings.Current.Save();
            Log.Info("收集来源改为 " + value);
        };
        var stack = new StackPanel { Margin = new Thickness(18) };
        stack.Children.Add(start);
        stack.Children.Add(copy);
        stack.Children.Add(new TextBlock { Text = "自动挂上", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        stack.Children.Add(collect);
        return new Border { Child = stack };
    }

    private void ChangePath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择图库位置", InitialDirectory = Library.Root };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName)) return;
        var dest = dialog.FolderName;
        if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(Library.Root), StringComparison.OrdinalIgnoreCase))
            return;
        var n = Library.List().Count;
        var move = false;
        if (n > 0)
        {
            var ask = MessageBox.Show(this, $"把现有 {n} 张图搬到新位置吗？", "图库位置", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (ask == MessageBoxResult.Cancel) return;
            move = ask == MessageBoxResult.Yes;
        }
        Library.ChangeRoot(dest, move, _line);
        _pathBox.Text = Library.Root;
        RefreshSpace();
    }

    private void RefreshSpace()
    {
        _space.Text = "占用 " + Library.FormatSize(Library.OccupiedBytes());
    }

    private void OnChord(KeyEventArgs e, int id, string name, TextBox box, TextBlock error, Action action)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) { e.Handled = false; return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;
        uint mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.ModControl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.ModAlt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.ModShift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= NativeMethods.ModWin;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;
        var chord = new HotkeyChord(mods, vk);
        if (!chord.IsBindable) return;
        OnChordKey(id, name, chord, box, error, action);
    }

    private void OnChordKey(int id, string name, HotkeyChord chord, TextBox box, TextBlock error, Action action)
    {
        HotkeyChord previous = id switch
        {
            HotKey.CaptureId => AppSettings.Current.Capture,
            HotKey.ToggleId => AppSettings.Current.ToggleLine,
            _ => AppSettings.Current.Gallery,
        };
        if (chord.Equals(previous) && chord.IsBindable)
        {
            box.Text = previous.Format();
            error.Visibility = Visibility.Collapsed;
            return;
        }
        if (!_hotkey.TryReplace(id, chord, action, name))
        {
            box.Text = previous.IsBindable ? previous.Format() : "未设置";
            error.Text = "该组合已被占用";
            error.Visibility = Visibility.Visible;
            error.Opacity = 0;
            Motion.Fade(error, 1, 120);
            if (box.RenderTransform is TranslateTransform shake) Motion.ShakeX(shake);
            return;
        }
        if (id == HotKey.CaptureId) AppSettings.Current.Capture = chord;
        else if (id == HotKey.ToggleId) AppSettings.Current.ToggleLine = chord;
        else AppSettings.Current.Gallery = chord;
        AppSettings.Current.Save();
        box.Text = chord.Format();
        error.Visibility = Visibility.Collapsed;
    }

    private static void AddRow(Grid grid, int row, string label, TextBox box, Button reset)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 8, 0) };
        box.Margin = new Thickness(0, 6, 8, 0);
        reset.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(text, row);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        Grid.SetRow(reset, row);
        Grid.SetColumn(reset, 2);
        grid.Children.Add(text);
        grid.Children.Add(box);
        grid.Children.Add(reset);
    }

    private static TextBox Box(HotkeyChord chord)
    {
        var border = new SolidColorBrush(Theme.WoodStroke);
        var box = new TextBox
        {
            Text = chord.IsBindable ? chord.Format() : "未设置",
            IsReadOnly = true,
            Height = 28,
            Padding = new Thickness(8, 2, 8, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.White,
            BorderBrush = border,
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.IBeam,
            RenderTransform = new TranslateTransform(),
        };
        box.GotKeyboardFocus += (_, _) =>
        {
            Motion.BreatheColor(border, Theme.Red, Color.FromRgb(0xE8, 0x7A, 0x74), 0.6);
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            Motion.StopColor(border, Theme.WoodStroke);
        };
        return box;
    }

    private static TextBlock ErrorLine() => new()
    {
        Text = "该组合已被占用",
        Foreground = Theme.RedBrush,
        FontSize = 11,
        Margin = new Thickness(0, 2, 0, 0),
        Opacity = 0,
        Visibility = Visibility.Collapsed,
    };

    private static Button DefaultButton(Action click)
    {
        var button = new Button
        {
            Content = "恢复默认",
            Padding = new Thickness(10, 4, 10, 4),
            Background = Theme.RedBrush,
            Foreground = Theme.WhiteBrush,
            BorderThickness = new Thickness(0),
        };
        button.Click += (_, _) => click();
        return button;
    }
}
