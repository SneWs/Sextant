namespace Sextant.Git.Tests;

public class WhitespaceDiffTests
{
    [Fact]
    public async Task Ignore_whitespace_hides_a_space_only_change()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "alpha \n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "alpha\n");
        await using var session = await Open(repo);
        var shown = await session.WorkingDiffAsync("a.txt", staged: false, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(shown);
        Assert.NotEmpty(shown.Hunks);
        var ignored = await session.WorkingDiffAsync("a.txt", staged: false, untracked: false, allowLarge: true, CancellationToken.None, ignoreWhitespace: true);
        Assert.NotNull(ignored);
        Assert.Empty(ignored.Hunks);
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
