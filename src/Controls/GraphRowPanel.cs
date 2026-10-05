using Avalonia;
using Avalonia.Controls;

namespace Sextant.Controls;

/// <summary>
/// Lanes on the left, the subject beside them, and tag labels on the right. The lane column keeps
/// its width. The row asks for that width, a short subject, and the labels, so a graph with many
/// branches can scroll sideways.
/// </summary>
public sealed class GraphRowPanel : Panel
{
    private const double SubjectReserve = 160;

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

        var tagWidth = 0.0;
        var tagHeight = 0.0;
        if (tags is { IsVisible: true })
        {
            tags.Measure(new Size(double.PositiveInfinity, available.Height));
            tagWidth = tags.DesiredSize.Width;
            tagHeight = tags.DesiredSize.Height;
        }

        text?.Measure(new Size(SubjectReserve, available.Height));
        var height = Math.Max(laneHeight, Math.Max(text?.DesiredSize.Height ?? 0, tagHeight));
        return new Size(laneWidth + SubjectReserve + tagWidth, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var lanes = LaneChild();
        var text = TextChild();
        var tags = TagChild();
        var laneWidth = lanes is { IsVisible: true } ? lanes.DesiredSize.Width : 0;
        if (laneWidth > finalSize.Width)
            laneWidth = finalSize.Width;
        if (lanes is { IsVisible: true })
            lanes.Arrange(new Rect(0, 0, laneWidth, finalSize.Height));

        var tagWidth = tags is { IsVisible: true } ? tags.DesiredSize.Width : 0;
        var tagLeft = Math.Max(laneWidth, finalSize.Width - tagWidth);
        if (tags is { IsVisible: true })
            tags.Arrange(new Rect(tagLeft, 0, Math.Max(0, finalSize.Width - tagLeft), finalSize.Height));
        var textWidth = Math.Max(0, tagLeft - laneWidth);
        text?.Arrange(new Rect(laneWidth, 0, textWidth, finalSize.Height));
        return finalSize;
    }

    private Control? LaneChild() => Children.Count > 0 ? Children[0] : null;

    private Control? TextChild() => Children.Count > 1 ? Children[1] : null;

    private Control? TagChild() => Children.Count > 2 ? Children[2] : null;
}
