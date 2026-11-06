using System.Drawing;
using WinForms = System.Windows.Forms;

namespace Clothesline;

internal sealed class TrayController : IDisposable
{
    private readonly Line _line;
    private readonly ScreenHost _screens;
    private readonly Action _capture;
    private readonly Action _openGallery;
    private readonly Action _openSettings;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _toggle;
    private readonly WinForms.ToolStripMenuItem _clear;
    private readonly WinForms.ToolStripMenuItem _capture2;
    private readonly WinForms.ToolStripMenuItem _header;
    private readonly WinForms.ToolStripMenuItem _startup;
    private readonly WinForms.ToolStripMenuItem _themes;
    private readonly WinForms.ToolStripMenuItem _exit;

    public TrayController(Line line, ScreenHost screens, Action capture, Action openGallery, Action openSettings)
    {
        _line = line;
        _screens = screens;
        _capture = capture;
        _openGallery = openGallery;
        _openSettings = openSettings;
        _toggle = new WinForms.ToolStripMenuItem("显示绳子", null, (_, _) => _screens.ToggleFromMenu());
        _clear = new WinForms.ToolStripMenuItem("清空绳子", null, (_, _) => _line.Clear());
        _capture2 = new WinForms.ToolStripMenuItem("截图", null, (_, _) => _capture());
        _header = TrayMenuStyle.Header("系图");
        _themes = new WinForms.ToolStripMenuItem("换主题");
        _themes.DropDownItems.Add(new WinForms.ToolStripMenuItem("…")); // 打开菜单时按 ThemeStore.List 重建
        if (_themes.DropDown is WinForms.ToolStripDropDownMenu themeMenu) TrayMenuStyle.ApplyDropDown(themeMenu);
        _startup = new WinForms.ToolStripMenuItem("开机自启", null, (_, _) => Autostart.SetEnabled(!Autostart.IsEnabled()));
        _exit = new WinForms.ToolStripMenuItem("退出", null, (_, _) => System.Windows.Application.Current.Shutdown());

        var menu = new WinForms.ContextMenuStrip();
        TrayMenuStyle.Apply(menu);
        menu.Opening += (_, _) => RefreshMenu();
        menu.Items.Add(_header);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_capture2);
        menu.Items.Add(new WinForms.ToolStripMenuItem("图库", null, (_, _) => _openGallery()));
        menu.Items.Add(_toggle);
        menu.Items.Add(_clear);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_themes);
        menu.Items.Add(new WinForms.ToolStripMenuItem("设置…", null, (_, _) => _openSettings()));
        menu.Items.Add(_startup);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_exit);
        foreach (WinForms.ToolStripItem item in menu.Items)
        {
            if (item is not WinForms.ToolStripSeparator) TrayMenuStyle.Item(item);
        }

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "系图",
            Visible = true,
            ContextMenuStrip = menu,
        };
        Log.Info("托盘已显示");
    }

    public void Balloon(string title, string text) =>
        _icon.ShowBalloonTip(8000, title, text, WinForms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private void RefreshMenu()
    {
        _toggle.Text = _screens.AnyRevealed ? "收起绳子" : "放下绳子";
        _toggle.ShortcutKeyDisplayString = AppSettings.Current.ToggleLine.Format();
        _capture2.ShortcutKeyDisplayString = AppSettings.Current.Capture.Format();
        _header.Text = _line.Count == 0 ? "系图 · 绳上空着" : $"系图 · 绳上 {_line.Count} 张";
        _clear.Enabled = _line.Count > 0;
        _startup.Checked = Autostart.IsEnabled();
        RefreshThemes();
    }

    /// 内置主题和 %LOCALAPPDATA%\Xitu\themes 里的都列出来，当前的打勾。同 Linux 绳子右键「换主题」。
    private void RefreshThemes()
    {
        _themes.DropDownItems.Clear();
        var current = ThemeStore.Current.Id;
        foreach (var (id, name) in ThemeStore.List())
        {
            var themeId = id;
            var item = new WinForms.ToolStripMenuItem(name, null, (_, _) => ThemeStore.Set(themeId))
            {
                Checked = id == current,
            };
            TrayMenuStyle.Item(item);
            _themes.DropDownItems.Add(item);
        }
    }


    private static Icon LoadTrayIcon()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path))
        {
            var extracted = Icon.ExtractAssociatedIcon(path);
            if (extracted != null) return extracted;
        }

        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/xitu.ico"));
        if (resource != null) return new Icon(resource.Stream);
        throw new InvalidOperationException("找不到系图图标");
    }
}
