namespace Sextant.Git.Tests;

public class StashTests
{
    [Fact]
    public async Task Stash_push_pop_and_drop_round_trip()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "two\n");
        await using var session = await Open(repo);
        await session.StashPushAsync("wip note", CancellationToken.None);
        var stashed = session.Snapshot();
        Assert.Empty(stashed.Entries);
        Assert.Single(stashed.Stashes);
        Assert.Contains("wip note", stashed.Stashes[0].Subject, StringComparison.Ordinal);
        Assert.Contains(stashed.Commits, row => row.Commit.Subject.Contains("wip note", StringComparison.Ordinal));

        await session.StashPopAsync(stashed.Stashes[0].Ref, CancellationToken.None);
        var restored = session.Snapshot();
        Assert.Empty(restored.Stashes);
        Assert.Contains(restored.Entries, entry => entry.Path == "a.txt");
        Assert.Contains("two", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
