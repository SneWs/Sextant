using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Sextant.Controls;

/// <summary>
/// Lanes on the left and the subject beside them. Branch labels, tags, and the file count float on
/// the right of the visible history view, over the subject, so a narrow column still shows them.
/// The row's width is the lanes plus a short subject, which lets a wide graph scroll sideways.
/// </summary>
public sealed class GraphRowPanel : Panel
{
    private const double SubjectReserve = 160;
    private ScrollViewer? _scroll;
    private bool _arranging;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachScroll();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachScroll();
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size available)
    {
        var lanes = LaneChild();
        var text = TextChild();
        var tags = TagChild();
        var laneWidth = 0.0;
        var laneHeight = 0.0;
        if (lanes is { IsVisible: true })
        {
            lanes.Measure(new Size(double.PositiveInfinity, available.Height));
            laneWidth = lanes.DesiredSize.Width;
            laneHeight = lanes.DesiredSize.Height;
        }

        var tagHeight = 0.0;
        if (tags is { IsVisible: true })
        {
            tags.Measure(new Size(double.PositiveInfinity, available.Height));
            tagHeight = tags.DesiredSize.Height;
        }

        text?.Measure(new Size(SubjectReserve, available.Height));
        var height = Math.Max(laneHeight, Math.Max(text?.DesiredSize.Height ?? 0, tagHeight));
        // Labels stay out of the desired width. Counting them would scroll them off a narrow pane.
        return new Size(laneWidth + SubjectReserve, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        AttachScroll();
        _arranging = true;
        try
        {
            var lanes = LaneChild();
            var text = TextChild();
            var tags = TagChild();
            var laneWidth = lanes is { IsVisible: true } ? lanes.DesiredSize.Width : 0;
            if (laneWidth > finalSize.Width)
                laneWidth = finalSize.Width;
            if (lanes is { IsVisible: true })
                lanes.Arrange(new Rect(0, 0, laneWidth, finalSize.Height));

            var visibleRight = VisibleRight(finalSize);
            var tagWidth = tags is { IsVisible: true } ? tags.DesiredSize.Width : 0;
            // A label row wider than the viewport keeps its right edge (the file count) in view.
            var tagLeft = visibleRight - tagWidth;
            if (tags is { IsVisible: true })
                tags.Arrange(new Rect(tagLeft, 0, tagWidth, finalSize.Height));
            var textWidth = Math.Max(0, finalSize.Width - laneWidth);
            text?.Arrange(new Rect(laneWidth, 0, textWidth, finalSize.Height));
            return finalSize;
        }
        finally
        {
            _arranging = false;
        }
    }

    /// <summary>The right edge of the history viewport, in this panel's coordinates.</summary>
    private double VisibleRight(Size finalSize)
    {
        if (_scroll is not { } scroll || scroll.Viewport.Width <= 1)
            return finalSize.Width;
        var origin = this.TranslatePoint(default, scroll);
        if (origin is null)
            return finalSize.Width;
        var right = scroll.Viewport.Width - origin.Value.X;
        if (double.IsNaN(right) || double.IsInfinity(right))
            return finalSize.Width;
        if (right < 0)
            return 0;
        return Math.Min(finalSize.Width, right);
    }

    private void AttachScroll()
    {
        var scroll = this.FindAncestorOfType<ScrollViewer>();
        if (ReferenceEquals(scroll, _scroll))
            return;
        DetachScroll();
        _scroll = scroll;
        if (_scroll is not null)
            _scroll.ScrollChanged += OnScrollChanged;
    }

    private void DetachScroll()
    {
        if (_scroll is null)
            return;
        _scroll.ScrollChanged -= OnScrollChanged;
        _scroll = null;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_arranging)
            return;
        if (e.OffsetDelta.X == 0 && e.ViewportDelta.X == 0)
            return;
        InvalidateArrange();
    }

    private Control? LaneChild() => Children.Count > 0 ? Children[0] : null;

    private Control? TextChild() => Children.Count > 1 ? Children[1] : null;

    private Control? TagChild() => Children.Count > 2 ? Children[2] : null;
}
