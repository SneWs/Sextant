using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Sextant.ViewModels;

namespace Sextant.Views;

/// <summary>
/// Lays repository tabs on one row. Tabs that do not fit are kept out of the hit test
/// so a scrollbar is not drawn over them. The open tab stays on the row.
/// </summary>
public class RepositoryTabPanel : Panel
{
    private const double FallbackReserve = 32;

    private TabStrip? _strip;

    public RepositoryTabPanel()
    {
        ClipToBounds = true;
    }

    public bool HasOverflow { get; private set; }

    public IReadOnlyList<RepositoryViewModel> OverflowTabs { get; private set; } = [];

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _strip = this.FindAncestorOfType<TabStrip>();
        if (_strip is not null)
            _strip.SelectionChanged += OnSelectionChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_strip is not null)
            _strip.SelectionChanged -= OnSelectionChanged;
        _strip = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => InvalidateMeasure();

    protected override Size MeasureOverride(Size available)
    {
        double total = 0;
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, available.Height));
            total += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        var width = double.IsInfinity(available.Width) ? total : available.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = Children.Count;
        var widths = new double[count];
        double total = 0;
        for (var i = 0; i < count; i++)
        {
            widths[i] = Children[i].DesiredSize.Width;
            total += widths[i];
        }

        var overflow = count > 0 && total > finalSize.Width + 0.5;
        var space = overflow ? Math.Max(0, finalSize.Width - Reserve()) : finalSize.Width;
        var shown = Fit(widths, space, overflow, SelectedIndex());

        double x = 0;
        var hidden = new List<RepositoryViewModel>();
        for (var i = 0; i < count; i++)
        {
            var child = Children[i];
            var width = shown[i] ? widths[i] : 0;
            var room = space - x;
            if (width > room)
                width = Math.Max(0, room);
            var placed = shown[i] && width >= 1;
            if (placed)
            {
                child.Arrange(new Rect(x, 0, width, finalSize.Height));
                child.Opacity = 1;
                child.IsHitTestVisible = true;
                child.Focusable = true;
                x += width;
            }
            else
            {
                child.Arrange(default);
                child.Opacity = 0;
                child.IsHitTestVisible = false;
                child.Focusable = false;
                if (child.DataContext is RepositoryViewModel tab)
                    hidden.Add(tab);
            }
        }

        HasOverflow = hidden.Count > 0;
        OverflowTabs = hidden;
        return finalSize;
    }

    private static bool[] Fit(double[] widths, double space, bool overflow, int selected)
    {
        var shown = new bool[widths.Length];
        if (!overflow)
        {
            for (var i = 0; i < shown.Length; i++)
                shown[i] = true;
            return shown;
        }

        double used = 0;
        for (var i = 0; i < widths.Length; i++)
        {
            if (used + widths[i] > space + 0.5)
                break;
            shown[i] = true;
            used += widths[i];
        }

        if (selected < 0 || shown[selected])
            return shown;

        var need = widths[selected];
        for (var i = widths.Length - 1; i >= 0 && used + need > space + 0.5; i--)
        {
            if (!shown[i])
                continue;
            shown[i] = false;
            used -= widths[i];
        }

        shown[selected] = true;
        return shown;
    }

    private int SelectedIndex()
    {
        if (_strip?.SelectedItem is null)
            return -1;
        for (var i = 0; i < Children.Count; i++)
        {
            if (ReferenceEquals(Children[i].DataContext, _strip.SelectedItem))
                return i;
        }

        return -1;
    }

    private double Reserve()
    {
        if (this.TryFindResource("DockHeaderHeight", out var value) && value is double height && height > 0)
            return height;
        return FallbackReserve;
    }
}
