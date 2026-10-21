using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering;

namespace Sextant;

/// <summary>
/// Places minimize, maximize, and close on the sides named by a GNOME button layout.
/// A button the layout omits stays hidden. A button the window cannot offer stays hidden too.
/// Empty space is not hit-testable, so a drag there still reaches the title bar underneath.
/// </summary>
public class GnomeCaptionBar : Panel, ICustomHitTest
{
    public static readonly StyledProperty<string?> ButtonLayoutProperty =
        AvaloniaProperty.Register<GnomeCaptionBar, string?>(nameof(ButtonLayout));

    public GnomeCaptionBar()
    {
        Children.CollectionChanged += (_, _) =>
        {
            ApplyVisibility();
            InvalidateMeasure();
        };
    }

    public string? ButtonLayout
    {
        get => GetValue(ButtonLayoutProperty);
        set => SetValue(ButtonLayoutProperty, value);
    }

    public bool HitTest(Point point)
    {
        foreach (var child in Children)
        {
            if (child.IsVisible && child.Bounds.Contains(point))
                return true;
        }

        return false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ButtonLayoutProperty)
            return;
        ApplyVisibility();
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0d;
        var height = 0d;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (!child.IsVisible)
                continue;
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var layout = GnomeButtonLayout.Parse(ButtonLayout);
        var left = Buttons(layout.Left);
        var right = Buttons(layout.Right);
        var placed = new HashSet<Control>();
        var x = 0d;
        foreach (var child in left)
        {
            x += Place(child, x, finalSize.Height);
            placed.Add(child);
        }

        var rightWidth = right.Sum(child => child.DesiredSize.Width);
        var rx = Math.Max(x, finalSize.Width - rightWidth);
        foreach (var child in right)
        {
            rx += Place(child, rx, finalSize.Height);
            placed.Add(child);
        }

        foreach (var child in Children)
        {
            if (!placed.Contains(child))
                child.Arrange(default);
        }

        return finalSize;
    }

    private void ApplyVisibility()
    {
        var layout = GnomeButtonLayout.Parse(ButtonLayout);
        var shown = new HashSet<string>(layout.Left, StringComparer.Ordinal);
        shown.UnionWith(layout.Right);
        foreach (var child in Children)
        {
            var token = Token(child);
            if (token is null)
                continue;
            if (shown.Contains(token))
                child.ClearValue(IsVisibleProperty);
            else
                child.IsVisible = false;
        }
    }

    private List<Control> Buttons(IReadOnlyList<string> tokens)
    {
        var buttons = new List<Control>(tokens.Count);
        foreach (var token in tokens)
        {
            var child = Children.FirstOrDefault(control => Token(control) == token);
            if (child is { IsVisible: true })
                buttons.Add(child);
        }

        return buttons;
    }

    private static double Place(Control child, double x, double height)
    {
        var size = child.DesiredSize;
        var y = Math.Max(0, (height - size.Height) / 2);
        child.Arrange(new Rect(x, y, size.Width, size.Height));
        return size.Width;
    }

    private static string? Token(Control child) => child.Name switch
    {
        "PART_MinimizeButton" => "minimize",
        "PART_MaximizeButton" => "maximize",
        "PART_CloseButton" => "close",
        _ => null,
    };
}
