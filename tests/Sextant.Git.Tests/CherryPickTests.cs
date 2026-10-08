using Sextant.Git.Models;
using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class CherryPickTests
{
    [Fact]
    public async Task Cherry_pick_applies_the_commit_and_abort_clears_a_conflict()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var branch = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("b.txt", "from-other\n");
        repo.CommitAll("add b");
        var picked = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("switch", branch);
        await using var session = await Open(repo);
        await session.CherryPickAsync(picked, CancellationToken.None);
        Assert.Contains("from-other", File.ReadAllText(Path.Combine(repo.Directory, "b.txt")), StringComparison.Ordinal);
        Assert.Equal(SequencerKind.None, session.Snapshot().Sequencer);

        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");
        repo.Run("switch", "other");
        repo.WriteFile("a.txt", "other\n");
        repo.CommitAll("other");
        var conflict = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("switch", branch);
        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.CherryPickAsync(conflict, CancellationToken.None));
        Assert.Equal(SequencerKind.CherryPick, session.Snapshot().Sequencer);
        await session.AbortSequencerAsync(CancellationToken.None);
        Assert.Equal(SequencerKind.None, session.Snapshot().Sequencer);
        Assert.DoesNotContain(session.Snapshot().Entries, entry => entry.Kind == ChangeKind.Unmerged);
    }

    [Fact]
    public async Task Cherry_pick_continue_keeps_the_resolution()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var branch = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("a.txt", "other\n");
        repo.CommitAll("other");
        var picked = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("switch", branch);
        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");

        await using var session = await Open(repo);
        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.CherryPickAsync(picked, CancellationToken.None));
        Assert.Equal(SequencerKind.CherryPick, session.Snapshot().Sequencer);
        repo.WriteFile("a.txt", "picked\n");
        await session.StageFileAsync("a.txt", CancellationToken.None);
        await session.ContinueSequencerAsync(CancellationToken.None);

        var state = session.Snapshot();
        Assert.Equal(SequencerKind.None, state.Sequencer);
        Assert.Contains("picked", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
        Assert.Contains(state.Commits, row => row.Commit.Subject == "other");
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
