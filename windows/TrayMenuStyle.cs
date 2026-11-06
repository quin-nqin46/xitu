// 移植自 alejandrobujan/tendedero（MIT）。名字与图标不在授权范围内。

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace Clothesline;

/// 托盘菜单的系图样式：米白底、圆角、悬停浅红、红色勾、快捷键灰字靠右。
internal static class TrayMenuStyle
{
    public static readonly Color Paper = Color.FromArgb(0xF6, 0xF1, 0xE7);
    public static readonly Color Ink = Color.FromArgb(0x2E, 0x34, 0x40);
    public static readonly Color Muted = Color.FromArgb(0x8A, 0x84, 0x78);
    public static readonly Color Red = Color.FromArgb(0xB5, 0x30, 0x2A);
    public static readonly Color Hover = Color.FromArgb(0xEF, 0xDD, 0xD6);
    public static readonly Color Rule = Color.FromArgb(0xE3, 0xDA, 0xC8);
    public static readonly Color Border = Color.FromArgb(0xE0, 0xD7, 0xC6);

    public static void Apply(WinForms.ContextMenuStrip menu)
    {
        menu.Renderer = new Renderer();
        menu.BackColor = Paper;
        menu.ForeColor = Ink;
        menu.Font = new Font("Microsoft YaHei UI", 9.75f);
        // 不留左侧勾选栏，所有文字从同一条左边线开始；勾画在行尾。
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = false;
        menu.Padding = new WinForms.Padding(4, 4, 4, 4);
        menu.DropShadowEnabled = true;
        menu.HandleCreated += (_, _) => RoundCorners(menu.Handle);
    }

    /// 子菜单（如「换主题」）和主菜单同一套样式。
    public static void ApplyDropDown(WinForms.ToolStripDropDownMenu menu)
    {
        menu.Renderer = new Renderer();
        menu.BackColor = Paper;
        menu.ForeColor = Ink;
        menu.Font = new Font("Microsoft YaHei UI", 9.75f);
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = false;
        menu.Padding = new WinForms.Padding(4, 4, 4, 4);
        menu.DropShadowEnabled = true;
        menu.HandleCreated += (_, _) => RoundCorners(menu.Handle);
    }

    public static void Item(WinForms.ToolStripItem item)
    {
        item.Padding = new WinForms.Padding(2, 4, 2, 4);
        item.Margin = WinForms.Padding.Empty;
        item.ForeColor = Ink;

    }

    /// 标题行：不可点的菜单项，和其他项共用同一套排版，左边线才对得齐。
    public static WinForms.ToolStripMenuItem Header(string text) => new(text)
    {
        Enabled = false,
        Tag = HeaderTag,
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
    };

    private const string HeaderTag = "header";

    /// Windows 11 上让 DWM 给弹出菜单画圆角和米色描边；旧系统上这两个属性无效，忽略失败。
    private static void RoundCorners(IntPtr hwnd)
    {
        var round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        var border = Border.R | (Border.G << 8) | (Border.B << 16); // COLORREF 是 BGR
        DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private sealed class Renderer : WinForms.ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(WinForms.ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Paper);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        // 描边交给 DWM；不支持圆角的系统上就是一块米白方块，也不难看。
        protected override void OnRenderToolStripBorder(WinForms.ToolStripRenderEventArgs e) { }

        protected override void OnRenderImageMargin(WinForms.ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(WinForms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            using var path = Rounded(r, 6);
            using var brush = new SolidBrush(Hover);
            g.FillPath(brush, path);
        }

        protected override void OnRenderItemBackground(WinForms.ToolStripItemRenderEventArgs e) { }

        protected override void OnRenderLabelBackground(WinForms.ToolStripItemRenderEventArgs e) { }

        protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
        {
            var isShortcut = e.Item is WinForms.ToolStripMenuItem mi
                && !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString)
                && e.Text == mi.ShortcutKeyDisplayString;
            e.TextColor = Equals(e.Item.Tag, HeaderTag) ? Red
                : !e.Item.Enabled ? Color.FromArgb(0xB8, 0xB1, 0xA4)
                : isShortcut ? Muted
                : Ink;
            if (isShortcut)
            {
                using var small = new Font(e.TextFont!.FontFamily, e.TextFont.Size - 0.75f);
                var box = new Rectangle(0, 0, e.Item.Width - RightInset, e.Item.Height);
                WinForms.TextRenderer.DrawText(e.Graphics, e.Text, small, box, e.TextColor,
                    WinForms.TextFormatFlags.Right | WinForms.TextFormatFlags.VerticalCenter | WinForms.TextFormatFlags.SingleLine);
                return;
            }
            var left = new Rectangle(LeftInset, 0, e.Item.Width - LeftInset, e.Item.Height);
            WinForms.TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, left, e.TextColor,
                WinForms.TextFormatFlags.Left | WinForms.TextFormatFlags.VerticalCenter | WinForms.TextFormatFlags.SingleLine);
            if (e.Item is WinForms.ToolStripMenuItem { Checked: true }) DrawCheck(e.Graphics, e.Item);
        }

        protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using var pen = new Pen(Rule, 1);
            e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderItemCheck(WinForms.ToolStripItemImageRenderEventArgs e) { }

        private const int LeftInset = 14;
        private const int RightInset = 14;

        private static void DrawCheck(Graphics g, WinForms.ToolStripItem item)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var size = (int)(item.Height * 0.42);
            var r = new Rectangle(item.Width - RightInset - size, (item.Height - size) / 2, size, size);
            var cx = r.Left + r.Width / 2f;
            var cy = r.Top + r.Height / 2f;
            var s = Math.Min(r.Width, r.Height) / 2f;
            using var pen = new Pen(Red, Math.Max(1.6f, s / 3.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[]
            {
                new PointF(cx - s * 0.62f, cy + s * 0.02f),
                new PointF(cx - s * 0.18f, cy + s * 0.46f),
                new PointF(cx + s * 0.66f, cy - s * 0.48f),
            });
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
