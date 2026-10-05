namespace Sextant.Git.Tests;

public class LineStageTests
{
    [Fact]
    public async Task Staging_one_added_line_leaves_the_other_unstaged()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "one\nalpha\nbeta\n");
        await using var session = await Open(repo);
        var diff = await session.WorkingDiffAsync("a.txt", staged: false, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(diff);
        Assert.Single(diff.Hunks);
        var beta = diff.Hunks[0].Lines.ToList().FindIndex(line => line.Kind == DiffLineKind.Added && line.Text == "beta");
        Assert.True(beta >= 0);
        await session.ApplyLineAsync(diff.RawPatch, 0, beta, reverse: false, CancellationToken.None);
        var staged = repo.RunCapture("diff", "--cached", "--", "a.txt");
        Assert.Contains("beta", staged, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha", staged, StringComparison.Ordinal);
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
