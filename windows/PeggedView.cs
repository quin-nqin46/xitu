using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Clothesline;

/// 一张照片和顶部原木夹。倾斜是静止角，摆动叠在上面。
internal sealed class PeggedView : Canvas
{
    private readonly Line _line;
    private readonly Image _image;
    private readonly RectangleGeometry _clip;
    private readonly Grid _card;
    private readonly Border _frame;
    private readonly Border _cross;
    private readonly Border _copied;
    private readonly Border _starHost;
    private readonly Path _star;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _lift = new();
    private readonly ScaleTransform _crossScale = new(0.6, 0.6);
    private readonly TranslateTransform _copiedT = new(0, 4);
    private readonly ScaleTransform _starScale = new(1, 1);
    private readonly RotateTransform _rotate = new();
    private readonly SoftShadow _shadow;
    private readonly Clothespin _pin;
    private readonly GrabSession _grab;
    private bool _favorite;
    private bool _crossOn;

    private double _left;
    private double _top;
    private double _targetLeft;
    private double _targetTop;
    private double _vx;
    private double _vy;
    private bool _placed;

    private double _drop = -Layout.ArriveDrop;
    private double _dropVel;
    private readonly double _dropK;
    private readonly double _dropC;
    private readonly double _moveK;
    private readonly double _moveC;

    private double _swing = 16;
    private double _swingVel;
    private double _swingK = 46;
    private double _swingC = 2.6;
    private bool _easing;
    private double _easeFrom;
    private double _easeTo;
    private double _easeT;

    private bool _falling;
    private bool _hiddenForFlight;
    private double _fallElapsed;
    private double _fallSpin;
    private Action? _onFell;
    private bool _retired;
    private bool _hover;
    private bool _pressed;
    private bool _dragging;
    private bool _lifted;
    private DispatcherTimer? _copiedTimer;

    public PeggedView(Pegged item, Line line)
    {
        Item = item;
        _line = line;
        Width = Layout.CardWidth;
        var photo = Layout.PhotoSize(item.PixelWidth, item.PixelHeight);

        _image = new Image
        {
            Source = item.Thumb,
            Width = photo.Width,
            Height = photo.Height,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        var look = ThemeStore.Current;
        _clip = new RectangleGeometry(new Rect(0, 0, photo.Width, photo.Height), look.Frame.PhotoRadius, look.Frame.PhotoRadius);
        _image.Clip = _clip;

        _frame = new Border
        {
            Width = photo.Width + look.Frame.PadLeft + look.Frame.PadRight,
            Height = photo.Height + look.Frame.PadTop + look.Frame.PadBottom,
            CornerRadius = new CornerRadius(look.Frame.Radius),
            Padding = new Thickness(look.Frame.PadLeft, look.Frame.PadTop, look.Frame.PadRight, look.Frame.PadBottom),
            Background = ThemeStore.Brush(look.Frame.Paper),
            BorderThickness = new Thickness(0),
            Child = _image,
            IsHitTestVisible = false,
        };
        _shadow = new SoftShadow(_frame.Width, _frame.Height, look.Frame.Radius, blur: 14, depth: 6, opacity: 0.35);

        _cross = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = Theme.RedBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(-8, -8, 0, 0),
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _crossScale,
            Child = new TextBlock
            {
                Text = "×",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Theme.WhiteBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0),
                IsHitTestVisible = false,
            },
        };

