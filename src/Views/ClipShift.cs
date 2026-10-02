using Avalonia;
using Avalonia.Controls;

namespace Sextant.Views;

/// <summary>
/// Shows a wide child inside a narrow column and slides it sideways.
/// Both columns of a side-by-side diff share one shift, so they scroll together.
/// </summary>
public class ClipShift : Decorator
{
    public static readonly StyledProperty<double> ShiftProperty =
        AvaloniaProperty.Register<ClipShift, double>(nameof(Shift));

    public ClipShift()
    {
        ClipToBounds = true;
    }

    public double Shift
    {
        get => GetValue(ShiftProperty);
        set => SetValue(ShiftProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShiftProperty)
            InvalidateArrange();
    }

    protected override Size MeasureOverride(Size available)
    {
        var child = Child;
        if (child is null)
            return default;
        child.Measure(new Size(double.PositiveInfinity, available.Height));
        var width = double.IsInfinity(available.Width) ? child.DesiredSize.Width : available.Width;
        return new Size(Math.Max(0, width), child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var child = Child;
        if (child is not null)
        {
            var width = Math.Max(child.DesiredSize.Width, finalSize.Width);
            var height = Math.Max(child.DesiredSize.Height, finalSize.Height);
            child.Arrange(new Rect(-Shift, 0, width, height));
        }

        return finalSize;
    }
}
