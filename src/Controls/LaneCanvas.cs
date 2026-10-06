using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Sextant.Git;

namespace Sextant.Controls;

public sealed class LaneCanvas : Control
{
    private const double Pitch = 10;
    // Immutable brushes have no dispatcher. A SolidColorBrush is owned by the thread that created it,
    // and a later headless session renders this canvas on a different thread.
    private static readonly ImmutableSolidColorBrush[] Palette =
    [
        new(Color.Parse("#4C78A8")),
        new(Color.Parse("#F58518")),
        new(Color.Parse("#54A24B")),
        new(Color.Parse("#E45756")),
        new(Color.Parse("#72B7B2")),
        new(Color.Parse("#B279A2")),
        new(Color.Parse("#EECA3B")),
        new(Color.Parse("#FF9DA6")),
    ];

    private static readonly ImmutablePen[] Pens =
    [
        new(Palette[0], 1.4),
        new(Palette[1], 1.4),
        new(Palette[2], 1.4),
        new(Palette[3], 1.4),
        new(Palette[4], 1.4),
        new(Palette[5], 1.4),
        new(Palette[6], 1.4),
        new(Palette[7], 1.4),
    ];

    public static readonly StyledProperty<LaneGeometry?> GeometryProperty =
        AvaloniaProperty.Register<LaneCanvas, LaneGeometry?>(nameof(Geometry));

    static LaneCanvas()
    {
        AffectsRender<LaneCanvas>(GeometryProperty);
        AffectsMeasure<LaneCanvas>(GeometryProperty);
    }

    public LaneGeometry? Geometry
    {
        get => GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    protected override Size MeasureOverride(Size available)
    {
        var count = Math.Max(1, Geometry?.LaneCount ?? 1);
        var height = double.IsFinite(available.Height) && available.Height > 0 ? available.Height : 0;
        return new Size(8 + (count * Pitch), height);
    }

    public override void Render(DrawingContext context)
    {
        var geometry = Geometry;
        if (geometry is null)
            return;

        var mid = Bounds.Height / 2;
        foreach (var lane in geometry.ThroughLanes)
            context.DrawLine(Pen(lane), new Point(X(lane), 0), new Point(X(lane), Bounds.Height));
        foreach (var lane in geometry.IncomingLanes)
            context.DrawLine(Pen(lane), new Point(X(lane), 0), new Point(X(geometry.NodeLane), mid));
        foreach (var edge in geometry.Edges)
            context.DrawLine(Pen(edge.To), new Point(X(edge.From), mid), new Point(X(edge.To), Bounds.Height));
        context.DrawEllipse(Fill(geometry.NodeLane), null, new Point(X(geometry.NodeLane), mid), 3.5, 3.5);
    }

    private static double X(int lane) => 6 + (lane * Pitch);

    private static int Index(int lane) => Math.Abs(lane) % Palette.Length;

    private static IPen Pen(int lane) => Pens[Index(lane)];

    private static IBrush Fill(int lane) => Palette[Index(lane)];
}
