using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Orbit.Controls;

/// <summary>
/// Head-and-headphones illustration. <see cref="IsWorn"/> = true slides the headphones onto the head,
/// false lifts them off with a slight tilt, null (unknown / asleep) leaves them lifted and dimmed.
/// Animated frame by frame on a timer with plain property values: a XAML Storyboard keeps holding its final
/// value over later changes, which left the drawing showing "on head" while the chip said "off head".
/// </summary>
public sealed partial class WearIndicator : UserControl
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(420);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private bool? _isWorn;
    private bool _initialised;
    private (double Y, double Angle, double Opacity) _from;
    private (double Y, double Angle, double Opacity) _to;
    private DateTime _start;

    public WearIndicator()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Step();
        Unloaded += (_, _) => _timer.Stop();
        _to = Target(null);
        Set(_to);
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
            _to = Target(value);
            if (!_initialised)
            {
                _initialised = true;
                _timer.Stop();
                Set(_to);
                return;
            }

            // Start from wherever the drawing is right now, so interrupted animations never jump or stick.
            _from = (PhonesTransform.TranslateY, PhonesTransform.Rotation, Headphones.Opacity);
            _start = DateTime.UtcNow;
            _timer.Start();
        }
    }

    private static (double Y, double Angle, double Opacity) Target(bool? worn) => worn switch
    {
        true => (0.0, 0.0, 1.0),
        false => (-26.0, -8.0, 1.0),
        null => (-26.0, -8.0, 0.35),
    };

    private void Step()
    {
        var t = Math.Clamp((DateTime.UtcNow - _start) / Duration, 0, 1);
        var e = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; // cubic ease-in-out
        Set((Lerp(_from.Y, _to.Y, e), Lerp(_from.Angle, _to.Angle, e), Lerp(_from.Opacity, _to.Opacity, e)));
        if (t >= 1)
        {
            _timer.Stop();
            Set(_to); // land exactly on the target
        }
    }

    private void Set((double Y, double Angle, double Opacity) v)
    {
        PhonesTransform.TranslateY = v.Y;
        PhonesTransform.Rotation = v.Angle;
        Headphones.Opacity = v.Opacity;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
