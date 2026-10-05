namespace Sextant.Git.Tests;

public class RebasePlanTests
{
    [Fact]
    public void Rebase_range_is_the_straight_line_from_head_through_the_selection()
    {
        var commits = new[]
        {
            Commit("c3", "c2"),
            Commit("side", "c1"),
            Commit("c2", "c1"),
            Commit("c1"),
        };
        Assert.True(RebasePlan.TryRange(commits, "c3", ["c2"], out var range, out var error));
        Assert.Null(error);
        Assert.Equal("c1", range!.Upstream);
        Assert.Equal(["c2", "c3"], range.Steps.Select(step => step.Sha).ToArray());
        Assert.All(range.Steps, step => Assert.Equal(RebaseVerb.Pick, step.Verb));

        Assert.True(RebasePlan.TryRange(commits, "c3", ["c1"], out var rooted, out _));
        Assert.Null(rooted!.Upstream);
        Assert.Equal(["c1", "c2", "c3"], rooted.Steps.Select(step => step.Sha).ToArray());

        Assert.False(RebasePlan.TryRange(commits, "c3", ["side"], out _, out error));
        Assert.Contains("straight line", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rebase_todo_rewords_with_amend_and_refuses_a_leading_fixup()
    {
        var steps = new[]
        {
            new RebaseStep("aaa", "one", RebaseVerb.Reword, "next"),
            new RebaseStep("bbb", "two\nbad", RebaseVerb.Fixup, null),
            new RebaseStep("ccc", "three", RebaseVerb.Drop, null),
        };
        var todo = RebasePlan.Render(steps, new Dictionary<int, string> { [0] = "/tmp/my msg" });
        Assert.Contains("pick aaa one\nexec git commit --amend -F '/tmp/my msg'\n", todo, StringComparison.Ordinal);
        Assert.Contains("fixup bbb two bad\n", todo, StringComparison.Ordinal);
        Assert.Contains("drop ccc three\n", todo, StringComparison.Ordinal);
        Assert.Equal("Squash and fixup need a commit before them.", RebasePlan.Validate([new RebaseStep("bbb", "two", RebaseVerb.Fixup, null)]));
        Assert.Equal("Reword needs a message.", RebasePlan.Validate([new RebaseStep("aaa", "one", RebaseVerb.Reword, "  ")]));
    }

    [Fact]
    public void Subject_list_keeps_the_sha_and_subject()
    {
        var commits = RebasePlan.ParseSubjects("abc\u001ffirst\0def\u001fsecond\0");
        Assert.Equal([("abc", "first"), ("def", "second")], commits);
    }

    private static CommitRecord Commit(string sha, params string[] parents) =>
        new(sha, parents, 0, "A", "a@b", sha);
}
