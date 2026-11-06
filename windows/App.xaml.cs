// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Clothesline;

public partial class App : Application
{
    private Line? _line;
    private ScreenHost? _screens;
    private HotKey? _hotkey;
    private TrayController? _tray;
    private ScreenshotWatcher? _shots;
    private Mutex? _single;
    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // 软件渲染：显卡渲染要加载核显/独显驱动的 D3D 组件，进程多占约 200 MB（在核显机器上实测 270 → 65 MB）；
        // 绳子是透明分层窗口，显卡渲染每帧还要读回内存，省不了 CPU。实时阴影已换成预渲染（SoftShadow），软件渲染下也不卡。
        // 加 --gpu 可切回显卡渲染。
        if (!e.Args.Contains("--gpu"))
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception.ToString());
            // 常驻小工具：界面线程的意外记日志后继续跑，不因一帧出错整个退出。
            args.Handled = true;
        };

        // 只允许一个系图：再次双击时通知已在跑的那个放下绳子，自己退出。
        _single = new Mutex(true, @"Local\Xitu.SingleInstance", out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Xitu.Show");
        if (!first)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }
        var signal = _showSignal;
        new Thread(() =>
        {
            while (signal.WaitOne())
                Dispatcher.BeginInvoke(() => _screens?.ToggleFromMenu());
        }) { IsBackground = true, Name = "XituShowSignal" }.Start();

        Log.Info("启动");
        Autostart.ClearLegacy();
        var firstRun = !File.Exists(AppPaths.SettingsFile);
        AppSettings.Load();
        ThemeStore.Load();
        var render = Array.FindIndex(e.Args, a => string.Equals(a, "--render-themes", StringComparison.OrdinalIgnoreCase));
        if (render >= 0)
        {
            // 诊断：给每个主题画一张预览 PNG 后退出，不起托盘、热键和监听（见 ThemePreview）
            ThemePreview.Run(render + 1 < e.Args.Length ? e.Args[render + 1] : "theme-previews", this);
            return;
        }
        Library.Start(Dispatcher);
        _line = new Line();
        _line.Restore();
        _line.StartPruneTimer(Dispatcher);
        Retention.Start(_line, Dispatcher);

        _screens = new ScreenHost(_line);
        _screens.Start();
        _hotkey = new HotKey();
        Action capture = () =>
        {
            if (_line != null && _screens != null) CaptureSession.Begin(_line, _screens);
        };
        Action toggle = () => _screens?.ToggleUnderCursor();
        Action gallery = () =>
        {
            if (_line != null && _screens != null) GalleryWindow.Show(_line, _screens);
        };
        GalleryWindow.Launcher = gallery;
        var taken = new List<string>();
        if (!_hotkey.TryReplace(HotKey.CaptureId, AppSettings.Current.Capture, capture, "截图"))
            taken.Add($"截图 {AppSettings.Current.Capture.Format()}");
        if (!_hotkey.TryReplace(HotKey.ToggleId, AppSettings.Current.ToggleLine, toggle, "显示/隐藏绳子"))
            taken.Add($"放下/收起绳子 {AppSettings.Current.ToggleLine.Format()}");
        if (AppSettings.Current.Gallery.IsBindable
            && !_hotkey.TryReplace(HotKey.GalleryId, AppSettings.Current.Gallery, gallery, "打开图库"))
            taken.Add($"打开图库 {AppSettings.Current.Gallery.Format()}");
        SettingsWindow.Launcher = page =>
        {
            if (_hotkey != null && _screens != null && _line != null)
                SettingsWindow.Show(_hotkey, _screens, capture, toggle, gallery, _line, page);
        };
        _tray = new TrayController(_line, _screens, capture, gallery, () => SettingsWindow.ShowPage(SettingsPage.Hotkeys));

        _shots = new ScreenshotWatcher(_line, () => _screens.PeekUnderCursor(), Dispatcher);
        _shots.Start();

        if (firstRun)
        {
            _tray.Balloon("系图已在右下角托盘运行",
                $"鼠标移到屏幕最顶边停一下，绳子就会滑下来。截图按 {AppSettings.Current.Capture.Format()}。右键托盘图标可以打开图库和设置。");
            AppSettings.Current.Save();
        }
        else if (taken.Count > 0)
        {
            _tray.Balloon("有快捷键被别的程序占用",
                string.Join("、", taken) + " 没能生效（常见是微信、QQ 的截图键）。右键托盘图标 → 设置 里换一个。");
        }
        if (firstRun && taken.Count > 0)
        {
            // 首次提示和冲突提示都要让用户看到：冲突的晚 9 秒再弹
            var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
            later.Tick += (_, _) =>
            {
                later.Stop();
                _tray?.Balloon("有快捷键被别的程序占用",
                    string.Join("、", taken) + " 没能生效（常见是微信、QQ 的截图键）。右键托盘图标 → 设置 里换一个。");
            };
            later.Start();
        }

        if (e.Args.Any(a => string.Equals(a, "--mem-report", StringComparison.OrdinalIgnoreCase)))
        {
            // 诊断：15 秒后记录托管堆与进程私有内存，强制回收前后各一次
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                Log.Info($"[内存] 托管堆 {GC.GetTotalMemory(false) / 1048576} MB，私有 {proc.PrivateMemorySize64 / 1048576} MB");
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                proc.Refresh();
                Log.Info($"[内存] 回收后 托管堆 {GC.GetTotalMemory(true) / 1048576} MB，私有 {proc.PrivateMemorySize64 / 1048576} MB，工作集 {proc.WorkingSet64 / 1048576} MB");
            };
            t.Start();
        }
        if (e.Args.Any(a => string.Equals(a, "--open-gallery", StringComparison.OrdinalIgnoreCase)))
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_line != null && _screens != null) GalleryWindow.Show(_line, _screens);
            }, DispatcherPriority.ApplicationIdle);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _line?.Save();
            Log.Info("退出");
        }
        catch (Exception ex)
        {
            Log.Error(ex.ToString());
        }
        _shots?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _screens?.Dispose();
        base.OnExit(e);
    }
}
