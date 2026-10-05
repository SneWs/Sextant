namespace Sextant.Git.Tests;

public class RebaseTests
{
    [Fact]
    public async Task Rebase_conflict_is_a_rebase_and_abort_clears_it()
    {
        using var repo = new TempRepo();
        StartRebase(repo);
        await using var session = await Open(repo);
        Assert.Equal(SequencerKind.Rebase, session.Snapshot().Sequencer);

        await session.AbortSequencerAsync(CancellationToken.None);
        Assert.Equal(SequencerKind.None, session.Snapshot().Sequencer);
        Assert.Contains("feature", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rebase_continue_keeps_the_resolution()
    {
        using var repo = new TempRepo();
        StartRebase(repo);
        await using var session = await Open(repo);
        repo.WriteFile("a.txt", "resolved\n");
        await session.StageFileAsync("a.txt", CancellationToken.None);
        await session.ContinueSequencerAsync(CancellationToken.None);

        var state = session.Snapshot();
        Assert.Equal(SequencerKind.None, state.Sequencer);
        Assert.Contains("resolved", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
        Assert.Contains(state.Commits, row => row.Commit.Subject == "feature");
    }

    [Fact]
    public async Task Interactive_rebase_rewords_fixup_and_drop_without_an_editor()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("one");
        repo.WriteFile("b.txt", "two\n");
        repo.CommitAll("two");
        repo.WriteFile("b.txt", "two-fix\n");
        repo.CommitAll("fix two");
        repo.WriteFile("c.txt", "drop\n");
        repo.CommitAll("drop me");

        await using var session = await Open(repo);
        var bySubject = session.Snapshot().Commits.ToDictionary(row => row.Commit.Subject, row => row.Commit);
        var steps = new[]
        {
            new RebaseStep(bySubject["one"].Sha, "one", RebaseVerb.Reword, "one renamed"),
            new RebaseStep(bySubject["two"].Sha, "two", RebaseVerb.Pick, null),
            new RebaseStep(bySubject["fix two"].Sha, "fix two", RebaseVerb.Fixup, null),
            new RebaseStep(bySubject["drop me"].Sha, "drop me", RebaseVerb.Drop, null),
        };
        await session.RebaseInteractiveAsync(bySubject["one"].Parents[0], steps, CancellationToken.None);

        var subjects = session.Snapshot().Commits.Select(row => row.Commit.Subject).ToList();
        Assert.Equal("two", subjects[0]);
        Assert.Equal("one renamed", subjects[1]);
        Assert.DoesNotContain(subjects, subject => subject is "drop me" or "fix two" or "one");
        Assert.Equal("two-fix\n", File.ReadAllText(Path.Combine(repo.Directory, "b.txt")).Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(repo.Directory, "c.txt")));
        Assert.False(File.Exists(Path.Combine(session.GitDirectory, RebaseEditor.PointerName)));
    }

    [Fact]
    public async Task Interactive_rebase_squash_keeps_the_message_you_type()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("one");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("two");

        await using var session = await Open(repo);
        var bySubject = session.Snapshot().Commits.ToDictionary(row => row.Commit.Subject, row => row.Commit);
        var steps = new[]
        {
            new RebaseStep(bySubject["one"].Sha, "one", RebaseVerb.Pick, null),
            new RebaseStep(bySubject["two"].Sha, "two", RebaseVerb.Squash, "squashed together"),
        };
        await session.RebaseInteractiveAsync(bySubject["one"].Parents[0], steps, CancellationToken.None);

        var state = session.Snapshot();
        Assert.Equal(SequencerKind.None, state.Sequencer);
        Assert.Equal("squashed together", state.Commits[0].Commit.Subject);
        Assert.Equal("two\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Interactive_rebase_edit_amends_the_stopped_commit()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        repo.WriteFile("a.txt", "edit\n");
        repo.CommitAll("edit me");

        await using var session = await Open(repo);
        var tip = session.Snapshot().Commits[0].Commit;
        await session.RebaseInteractiveAsync(
            tip.Parents[0],
            [new RebaseStep(tip.Sha, tip.Subject, RebaseVerb.Edit, null)],
            CancellationToken.None);
        Assert.Equal(SequencerKind.Rebase, session.Snapshot().Sequencer);

        repo.WriteFile("a.txt", "edited\n");
        await session.StageFileAsync("a.txt", CancellationToken.None);
        await session.AmendAsync("edited message\n", CancellationToken.None);
        await session.ContinueSequencerAsync(CancellationToken.None);

        var state = session.Snapshot();
        Assert.Equal(SequencerKind.None, state.Sequencer);
        Assert.Equal("edited message", state.Commits[0].Commit.Subject);
        Assert.Contains("edited", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(session.GitDirectory, RebaseEditor.PointerName)));
    }

    private static void StartRebase(TempRepo repo)
    {
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "feature");
        repo.WriteFile("a.txt", "feature\n");
        repo.CommitAll("feature");
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");
        repo.Run("switch", "feature");
        Assert.Throws<InvalidOperationException>(() => repo.Run("rebase", trunk));
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
