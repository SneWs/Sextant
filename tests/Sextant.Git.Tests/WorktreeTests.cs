using Sextant.Git.Parsing;

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
