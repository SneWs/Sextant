using Sextant.Git.Graph;
using Sextant.Git.Models;

namespace Sextant.Git.Tests;

public class LaneAssignerTests
{
    [Fact]
    public void Lane_assigner_keeps_a_line_and_a_diamond()
    {
        var lanes = new LaneAssigner();
        var linear = new[]
        {
            Commit("c", "b"),
            Commit("b", "a"),
            Commit("a"),
        };
        var nodes = linear.Select(commit => lanes.Assign(commit).NodeLane).ToArray();
        Assert.Equal([0, 0, 0], nodes);

        lanes.Reset();
        var diamond = new[]
        {
            Commit("m", "a", "b"),
            Commit("b", "r"),
            Commit("a", "r"),
            Commit("r"),
        };
        Assert.Equal([0, 1, 0, 0], diamond.Select(commit => lanes.Assign(commit).NodeLane).ToArray());

        lanes.Reset();
        var fork = new[]
        {
            Commit("tipB", "base"),
            Commit("tipA", "base"),
            Commit("base"),
        };
        Assert.Equal([0, 1, 0], fork.Select(commit => lanes.Assign(commit).NodeLane).ToArray());
    }

    private static CommitRecord Commit(string sha, params string[] parents) =>
        new(sha, parents, 0, "A", "a@b", sha);
}
