using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Orbit.Controls;

/// <summary>
/// Head-and-headphones illustration. <see cref="IsWorn"/> = true slides the headphones onto the head,
/// false lifts them off with a slight tilt, null (unknown / asleep) leaves them lifted and dimmed.
/// </summary>
public sealed partial class WearIndicator : UserControl
{
    private bool? _isWorn;
    private bool _initialised;

    public WearIndicator()
    {
        InitializeComponent();
        Apply(animate: false);
    }

    public bool? IsWorn
    {
        get => _isWorn;
        set
        {
            if (_initialised && value == _isWorn)
            {
                return;
            }

            _isWorn = value;
            Apply(animate: _initialised);
            _initialised = true;
        }
    }

    private void Apply(bool animate)
    {
        var (y, angle, opacity) = _isWorn switch
        {
            true => (0.0, 0.0, 1.0),
            false => (-26.0, -8.0, 1.0),
            null => (-26.0, -8.0, 0.35),
        };

        if (!animate)
        {
            PhonesTransform.TranslateY = y;
            PhonesTransform.Rotation = angle;
            Headphones.Opacity = opacity;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(420));
        var storyboard = new Storyboard();
        Add(storyboard, PhonesTransform, "TranslateY", y, duration, ease);
        Add(storyboard, PhonesTransform, "Rotation", angle, duration, ease);
        Add(storyboard, Headphones, "Opacity", opacity, duration, ease);
        storyboard.Begin();
    }

    private static void Add(Storyboard storyboard, DependencyObject target, string property, double to, Duration duration, EasingFunctionBase ease)
    {
        // Transform and opacity animations run on the composition thread; no dependent animation needed.
        var animation = new DoubleAnimation { To = to, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }
}
