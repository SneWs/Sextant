using Avalonia;
using Avalonia.Controls;

namespace Sextant.Controls;

/// <summary>
/// Lanes on the left and the subject on the right. The lane column keeps its width, and the row asks
/// for that width plus a short subject, so a graph with many branches can scroll sideways.
/// </summary>
public sealed class GraphRowPanel : Panel
{
    private const double SubjectReserve = 160;

    protected override Size MeasureOverride(Size available)
    {
        var lanes = LaneChild();
        var text = TextChild();
        var laneWidth = 0.0;
        var laneHeight = 0.0;
        if (lanes is { IsVisible: true })
        {
            lanes.Measure(new Size(double.PositiveInfinity, available.Height));
            laneWidth = lanes.DesiredSize.Width;
            laneHeight = lanes.DesiredSize.Height;
        }

        text?.Measure(new Size(SubjectReserve, available.Height));
        var height = Math.Max(laneHeight, text?.DesiredSize.Height ?? 0);
        return new Size(laneWidth + SubjectReserve, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var lanes = LaneChild();
        var text = TextChild();
        var laneWidth = lanes is { IsVisible: true } ? lanes.DesiredSize.Width : 0;
        if (laneWidth > finalSize.Width)
            laneWidth = finalSize.Width;
        if (lanes is { IsVisible: true })
            lanes.Arrange(new Rect(0, 0, laneWidth, finalSize.Height));
        var textWidth = Math.Max(0, finalSize.Width - laneWidth);
        text?.Arrange(new Rect(laneWidth, 0, textWidth, finalSize.Height));
        return finalSize;
    }

    private Control? LaneChild() => Children.Count > 0 ? Children[0] : null;

    private Control? TextChild() => Children.Count > 1 ? Children[1] : null;
}
