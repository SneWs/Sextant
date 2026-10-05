namespace Sextant.Git.Tests;

public class ResetTests
{
    [Fact]
    public async Task Mixed_reset_keeps_the_later_change_unstaged()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var first = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("second");
        await using var session = await Open(repo);
        await session.ResetAsync("--mixed", first, CancellationToken.None);
        var state = session.Snapshot();
        Assert.Equal(first, state.Branch.Oid);
        Assert.Contains(state.Entries, entry => entry.Path == "a.txt" && entry.Unstaged && !entry.Staged);
        Assert.Contains("two", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
