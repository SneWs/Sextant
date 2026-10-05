namespace Sextant.Git.Tests;

public class FetchTests
{
    [Fact]
    public async Task Fetch_marks_upstream_commits_on_local_branches()
    {
        using var origin = new TempRepo();
        origin.WriteFile("a.txt", "one\n");
        origin.CommitAll("base");
        var main = origin.CurrentBranch();
        var clone = Path.Combine(Path.GetTempPath(), "sextant-clone-" + Guid.NewGuid().ToString("N"));
        var extra = Path.Combine(Path.GetTempPath(), "sextant-wt-" + Guid.NewGuid().ToString("N"));
        try
        {
            origin.Run("clone", origin.Directory, clone);
            origin.SetIdentity(clone);
            origin.Run("switch", "-c", "feature");
            origin.WriteFile("b.txt", "feature\n");
            origin.CommitAll("feature");
            origin.Run("switch", main);
            GitIn(origin, clone, "fetch", "origin");
            GitIn(origin, clone, "branch", "--track", "feature", "origin/feature");
            GitIn(origin, clone, "branch", "--track", "idle", "origin/" + main);
            GitIn(origin, clone, "branch", "plain");
            GitIn(origin, clone, "worktree", "add", extra, "feature");

            origin.WriteFile("a.txt", "two\n");
            origin.CommitAll("second");
            origin.Run("switch", "feature");
            origin.WriteFile("b.txt", "more\n");
            origin.CommitAll("third");
            origin.Run("switch", main);

            await using var session = await RepositorySession.OpenAsync(new GitProcessRunner(), origin.Git, clone, CancellationToken.None);
            await session.FetchAsync(null, CancellationToken.None);
            var refs = session.Snapshot().Refs;
            var current = Assert.Single(refs, reference => reference.Name == "refs/heads/" + main);
            var feature = Assert.Single(refs, reference => reference.Name == "refs/heads/feature");
            var idle = Assert.Single(refs, reference => reference.Name == "refs/heads/idle");
            var plain = Assert.Single(refs, reference => reference.Name == "refs/heads/plain");
            var remote = Assert.Single(refs, reference => reference.Name == "refs/remotes/origin/" + main);
            Assert.Equal(0, current.Ahead);
            Assert.Equal(1, current.Behind);
            Assert.Equal(0, feature.Ahead);
            Assert.Equal(1, feature.Behind);
            Assert.Equal(0, idle.Ahead);
            Assert.Equal(1, idle.Behind);
            Assert.Null(plain.Ahead);
            Assert.Null(plain.Behind);
            Assert.Null(remote.Ahead);
            Assert.Null(remote.Behind);
            Assert.Contains(session.Snapshot().Commands, command =>
                command.Arguments.Contains("refs/heads")
                && command.Arguments.Any(argument => argument.Contains("%(upstream:track)", StringComparison.Ordinal)));
        }
        finally
        {
            try
            {
                GitIn(origin, clone, "worktree", "remove", "--force", extra);
            }
            catch (InvalidOperationException)
            {
            }

            TryDeleteDirectory(extra);
            TryDeleteDirectory(clone);
        }
    }

    [Fact]
    public async Task Fetch_all_reads_every_remote_and_prune_drops_a_deleted_branch()
    {
        using var origin = new TempRepo();
        origin.WriteFile("a.txt", "one\n");
        origin.CommitAll("base");
        var main = origin.CurrentBranch();
        origin.Run("switch", "-c", "gone");
        origin.WriteFile("gone.txt", "gone\n");
        origin.CommitAll("gone");
        origin.Run("switch", main);

        using var other = new TempRepo();
        other.WriteFile("b.txt", "other\n");
        other.CommitAll("other");
        var otherBranch = other.CurrentBranch();

        var clone = Path.Combine(Path.GetTempPath(), "sextant-clone-" + Guid.NewGuid().ToString("N"));
        try
        {
            origin.Run("clone", origin.Directory, clone);
            origin.SetIdentity(clone);
            GitIn(origin, clone, "config", "fetch.prune", "false");
            GitIn(origin, clone, "remote", "add", "other", other.Directory);
            origin.Run("branch", "-D", "gone");

            await using var session = await RepositorySession.OpenAsync(new GitProcessRunner(), origin.Git, clone, CancellationToken.None);
            await session.FetchAllAsync(null, CancellationToken.None);
            var fetched = session.Snapshot().Refs;
            Assert.Contains(fetched, reference => reference.Name == "refs/remotes/other/" + otherBranch);
            Assert.Contains(fetched, reference => reference.Name == "refs/remotes/origin/gone");
            Assert.Contains(session.Snapshot().Commands, command =>
                command.Arguments.Contains("--all") && !command.Arguments.Contains("--prune"));

            await session.FetchAllPruneAsync(null, CancellationToken.None);
            var pruned = session.Snapshot().Refs;
            Assert.Contains(pruned, reference => reference.Name == "refs/remotes/other/" + otherBranch);
            Assert.DoesNotContain(pruned, reference => reference.Name == "refs/remotes/origin/gone");
            Assert.Contains(pruned, reference => reference.Name == "refs/heads/" + main);
            Assert.Contains(session.Snapshot().Commands, command =>
                command.Arguments.Contains("--all") && command.Arguments.Contains("--prune"));
        }
        finally
        {
            TryDeleteDirectory(clone);
        }
    }

    private static void GitIn(TempRepo origin, string directory, params string[] args)
    {
        var command = new List<string> { "-C", directory };
        command.AddRange(args);
        origin.Run(command.ToArray());
    }

    private static void TryDeleteDirectory(string path)
    {
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
