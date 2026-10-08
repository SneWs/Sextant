namespace Sextant.Git.Models;

public sealed class LaneGeometry
{
    public required int NodeLane { get; init; }

    public required int LaneCount { get; init; }

    public required IReadOnlyList<int> IncomingLanes { get; init; }

    public required IReadOnlyList<int> ThroughLanes { get; init; }

    public required IReadOnlyList<LaneEdge> Edges { get; init; }
}