        _copied = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, -34),
            CornerRadius = new CornerRadius(12),
            Background = Theme.RedBrush,
            Padding = new Thickness(14, 4, 14, 4),
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            IsHitTestVisible = false,
            RenderTransform = _copiedT,
            Child = new TextBlock
            {
                Text = "已复制",
                FontSize = 12,
                Foreground = Theme.WhiteBrush,
            },
        };

        // 压在卡片右上角的徽章：白描边让它在照片和桌面上都读得出来。
        _star = Motion.MakeStar(17, filled: true);
        _star.Stroke = Theme.WhiteBrush;
        _star.StrokeThickness = 1.8;
        // 星下面垫一颗下移 1 像素的淡黑星当投影（不用 DropShadowEffect，见 SoftShadow）
        var starShade = Motion.MakeStar(17, filled: true);
        starShade.Fill = Brushes.Black;
        starShade.Stroke = Brushes.Black;
        starShade.StrokeThickness = 2.4;
        starShade.Opacity = 0.18;
        starShade.RenderTransform = new TranslateTransform(0, 1);
        _starHost = new Border
        {
            Width = 17,
            Height = 17,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -7, -7, 0),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = new Grid
            {
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _starScale,
                Children = { starShade, _star },
            },
        };

        _card = new Grid
        {
            Width = _frame.Width,
            Height = _frame.Height,
            // 子元素都不接鼠标，卡片要靠自己的透明底才能被命中；没有 Background 的 Grid 对鼠标是空的。
            Background = Brushes.Transparent,
            ClipToBounds = false,
            RenderTransformOrigin = new Point(0.5, 0),
            RenderTransform = new TransformGroup { Children = { _scale, _lift } },
            Children = { _shadow, _frame, _cross, _copied, _starHost },
        };

        _pin = new Clothespin { IsHitTestVisible = false };
        Children.Add(_pin);
        Children.Add(_card);
        Fit();

        RenderTransformOrigin = new Point(0.5, 0);
        RenderTransform = _rotate;
        Opacity = 0;
        ApplyAngle();

        var drop = Swing.FromResponse(Layout.ArriveResponse, Layout.ArriveDamping);
        _dropK = drop.Stiffness;
        _dropC = drop.Damping;
        var move = Swing.FromResponse(Layout.PositionResponse, Layout.PositionDamping);
        _moveK = move.Stiffness;
        _moveC = move.Damping;

        _line.Copied += OnCopied;
        _line.Landed += OnLanded;
        _grab = new GrabSession(this, line);
        Swing.Track(this);
        RefreshFavorite(animate: false);
        if (item.IsFlying)
        {
            _drop = 0;
            _dropVel = 0;
            _swing = 0;
            _swingVel = 0;
            _hiddenForFlight = true;
            Opacity = 0;
            IsHitTestVisible = false;
        }
        else
        {
            Swing.Wake(this);
        }
    }

    public Pegged Item { get; }
    public FrameworkElement Card => _card;
    public bool IsFalling => _falling;

    public void SetPlacement(double left, double top, bool snap)
    {
        _targetLeft = left;
        _targetTop = top;
        if (snap || !_placed)
        {
            _left = left;
            _top = top;
            _vx = 0;
            _vy = 0;
            _placed = true;
            WritePosition();
            return;
        }
        if (Math.Abs(_left - left) > 0.4 || Math.Abs(_top - top) > 0.4)
            Swing.Wake(this);
    }

    public void RefreshThumb()
    {
        _image.Source = Item.Thumb;
        Fit();
        RefreshFavorite(animate: false);
    }

    /// 换了主题：夹子、相框颜色和尺寸都跟着变。
    public void ApplyTheme()
    {
        _pin.ApplyTheme();
        Fit();
    }

    /// 按当前主题和照片尺寸摆夹子和相框。几何同 Linux 版 Card.qml。
    private void Fit()
    {
        var look = ThemeStore.Current;
        var f = look.Frame;
        var photo = Layout.PhotoSize(Item.PixelWidth, Item.PixelHeight);
        _image.Width = photo.Width;
        _image.Height = photo.Height;
        _clip.Rect = new Rect(0, 0, photo.Width, photo.Height);
        _clip.RadiusX = f.PhotoRadius;
        _clip.RadiusY = f.PhotoRadius;
        _frame.Width = photo.Width + f.PadLeft + f.PadRight;
        _frame.Height = photo.Height + f.PadTop + f.PadBottom;
        _frame.Padding = new Thickness(f.PadLeft, f.PadTop, f.PadRight, f.PadBottom);
        _frame.CornerRadius = new CornerRadius(f.Radius);
        _frame.Background = ThemeStore.Brush(f.Paper);
        _shadow.Resize(_frame.Width, _frame.Height, f.Radius, 14);
        _card.Width = _frame.Width;
        _card.Height = _frame.Height;
        SetLeft(_pin, (Layout.CardWidth - _pin.Width) / 2);
        SetTop(_pin, 0);
        SetLeft(_card, (Layout.CardWidth - _card.Width) / 2);
        SetTop(_card, look.FrameTop);
        Height = look.FrameTop + _card.Height;
    }

    public void RefreshFavorite(bool animate)
    {
        var fav = Library.IsFavorite(Item.Path);
        if (fav == _favorite)
        {
            if (fav || _starHost.Visibility != Visibility.Visible || !animate) return;
        }
        var was = _favorite;
        _favorite = fav;
        if (fav)
        {
            _starHost.Visibility = Visibility.Visible;
            _star.Opacity = 1;
            if (animate && was != fav)
            {
                _starScale.ScaleX = 0;
                _starScale.ScaleY = 0;
                Motion.Pop(_starScale);
                Nudge(2.5);
            }
            else
            {
                Motion.Clear(_starScale, ScaleTransform.ScaleXProperty, 1);
                Motion.Clear(_starScale, ScaleTransform.ScaleYProperty, 1);
            }
            return;
        }
        if (animate && was)
        {
            Motion.Scale(_starScale, 0, 150);
            Motion.Fade(_star, 0, 150, () =>
            {
                if (_favorite) return;
                _starHost.Visibility = Visibility.Collapsed;
            });
            return;
        }
        Motion.Clear(_starScale, ScaleTransform.ScaleXProperty, 0);
        _starHost.Visibility = Visibility.Collapsed;
    }

    /// 拖出一份之后：照片从上方重新落回绳上，再摆几下。
    public void Rehang()
    {
        if (_falling || _retired) return;
        _drop = -Layout.ArriveDrop;
        _dropVel = 0;
        _easing = false;
        _swing = 10;
        _swingVel = 0;
        _swingK = 46;
        _swingC = 2.6;
        Opacity = 0;
        Swing.Wake(this);
    }

    public void Nudge(double degrees)
    {
        if (_falling || _retired) return;
        _easing = true;
        _easeFrom = _swing;
        _easeTo = degrees;
        _easeT = 0;
        _swingVel = 0;
        Swing.Wake(this);
    }

    public void ShowCopied()
    {
        if (_retired) return;
        _copiedTimer?.Stop();
        _copied.Visibility = Visibility.Visible;
        Motion.Fade(_copied, 1, 160);
        Motion.Move(_copiedT, 0, 0, 160);
        _copiedTimer = new DispatcherTimer { Interval = Layout.CopiedHint };
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer?.Stop();
            Motion.Fade(_copied, 0, 160, () =>
            {
                if (_retired) return;
                _copied.Visibility = Visibility.Collapsed;
                Motion.Clear(_copiedT, TranslateTransform.YProperty, 4);
            });
        };
        _copiedTimer.Start();
        Nudge(3);
    }

    public void SetHover(bool hover)
    {
        _hover = hover;
        if (_lifted) return;
        if (!_pressed && !_dragging) AnimateScale(_hover ? Layout.HoverScale : 1, 180);
        Motion.Move(_lift, 0, hover && !_pressed && !_dragging ? -2 : 0, 180);
        ApplyShadow();
        ApplyCross();
    }

    public void SetPressed(bool pressed)
    {
        _pressed = pressed;
        if (pressed) AnimateScale(Layout.PressedScale, 450);
        else AnimateScale(_hover ? Layout.HoverScale : 1, 180);
        ApplyCross();
    }

    /// 沿绳子拖动换位置：照片提起来一点。
    public void SetLifted(bool lifted)
    {
        _lifted = lifted;
        _pressed = false;
        AnimateScale(lifted ? Layout.LiftScale : _hover ? Layout.HoverScale : 1, 160);
        Motion.Move(_lift, 0, lifted ? -5 : _hover ? -2 : 0, 160);
        _shadow.To(lifted ? 10 : 6, lifted ? 0.45 : 0.35, 160);
        ApplyCross();
    }

    /// 拖动中跟手：横向位移直接跟上，并按移动量给一点惯性摆动（往右拖，下摆落后、顺时针偏）。
    public void Follow(double left, double top)
    {
        var dx = left - _left;
        _targetLeft = left;
        _targetTop = top;
        _left = left;
        _top = top;
        _vx = 0;
        _vy = 0;
        if (_easing) { _easing = false; _swingK = 38; _swingC = 2.4; }
        _swingVel = Math.Clamp(_swingVel + dx * 3, -90, 90);
        WritePosition();
        Swing.Wake(this);
    }

    public void SetDragging(bool dragging)
    {
        _dragging = dragging;
        _card.Opacity = dragging ? Layout.DragOpacity : 1;
        ApplyCross();
    }

    public void BeginFall(Action onDone)
    {
        if (_falling) return;
        _falling = true;
        _fallElapsed = 0;
        _onFell = onDone;
        IsHitTestVisible = false;
        _grab.Retire();
        ApplyCross();
        Swing.Wake(this);
    }

    public void Retire()
    {
        if (_retired) return;
        _retired = true;
        _line.Copied -= OnCopied;
        _line.Landed -= OnLanded;
        _copiedTimer?.Stop();
        _grab.Retire();
        _onFell = null;
        Swing.Untrack(this);
    }

    /// 返回 false 表示这帧之后可以退出逐帧循环。
    public bool Step(double dt)
    {
        if (_retired) return false;
        if (_hiddenForFlight)
        {
            Opacity = 0;
            return false;
        }

        if (_falling)
        {
            _fallElapsed += dt;
            var t = Math.Min(1, _fallElapsed / Layout.FallSeconds);
            var eased = t * t * t;
            var fall = eased * Layout.FallDistance;
            _fallSpin = (Item.Tilt * 7 + 20) * eased;
            Opacity = 1 - t;
            Canvas.SetLeft(this, _left);
            Canvas.SetTop(this, _top + _drop + fall);
            ApplyAngle();
            if (t < 1) return true;
            var done = _onFell;
            _onFell = null;
            done?.Invoke();
            return false;
        }

        StepSpring(ref _left, ref _vx, _targetLeft, _moveK, _moveC, dt);
        StepSpring(ref _top, ref _vy, _targetTop, _moveK, _moveC, dt);
        StepSpring(ref _drop, ref _dropVel, 0, _dropK, _dropC, dt);
        StepSwing(dt);
        Opacity = Math.Clamp(1 + _drop / Layout.ArriveDrop, 0, 1);
        WritePosition();
        ApplyAngle();

        var positionStill = Nearly(_left, _targetLeft, _vx, 0.4, 1.5);
        var dropStill = Nearly(_drop, 0, _dropVel, 0.3, 1.2);
        var swingStill = !_easing && Nearly(_swing, 0, _swingVel, 0.08, 0.2);
        return !(positionStill && dropStill && swingStill);
    }

    private void OnCopied(Guid id)
    {
        if (id == Item.Id) ShowCopied();
    }

    private void OnLanded(Guid id)
    {
        if (id != Item.Id || !_hiddenForFlight) return;
        _hiddenForFlight = false;
        IsHitTestVisible = true;
        Opacity = 1;
        Nudge(2.2);
    }

    private void StepSwing(double dt)
    {
        if (_easing)
        {
            _easeT += dt / Layout.NudgeOut.TotalSeconds;
            if (_easeT >= 1)
            {
                _swing = _easeTo;
                _swingVel = 0;
                _easing = false;
                _swingK = 38;
                _swingC = 2.4;
            }
            else
            {
                var u = 1 - Math.Pow(1 - _easeT, 3);
                _swing = _easeFrom + (_easeTo - _easeFrom) * u;
            }
            return;
        }
        StepSpring(ref _swing, ref _swingVel, 0, _swingK, _swingC, dt);
    }

    private static void StepSpring(ref double value, ref double velocity, double target, double stiffness, double damping, double dt)
    {
        var acc = -stiffness * (value - target) - damping * velocity;
        velocity += acc * dt;
        value += velocity * dt;
    }

    private static bool Nearly(double value, double target, double velocity, double valueEps, double velEps)
        => Math.Abs(value - target) < valueEps && Math.Abs(velocity) < velEps;

    private void WritePosition()
    {
        Canvas.SetLeft(this, _left);
        Canvas.SetTop(this, _top + _drop);
    }

    private void ApplyAngle() => _rotate.Angle = Item.Tilt + _swing + _fallSpin;

    private void AnimateScale(double to, int ms)
    {
        var ease = to < 1 ? Motion.QuadIn : Motion.QuadOut;
        Motion.Animate(_scale, ScaleTransform.ScaleXProperty, to, ms, ease, true, null);
        Motion.Animate(_scale, ScaleTransform.ScaleYProperty, to, ms, ease, true, null);
    }

    private void ApplyShadow()
    {
        var deep = _hover && !_dragging;
        _shadow.To(deep ? 8 : 6, deep ? 0.45 : 0.35, 180);
    }

    private void ApplyCross()
    {
        var show = _hover && !_dragging && !_pressed && !_falling && !_lifted;
        _cross.IsHitTestVisible = show;
        if (show == _crossOn) return;
        _crossOn = show;
        Motion.Fade(_cross, show ? 1 : 0, 120);
        Motion.Scale(_crossScale, show ? 1 : 0.6, 120);
    }
}

/// 夹子：按主题的 clip 画，压在相框上沿下面。同 Linux 版 Card.qml 的 clip。
internal sealed class Clothespin : Grid
{
    public Clothespin()
    {
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        var c = ThemeStore.Current.Clip;
        Children.Clear();
        Width = c.Width;
        Height = c.Height;
        if (c.Shadow)
            Children.Add(new SoftShadow(c.Width, c.Height, c.Radius, blur: 3, depth: 2, opacity: 0.25));
        Children.Add(new Border
        {
            CornerRadius = new CornerRadius(c.Radius),
            Background = ThemeStore.Brush(c.Color),
            BorderThickness = new Thickness(c.Border.IsTransparent ? 0 : 1),
            BorderBrush = c.Border.IsTransparent ? null : ThemeStore.Brush(c.Border),
        });
        if (!c.Highlight.IsTransparent)
        {
            Children.Add(new Border
            {
                Width = c.HighlightWidth,
                Height = Math.Min(c.HighlightHeight, c.Height),
                CornerRadius = new CornerRadius(c.HighlightRadius),
                Background = ThemeStore.Brush(c.Highlight),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new TranslateTransform(c.HighlightShift, 0),
            });
        }
    }
}
