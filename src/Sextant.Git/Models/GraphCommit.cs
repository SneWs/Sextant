namespace Sextant.Git.Models;

public sealed class GraphCommit
{
    public required CommitRecord Commit { get; init; }

    public required LaneGeometry Lanes { get; init; }
}