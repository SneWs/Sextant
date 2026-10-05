namespace Sextant.Git.Tests;

public class AmendTests
{
    [Fact]
    public async Task Amend_replaces_the_tip_message_and_keeps_unstaged_work()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("second");
        repo.WriteFile("a.txt", "three\n");
        repo.WriteFile("note.txt", "leave me\n");

        await using var session = await Open(repo);
        await session.StageFileAsync("a.txt", CancellationToken.None);
        await session.AmendAsync("second rewritten", CancellationToken.None);

        var state = session.Snapshot();
        Assert.Equal("second rewritten", state.Commits[0].Commit.Subject);
        Assert.Equal("three\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")).Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Contains(state.Entries, entry => entry.Path == "note.txt" && entry.Kind == ChangeKind.Untracked);
        Assert.DoesNotContain(state.Entries, entry => entry.Path == "a.txt");
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
