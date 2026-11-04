using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Clothesline;

/// 界面动效。装饰性动画尊重系统「客户区动画」开关；静止时不留无限循环。
internal static class Motion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    public static readonly IEasingFunction CubicOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction QuadOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction QuadIn = new QuadraticEase { EasingMode = EasingMode.EaseIn };

    public static readonly Geometry Star = CreateStar();
    public static readonly SolidColorBrush WashBrush = Freeze(Color.FromRgb(0xF0, 0xE9, 0xDC));
    public static readonly SolidColorBrush StarDisk = Freeze(Color.FromArgb(200, 255, 255, 255));

    private static Geometry CreateStar()
    {
        const double cx = 12, cy = 12, outer = 11, inner = 4.4;
        var fig = new PathFigure { IsClosed = true, IsFilled = true };
        for (var i = 0; i < 10; i++)
        {
            var r = (i & 1) == 0 ? outer : inner;
            var a = -Math.PI / 2 + i * Math.PI / 5;
            var p = new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            if (i == 0) fig.StartPoint = p;
            else fig.Segments.Add(new LineSegment(p, true));
        }
        var geo = new PathGeometry([fig]);
        geo.Freeze();
        return geo;
    }

    public static Path MakeStar(double size, bool filled)
    {
        return new Path
        {
            Data = Star,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Fill = filled ? Theme.RedBrush : Brushes.Transparent,
            Stroke = filled ? null : Theme.MuteBrush,
            StrokeThickness = filled ? 0 : 1.4,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
    }

    public static void Fade(UIElement el, double to, int ms, Action? done = null)
        => Animate(el, UIElement.OpacityProperty, to, ms, CubicOut, decorative: true, done);

    public static void Scale(ScaleTransform t, double to, int ms, Action? done = null)
    {
        Animate(t, ScaleTransform.ScaleXProperty, to, ms, CubicOut, true, null);
        Animate(t, ScaleTransform.ScaleYProperty, to, ms, CubicOut, true, done);
    }

    public static void Move(TranslateTransform t, double x, double y, int ms, Action? done = null)
    {
        Animate(t, TranslateTransform.XProperty, x, ms, CubicOut, true, null);
        Animate(t, TranslateTransform.YProperty, y, ms, CubicOut, true, done);
    }

    public static void Shadow(DropShadowEffect fx, double blur, double depth, double opacity, int ms)
    {
        Animate(fx, DropShadowEffect.BlurRadiusProperty, blur, ms, CubicOut, true, null);
        Animate(fx, DropShadowEffect.ShadowDepthProperty, depth, ms, CubicOut, true, null);
        Animate(fx, DropShadowEffect.OpacityProperty, opacity, ms, CubicOut, true, null);
    }

    public static void Pop(ScaleTransform t, Action? done = null)
    {
        if (!Enabled)
        {
            Clear(t, ScaleTransform.ScaleXProperty, 1);
            Clear(t, ScaleTransform.ScaleYProperty, 1);
            done?.Invoke();
            return;
        }
        KeyPop(t, ScaleTransform.ScaleXProperty, null);
        KeyPop(t, ScaleTransform.ScaleYProperty, done);
    }

    public static void BreatheOpacity(UIElement el, double from, double to, double seconds)
    {
        if (!Enabled)
        {
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = to;
            return;
        }
        var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = QuadOut,
        };
        el.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    public static void StopOpacity(UIElement el, double rest = 1)
    {
        el.BeginAnimation(UIElement.OpacityProperty, null);
        el.Opacity = rest;
    }

    public static void BreatheColor(SolidColorBrush brush, Color a, Color b, double seconds)
    {
        if (!Enabled)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = a;
            return;
        }
        var anim = new ColorAnimation(a, b, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    public static void StopColor(SolidColorBrush brush, Color rest)
    {
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        brush.Color = rest;
    }

    public static void ShakeX(TranslateTransform t, Action? done = null)
    {
        if (!Enabled)
        {
            Clear(t, TranslateTransform.XProperty, 0);
            done?.Invoke();
            return;
        }
        var anim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(300),
            FillBehavior = FillBehavior.Stop,
        };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-6, KeyTime.FromPercent(0.2)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(6, KeyTime.FromPercent(0.4)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-4, KeyTime.FromPercent(0.6)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(4, KeyTime.FromPercent(0.8)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        anim.Completed += (_, _) =>
        {
            Clear(t, TranslateTransform.XProperty, 0);
            done?.Invoke();
        };
        t.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    public static void Animate(
        Animatable target,
        DependencyProperty prop,
        double to,
        int ms,
        IEasingFunction? ease,
        bool decorative,
        Action? done)
        => Run(target.BeginAnimation, target, prop, to, ms, ease, decorative, done);

    public static void Animate(
        UIElement target,
        DependencyProperty prop,
        double to,
        int ms,
        IEasingFunction? ease,
        bool decorative,
        Action? done)
        => Run(target.BeginAnimation, target, prop, to, ms, ease, decorative, done);

    public static void Clear(Animatable target, DependencyProperty prop, double value)
    {
        target.BeginAnimation(prop, null);
        target.SetValue(prop, value);
    }

    public static void Clear(UIElement target, DependencyProperty prop, double value)
    {
        target.BeginAnimation(prop, null);
        target.SetValue(prop, value);
    }

    private static void Run(
        Action<DependencyProperty, AnimationTimeline?> begin,
        DependencyObject target,
        DependencyProperty prop,
        double to,
        int ms,
        IEasingFunction? ease,
        bool decorative,
        Action? done)
    {
        if (decorative && !Enabled)
        {
            begin(prop, null);
            target.SetValue(prop, to);
            done?.Invoke();
            return;
        }
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(Math.Max(1, ms)))
        {
            EasingFunction = ease ?? CubicOut,
            FillBehavior = FillBehavior.Stop,
        };
        anim.Completed += (_, _) =>
        {
            begin(prop, null);
            target.SetValue(prop, to);
            done?.Invoke();
        };
        begin(prop, anim);
    }

    private static void KeyPop(ScaleTransform t, DependencyProperty prop, Action? done)
    {
        var anim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(260),
            FillBehavior = FillBehavior.Stop,
        };
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.2, KeyTime.FromPercent(0.55))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1))
        {
            EasingFunction = CubicOut,
        });
        anim.Completed += (_, _) =>
        {
            Clear(t, prop, 1);
            done?.Invoke();
        };
        t.BeginAnimation(prop, anim);
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
