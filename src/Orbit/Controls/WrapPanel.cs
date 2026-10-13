using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Orbit.Controls;

/// <summary>Lays children out left to right and wraps to a new line when the row is full (used for status chips).</summary>
public sealed partial class WrapPanel : Panel
{
    public double Spacing { get; set; } = 8;

    public double LineSpacing { get; set; } = 8;

    protected override Size MeasureOverride(Size available)
    {
        double x = 0, y = 0, lineHeight = 0, width = 0;
        foreach (var child in Children.Where(c => c.Visibility == Visibility.Visible))
        {
            child.Measure(new Size(available.Width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > available.Width)
            {
                y += lineHeight + LineSpacing;
                x = 0;
                lineHeight = 0;
            }

            x += size.Width + Spacing;
            width = Math.Max(width, x - Spacing);
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return new Size(double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width), y + lineHeight);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0, y = 0, lineHeight = 0;
        foreach (var child in Children.Where(c => c.Visibility == Visibility.Visible))
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > final.Width)
            {
                y += lineHeight + LineSpacing;
                x = 0;
                lineHeight = 0;
            }

            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + Spacing;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return final;
    }
}
