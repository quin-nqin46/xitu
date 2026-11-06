using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Clothesline;

/// 诊断：`Xitu.exe --render-themes <目录>` 给每个主题画一张挂着几张示例图的绳子，存成 PNG 后退出。
/// CI 在 windows-latest 上跑它并上传成产物，没有 Windows 的人也能看到 Windows 版的样子。
/// 会改写 %LOCALAPPDATA%\Xitu 下的 state.json 和 settings.json 的 Theme，只该在 CI 这类一次性环境里跑。
internal static class ThemePreview
{
    private const double Width = 1100;
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

    public static void Run(string outDir, Application app)
    {
        Directory.CreateDirectory(outDir);
        var samples = MakeSamples(Path.Combine(outDir, "_samples"));
        var themes = ThemeStore.List().Select(t => t.Id).ToList();
        Log.Info("渲染主题预览 " + string.Join(", ", themes) + " → " + outDir);
        var queue = new Queue<string>(themes);
        Next(queue, samples, outDir, app);
    }

    private static void Next(Queue<string> queue, IReadOnlyList<string> samples, string outDir, Application app)
    {
        if (queue.Count == 0)
        {
            try { Directory.Delete(Path.Combine(outDir, "_samples"), recursive: true); } catch { }
            app.Shutdown(0);
            return;
        }
        var id = queue.Dequeue();
        ThemeStore.Set(id);
        var line = new Line();
        foreach (var s in samples) line.Hang(s, quiet: true);
        var view = new LineView(line) { Width = Width, Height = Layout.PanelHeight };
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = 0,
            Top = 0,
            Width = Width,
            Height = Layout.PanelHeight,
            Content = view,
        };
        window.Show();

        // 等入场的下落和摆动停下来再拍
        var timer = new DispatcherTimer { Interval = Settle };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                Save(view, ThemeStore.Current.GalleryBg, Path.Combine(outDir, id + ".png"));
                Log.Info("主题预览 " + id);
            }
            catch (Exception ex)
            {
                Log.Error("主题预览失败 " + id + ": " + ex);
            }
            view.Detach();
            window.Close();
            Next(queue, samples, outDir, app);
        };
        timer.Start();
    }

    /// 垫一层图库底色再拍，透明窗口直接存出来是黑底。
    private static void Save(FrameworkElement view, Argb background, string path)
    {
        const double scale = 2;
        var w = (int)(view.ActualWidth * scale);
        var h = (int)(view.ActualHeight * scale);
        var bmp = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
            dc.DrawRectangle(ThemeStore.Brush(background), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
        bmp.Render(bg);
        bmp.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// 几张不同比例、不同颜色的示例图：横的、竖的、方的。
    private static IReadOnlyList<string> MakeSamples(string dir)
    {
        Directory.CreateDirectory(dir);
        var specs = new (int W, int H, Color A, Color B)[]
        {
            (1600, 900, Color.FromRgb(0x6A, 0x9B, 0xD8), Color.FromRgb(0xF2, 0xC1, 0x7A)),
            (900, 1200, Color.FromRgb(0x5C, 0x8A, 0x5A), Color.FromRgb(0xE8, 0xE2, 0xC9)),
            (1000, 1000, Color.FromRgb(0xC4, 0x5B, 0x4E), Color.FromRgb(0x2E, 0x34, 0x40)),
            (1920, 1080, Color.FromRgb(0x2E, 0x34, 0x40), Color.FromRgb(0x8F, 0xA7, 0xC9)),
        };
        var paths = new List<string>();
        for (var i = 0; i < specs.Length; i++)
        {
            var (w, h, a, b) = specs[i];
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var brush = new LinearGradientBrush(a, b, 45);
                dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), null, new Rect(w * 0.08, h * 0.1, w * 0.5, h * 0.08));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), null, new Rect(w * 0.08, h * 0.25, w * 0.7, h * 0.05));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), null, new Rect(w * 0.08, h * 0.34, w * 0.6, h * 0.05));
            }
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            var path = Path.Combine(dir, $"sample{i + 1}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var file = File.Create(path)) encoder.Save(file);
            paths.Add(path);
        }
        return paths;
    }
}
