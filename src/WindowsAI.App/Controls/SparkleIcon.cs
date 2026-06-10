using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace WindowsAI_App.Controls;

/// <summary>
/// The app's brand mark as a live control: a four-point concave sparkle filled with the
/// signature rainbow gradient. Built entirely in code (no XAML, no glyph font) so it scales
/// crisply at any size and matches the generated app icon exactly. When <see cref="IsActive"/>
/// is true it spins and twinkles — used on the send button while a reply streams, in the
/// overlay search pill, and as the "searching your PC" indicator.
/// </summary>
public sealed partial class SparkleIcon : ContentControl
{
    // Brand gradient stops (mirror GlowGradientBrush / the icon generator).
    private static readonly (double Offset, Color Color)[] BrandStops =
    [
        (0.00, Color.FromArgb(0xFF, 0x4F, 0x8C, 0xFF)),
        (0.30, Color.FromArgb(0xFF, 0x9B, 0x6B, 0xFF)),
        (0.55, Color.FromArgb(0xFF, 0xFF, 0x6B, 0xD6)),
        (0.80, Color.FromArgb(0xFF, 0xFF, 0x9D, 0x5C)),
        (1.00, Color.FromArgb(0xFF, 0x4D, 0xE3, 0xFF)),
    ];

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(SparkleIcon),
        new PropertyMetadata(false, static (d, e) => ((SparkleIcon)d).OnIsActiveChanged((bool)e.NewValue)));

    /// <summary>When true the sparkle spins + twinkles; when false it rests, upright and still.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(SparkleIcon),
        new PropertyMetadata(null, static (d, e) => ((SparkleIcon)d).OnFillChanged((Brush?)e.NewValue)));

    /// <summary>Override the rainbow fill with a solid brush (e.g. white on the gradient send button).</summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    private readonly RotateTransform _rotate = new() { CenterX = 0, CenterY = 0 };
    private readonly ScaleTransform _scale = new() { CenterX = 0, CenterY = 0, ScaleX = 1, ScaleY = 1 };
    private readonly Path _star;
    private Storyboard? _spin;

    public SparkleIcon()
    {
        IsTabStop = false;
        IsHitTestVisible = false;
        Width = 18;
        Height = 18;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;

        _star = new Path
        {
            Data = BuildStarGeometry(),
            Fill = BuildBrandBrush(),
            Stretch = Stretch.Uniform,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { _scale, _rotate } },
        };
        Content = _star;
    }

    private void OnFillChanged(Brush? brush) => _star.Fill = brush ?? BuildBrandBrush();

    /// <summary>Four-point concave sparkle in a 24×24 box — same shape as the generated icon.</summary>
    private static Geometry BuildStarGeometry()
    {
        const double cx = 12, cy = 12, outer = 11, inner = 3.4;
        var figure = new PathFigure { IsClosed = true, IsFilled = true };

        for (int i = 0; i < 8; i++)
        {
            double angle = (-90 + 45 * i) * Math.PI / 180;
            double r = (i % 2 == 0) ? outer : inner;
            var pt = new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
            if (i == 0)
                figure.StartPoint = pt;
            else
                figure.Segments.Add(new LineSegment { Point = pt });
        }

        return new PathGeometry { Figures = { figure } };
    }

    /// <summary>Diagonal rainbow fill matching the brand GlowGradientBrush.</summary>
    private static LinearGradientBrush BuildBrandBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        foreach (var (offset, color) in BrandStops)
            brush.GradientStops.Add(new GradientStop { Offset = offset, Color = color });
        return brush;
    }

    private void OnIsActiveChanged(bool active)
    {
        if (active)
        {
            _spin ??= BuildSpinStoryboard();
            _spin.Begin();
        }
        else
        {
            _spin?.Stop();
            _rotate.Angle = 0;
            _scale.ScaleX = _scale.ScaleY = 1;
        }
    }

    /// <summary>Continuous spin plus a gentle twinkle (scale breathes in and out).</summary>
    private Storyboard BuildSpinStoryboard()
    {
        var spin = new DoubleAnimation
        {
            To = 360,
            Duration = new Duration(TimeSpan.FromSeconds(1.1)),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Storyboard.SetTarget(spin, _rotate);
        Storyboard.SetTargetProperty(spin, "Angle");

        var twinkleX = BuildTwinkle("ScaleX");
        var twinkleY = BuildTwinkle("ScaleY");

        var board = new Storyboard { Children = { spin, twinkleX, twinkleY } };
        return board;
    }

    private DoubleAnimation BuildTwinkle(string property)
    {
        var twinkle = new DoubleAnimation
        {
            From = 0.85,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromSeconds(0.55)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(twinkle, _scale);
        Storyboard.SetTargetProperty(twinkle, property);
        return twinkle;
    }
}
