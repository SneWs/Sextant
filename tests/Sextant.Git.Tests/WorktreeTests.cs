using Sextant.Git;
using Sextant.Git.Parsing;
using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class WorktreeTests
{
    [Fact]
    public void Worktree_porcelain_splits_records()
    {
        var text = """
            worktree C:/repo
            HEAD abcdef
            branch refs/heads/main

            worktree C:/other
            HEAD 1234567
            detached
            locked busy

            """;
        var entries = WorktreeParser.Parse(text);
        Assert.Equal(2, entries.Count);
        Assert.Equal("C:/repo", entries[0].Path);
        Assert.Equal("abcdef", entries[0].Head);
        Assert.Equal("refs/heads/main", entries[0].Branch);
        Assert.False(entries[0].Detached);
        Assert.Equal("C:/other", entries[1].Path);
        Assert.True(entries[0].Main);
        Assert.False(entries[1].Main);
        Assert.True(entries[1].Detached);
        Assert.True(entries[1].Locked);
        Assert.Equal("busy", entries[1].LockReason);
        Assert.Equal(["keep", "skip"], WorktreeParser.Patterns("keep\n\n skip \n"));
    }

    [Fact]
    public async Task Worktree_add_lists_the_new_directory()
    {
        using var repo = new TempRepo();
        repo.WriteFile("keep.txt", "keep");
        repo.CommitAll("keep");
        await using var session = await Open(repo);
        Assert.Contains(session.Snapshot().Worktrees, tree => RepoPath.Same(tree.Path, repo.Directory));
        var extra = Path.Combine(Path.GetTempPath(), "sextant-wt-" + Guid.NewGuid().ToString("N"));
        try
        {
            await session.AddWorktreeAsync(extra, "feature", null, CancellationToken.None);
            Assert.Contains(session.Snapshot().Worktrees, tree => RepoPath.Same(tree.Path, extra));
            Assert.True(File.Exists(Path.Combine(extra, "keep.txt")));
            Assert.DoesNotContain(session.Snapshot().Commands, command => command.Arguments.Contains("--no-checkout"));
        }
        finally
        {
            RemoveWorktree(repo, extra);
        }
    }

    [Fact]
    public async Task Removing_a_dirty_worktree_waits_for_force_and_keeps_the_branch()
    {
        using var repo = new TempRepo();
        repo.WriteFile("keep.txt", "keep");
        repo.CommitAll("keep");
        await using var session = await Open(repo);
        var extra = NewWorktreePath();
        try
        {
            await session.AddWorktreeAsync(extra, "feature", null, CancellationToken.None);
            File.WriteAllText(Path.Combine(extra, "dirty.txt"), "dirty\n");
            var failed = await Assert.ThrowsAsync<GitCommandFailedException>(() =>
                session.RemoveWorktreeAsync(extra, force: false, locked: false, CancellationToken.None));
            Assert.True(File.Exists(Path.Combine(extra, "dirty.txt")));
            Assert.Contains("use --force", failed.StandardError, StringComparison.OrdinalIgnoreCase);

            await session.RemoveWorktreeAsync(extra, force: true, locked: false, CancellationToken.None);
            Assert.False(Directory.Exists(extra));
            Assert.DoesNotContain(session.Snapshot().Worktrees, tree => RepoPath.Same(tree.Path, extra));
            Assert.Contains("feature", repo.RunCapture("branch", "--list"), StringComparison.Ordinal);
        }
        finally
        {
            RemoveWorktree(repo, extra);
        }
    }

    [Fact]
    public async Task A_worktree_can_be_locked_moved_and_removed_while_locked()
    {
        using var repo = new TempRepo();
        repo.WriteFile("keep.txt", "keep");
        repo.CommitAll("keep");
        await using var session = await Open(repo);
        var extra = NewWorktreePath();
        var destination = extra + "-moved";
        try
        {
            await session.AddWorktreeAsync(extra, "feature", null, CancellationToken.None);
            var main = Assert.Single(session.Snapshot().Worktrees, tree => tree.Main);
            Assert.True(RepoPath.Same(main.Path, repo.Directory));

            await session.LockWorktreeAsync(extra, "away", CancellationToken.None);
            var locked = session.Snapshot().Worktrees.Single(tree => RepoPath.Same(tree.Path, extra));
            Assert.False(locked.Main);
            Assert.True(locked.Locked);
            Assert.Equal("away", locked.LockReason);

            var refused = await Assert.ThrowsAsync<GitCommandFailedException>(() =>
                session.RemoveWorktreeAsync(extra, force: true, locked: false, CancellationToken.None));
            Assert.Contains("locked", refused.StandardError, StringComparison.OrdinalIgnoreCase);
            await Assert.ThrowsAsync<GitCommandFailedException>(() =>
                session.MoveWorktreeAsync(extra, destination, CancellationToken.None));
            Assert.True(Directory.Exists(extra));

            await session.UnlockWorktreeAsync(extra, CancellationToken.None);
            Assert.False(session.Snapshot().Worktrees.Single(tree => RepoPath.Same(tree.Path, extra)).Locked);

            await session.MoveWorktreeAsync(extra, destination, CancellationToken.None);
            Assert.False(Directory.Exists(extra));
            Assert.True(File.Exists(Path.Combine(destination, "keep.txt")));

            await session.LockWorktreeAsync(destination, null, CancellationToken.None);
            var quiet = session.Snapshot().Worktrees.Single(tree => RepoPath.Same(tree.Path, destination));
            Assert.True(quiet.Locked);
            Assert.True(string.IsNullOrEmpty(quiet.LockReason));
            await session.RemoveWorktreeAsync(destination, force: true, locked: true, CancellationToken.None);
            Assert.False(Directory.Exists(destination));
        }
        finally
        {
            RemoveWorktree(repo, extra);
            RemoveWorktree(repo, destination);
        }
    }

    private static string NewWorktreePath() =>
        Path.Combine(Path.GetTempPath(), "sextant-wt-" + Guid.NewGuid().ToString("N"));

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);

    private static void RemoveWorktree(TempRepo repo, string path)
    {
        try
        {
            if (Directory.Exists(path))
                repo.Run("worktree", "remove", "--force", path);
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
