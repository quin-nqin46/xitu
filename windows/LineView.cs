using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Clothesline;

/// 一条绳子上的照片排列。同一张照片的视图按 Id 留着，才能播摆动和下落。
internal sealed class LineView : Canvas
{
    private readonly Line _line;
    private readonly RopeElement _rope = new();
    private readonly Border _hint;
    private readonly TextBlock _hintText;
    private bool _dropping;
    private readonly List<PeggedView> _pegs = new();
    private readonly List<PeggedView> _falling = new();

    public LineView(Line line)
    {
        _line = line;
        Background = Brushes.Transparent;
        _hint = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = Theme.PaperBrush,
            Padding = new Thickness(12, 6, 12, 6),
            IsHitTestVisible = false,
        };
        _hintText = new TextBlock
        {
            Text = IdleHint,
            FontSize = 12,
            Foreground = Theme.InkBrush,
        };
        _hint.Child = _hintText;

        // 从别处拖图片进来。
        AllowDrop = true;
        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => EndDropHover();
        Drop += OnDrop;

        Children.Add(_rope);
        Children.Add(_hint);
        _line.Changed += OnChanged;
        _line.Removing += OnRemoving;
        _line.Relocated += OnRelocated;
        SizeChanged += (_, _) => Sync(snapNew: false);
        Swing.HostCreated();
        Sync(snapNew: true);
    }

    public void Detach()
    {
        _line.Changed -= OnChanged;
        _line.Removing -= OnRemoving;
        _line.Relocated -= OnRelocated;
        foreach (var peg in _pegs) peg.Retire();
        foreach (var peg in _falling) peg.Retire();
        _pegs.Clear();
        _falling.Clear();
        Swing.HostReleased();
    }

    private PeggedView? _moving;
    private int _movingIndex;

    /// 沿绳子拖动换位置：被拖的照片跟手，其余照片按预想的新顺序让位，松手才真正改顺序。
    public void BeginMove(PeggedView peg)
    {
        _moving = peg;
        _movingIndex = Math.Max(0, _line.Items.ToList().FindIndex(i => i.Id == peg.Item.Id));
        SetZIndex(peg, 1);
    }

    /// x 是照片中心在本视图里的横坐标。
    public void UpdateMove(double x)
    {
        if (_moving == null) return;
        var width = ActualWidth;
        var count = _line.Items.Count;
        if (width <= 1 || count == 0) return;
        var first = Layout.X(0, count, width);
        var index = (int)Math.Round((x - first) / Layout.Spacing);
        _movingIndex = Math.Clamp(index, 0, count - 1);

        x = Math.Clamp(x, first - Layout.Spacing * 0.6, Layout.X(count - 1, count, width) + Layout.Spacing * 0.6);
        _moving.Follow(x - Layout.CardWidth / 2, Layout.RopeY(x, width) - Layout.PinAbove);

        var others = _line.Items.Where(i => i.Id != _moving.Item.Id).ToList();
        for (var i = 0; i < others.Count; i++)
        {
            var slot = i < _movingIndex ? i : i + 1;
            var peg = _pegs.FirstOrDefault(p => p.Item.Id == others[i].Id);
            if (peg == null) continue;
            var px = Layout.X(slot, count, width);
            peg.SetPlacement(px - Layout.CardWidth / 2, Layout.RopeY(px, width) - Layout.PinAbove, snap: false);
        }
    }

    /// commit 为假：拖出绳子去别的程序了，照片回原位。
    public void EndMove(bool commit)
    {
        var peg = _moving;
        if (peg == null) return;
        _moving = null;
        SetZIndex(peg, 0);
        if (commit) _line.Move(peg.Item.Id, _movingIndex);
        Sync(snapNew: false);
    }

    /// 绳子滑到位时照片顺势晃一下（原版没有，只靠 7–16 秒一次的微风，拉下来常是静止的）。
    /// 比微风幅度小、方向随机、各张错开一点，免得整排同步像一块板。
    public void Sway()
    {
        if (!Motion.Enabled) return;
        foreach (var peg in _pegs.ToArray())
        {
            var degrees = (1.2 + Random.Shared.NextDouble() * 1.2) * (Random.Shared.Next(2) == 0 ? -1 : 1);
            var delay = Random.Shared.NextDouble() * 0.12;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(0.001, delay)) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (!peg.IsFalling) peg.Nudge(degrees);
            };
            timer.Start();
        }
    }

    public bool HitPhoto(int screenX, int screenY)
    {
        foreach (var peg in _pegs)
        {
            if (peg.Card.ActualWidth <= 0 || !peg.Card.IsVisible) continue;
            try
            {
                var origin = peg.Card.PointToScreen(new Point(0, 0));
                var far = peg.Card.PointToScreen(new Point(peg.Card.ActualWidth, peg.Card.ActualHeight));
                var rect = new Rect(origin, far);
                rect.Inflate(12, 12);
                if (rect.Contains(screenX, screenY)) return true;
            }
            catch (InvalidOperationException)
            {
                // 尚未挂到可视树上。
            }
        }
        return false;
    }

    private void OnChanged(object? sender, EventArgs e) => Sync(snapNew: false);

    private void OnRelocated(Pegged item)
    {
        var peg = _pegs.FirstOrDefault(p => p.Item.Id == item.Id);
        peg?.RefreshFavorite(animate: true);
    }

    private void OnRemoving(Pegged item) => StartFall(item.Id);

    private void StartFall(Guid id)
    {
        var peg = _pegs.FirstOrDefault(p => p.Item.Id == id);
        if (peg == null || peg.IsFalling) return;
        _pegs.Remove(peg);
        _falling.Add(peg);
        peg.BeginFall(() =>
        {
            Children.Remove(peg);
            _falling.Remove(peg);
            peg.Retire();
            UpdateHint();
        });
        UpdateHint();
    }

    private void Sync(bool snapNew)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1) return;

        SetLeft(_rope, 0);
        SetTop(_rope, 0);
        _rope.Width = width;
        _rope.Height = height;

        var items = _line.Items;
        var ids = new HashSet<Guid>(items.Select(i => i.Id));
        foreach (var peg in _pegs.ToList())
        {
            if (!ids.Contains(peg.Item.Id)) StartFall(peg.Item.Id);
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var peg = _pegs.FirstOrDefault(p => p.Item.Id == item.Id);
            var created = false;
            if (peg == null)
            {
                peg = new PeggedView(item, _line);
                Children.Add(peg);
                _pegs.Add(peg);
                created = true;
            }
            else
            {
                peg.RefreshThumb();
                peg.RefreshFavorite(animate: false);
            }

            if (ReferenceEquals(peg, _moving)) continue; // 正被拖着，位置跟手

            var x = Layout.X(i, items.Count, width);
            var ropeY = Layout.RopeY(x, width);
            peg.SetPlacement(x - Layout.CardWidth / 2, ropeY - Layout.PinAbove, snap: snapNew || created);
        }

        UpdateHint();
    }

    private const string IdleHint = "截一张图，它会挂在这里";

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var ok = DropIn.Accepts(e.Data);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        DropIn.IsHovering = ok;
        if (ok == _dropping) return;
        _dropping = ok;
        _hintText.Text = ok ? "松开，挂到绳上" : IdleHint;
        UpdateHint();
        if (ok) Motion.BreatheOpacity(_hint, 0.85, 1, 1.2);
        else
        {
            Motion.StopOpacity(_hint, 1);
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var ok = DropIn.Accepts(e.Data);
        EndDropHover();
        if (!ok) return;
        if (DropIn.Take(e.Data, _line) > 0) ScreenHost.HoldRevealed(Layout.PeekDuration);
    }

    private void EndDropHover()
    {
        DropIn.IsHovering = false;
        Motion.StopOpacity(_hint, 1);
        if (!_dropping) return;
        _dropping = false;
        _hintText.Text = IdleHint;
        UpdateHint();
    }

    private void UpdateHint()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1) return;
        _hint.Visibility = _dropping || (_line.Items.Count == 0 && _falling.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        _hint.Measure(new Size(width, height));
        var mid = width / 2;
        SetLeft(_hint, mid - _hint.DesiredSize.Width / 2);
        SetTop(_hint, Layout.RopeY(mid, width) + 34);
    }
}
