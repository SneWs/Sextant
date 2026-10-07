namespace Sextant.Git.Tests;

public class RepositoryFileRemovalTests
{
    [Fact]
    public void Removal_commands_use_literal_paths_and_never_recurse()
    {
        const string path = "--force [a]*.txt";
        Assert.Equal(["-C", "repo", "--no-optional-locks", "--literal-pathspecs", "ls-files", "--stage", "-z", "--", path],
            GitCommands.RepositoryFileIndex("repo", path));
        Assert.Equal(["-C", "repo", "--no-optional-locks", "--literal-pathspecs", "ls-files", "--others", "--exclude-standard", "-z", "--", path],
            GitCommands.RepositoryFileUntracked("repo", path));
        Assert.Equal(["-C", "repo", "--literal-pathspecs", "rm", "-f", "--sparse", "--", path],
            GitCommands.RemoveRepositoryFile("repo", path));
        Assert.Equal(["-C", "repo", "--literal-pathspecs", "clean", "-f", "--", path],
            GitCommands.RemoveUntrackedRepositoryFile("repo", path));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Removing_a_tracked_file_deletes_disk_and_index_and_stages_deletion(bool modified, bool staged)
    {
        using var repo = new TempRepo();
        repo.WriteFile("docs/[a].txt", "committed\n");
        repo.WriteFile("docs/a.txt", "leave this file\n");
        repo.CommitAll("base");
        var head = repo.RunCapture("rev-parse", "HEAD").Trim();
        if (modified)
            repo.WriteFile("docs/[a].txt", "edited\n");
        if (staged)
        {
            repo.Run("--literal-pathspecs", "add", "--", "docs/[a].txt");
            repo.WriteFile("docs/[a].txt", "edited again\n");
        }
        await using var session = await Open(repo);
        await session.RemoveRepositoryFileAsync("docs/[a].txt", CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(repo.Directory, "docs", "[a].txt")));
        Assert.True(File.Exists(Path.Combine(repo.Directory, "docs", "a.txt")));
        Assert.Equal("", repo.RunCapture("--literal-pathspecs", "ls-files", "--", "docs/[a].txt"));
        Assert.Contains(session.Snapshot().Entries, entry => entry.Path == "docs/[a].txt" && entry.Kind == ChangeKind.Deleted && entry.Staged);
        Assert.Equal(head, repo.RunCapture("rev-parse", "HEAD").Trim());
        Assert.Contains("committed", repo.RunCapture("show", "HEAD:docs/[a].txt"), StringComparison.Ordinal);
        Assert.DoesNotContain(await session.RepositoryFilesAsync(CancellationToken.None), file => file.Path == "docs/[a].txt");
    }

    [Fact]
    public async Task Removing_an_untracked_file_deletes_only_that_file_without_staging_anything()
    {
        using var repo = new TempRepo();
        repo.WriteFile("docs/[a].txt", "remove this file\n");
        repo.WriteFile("docs/a.txt", "leave this file\n");
        await using var session = await Open(repo);
        await session.RemoveRepositoryFileAsync("docs/[a].txt", CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(repo.Directory, "docs", "[a].txt")));
        Assert.True(File.Exists(Path.Combine(repo.Directory, "docs", "a.txt")));
        Assert.Empty(repo.RunCapture("ls-files"));
        Assert.DoesNotContain(session.Snapshot().Entries, entry => entry.Path == "docs/[a].txt");
        Assert.DoesNotContain(session.Snapshot().Entries, entry => entry.Staged);
    }

    [Fact]
    public async Task Removing_a_new_staged_file_before_the_first_commit_removes_it_completely()
    {
        using var repo = new TempRepo();
        repo.WriteFile("--force.txt", "new\n");
        repo.Run("add", "--", "--force.txt");
        await using var session = await Open(repo);
        await session.RemoveRepositoryFileAsync("--force.txt", CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(repo.Directory, "--force.txt")));
        Assert.Empty(repo.RunCapture("ls-files"));
        Assert.Empty(session.Snapshot().Entries);
        Assert.True(session.Snapshot().Branch.Unborn);
    }

    [Fact]
    public async Task Removing_a_missing_tracked_file_stages_the_deletion()
    {
        using var repo = new TempRepo();
        repo.WriteFile("gone.txt", "committed\n");
        repo.CommitAll("base");
        File.Delete(Path.Combine(repo.Directory, "gone.txt"));
        await using var session = await Open(repo);
        await session.RemoveRepositoryFileAsync("gone.txt", CancellationToken.None);

        Assert.Empty(repo.RunCapture("ls-files"));
        Assert.Contains(session.Snapshot().Entries, entry => entry.Path == "gone.txt" && entry.Staged && entry.Kind == ChangeKind.Deleted);
    }

    [Fact]
    public async Task Removing_a_sparse_excluded_file_does_not_materialize_excluded_paths()
    {
        using var repo = new TempRepo();
        repo.WriteFile("keep/file.txt", "keep\n");
        repo.WriteFile("skip/remove.txt", "remove\n");
        repo.WriteFile("skip/stay.txt", "stay\n");
        repo.CommitAll("base");
        repo.Run("sparse-checkout", "set", "--cone", "--sparse-index", "keep");
        await using var session = await Open(repo);
        await session.RemoveRepositoryFileAsync("skip/remove.txt", CancellationToken.None);

        Assert.False(Directory.Exists(Path.Combine(repo.Directory, "skip")));
        Assert.True(File.Exists(Path.Combine(repo.Directory, "keep", "file.txt")));
        Assert.DoesNotContain(await session.RepositoryFilesAsync(CancellationToken.None), file => file.Path == "skip/remove.txt");
        Assert.Contains(await session.RepositoryFilesAsync(CancellationToken.None), file => file.Path == "skip/stay.txt");
        Assert.Contains(session.Snapshot().Entries, entry => entry.Path == "skip/remove.txt" && entry.Staged);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("../outside.txt")]
    [InlineData(".git/config")]
    [InlineData("missing.txt")]
    [InlineData("docs")]
    public async Task Invalid_or_directory_paths_do_not_remove_other_files(string path)
    {
        using var repo = new TempRepo();
        repo.WriteFile("docs/file.txt", "keep\n");
        repo.CommitAll("base");
        var config = File.ReadAllBytes(Path.Combine(repo.Directory, ".git", "config"));
        await using var session = await Open(repo);
        await Assert.ThrowsAsync<RepositoryActionException>(() => session.RemoveRepositoryFileAsync(path, CancellationToken.None));

        Assert.True(File.Exists(Path.Combine(repo.Directory, "docs", "file.txt")));
        Assert.Equal(config, File.ReadAllBytes(Path.Combine(repo.Directory, ".git", "config")));
        Assert.Empty(repo.RunCapture("diff", "--cached"));
        Assert.DoesNotContain(session.Snapshot().Commands, command => command.Arguments.Contains("rm") || command.Arguments.Contains("clean"));
    }

    [Fact]
    public async Task A_tracked_file_replaced_by_a_directory_cannot_remove_the_directory()
    {
        using var repo = new TempRepo();
        repo.WriteFile("replaced.txt", "committed\n");
        repo.CommitAll("base");
        File.Delete(Path.Combine(repo.Directory, "replaced.txt"));
        repo.WriteFile("replaced.txt/keep.txt", "untracked content\n");
        await using var session = await Open(repo);
        await Assert.ThrowsAsync<RepositoryActionException>(() => session.RemoveRepositoryFileAsync("replaced.txt", CancellationToken.None));

        Assert.True(File.Exists(Path.Combine(repo.Directory, "replaced.txt", "keep.txt")));
        Assert.Contains("replaced.txt", repo.RunCapture("ls-files"), StringComparison.Ordinal);
        Assert.Empty(repo.RunCapture("diff", "--cached"));
    }

    [Fact]
    public async Task An_uninitialized_submodule_cannot_be_removed_as_a_file()
    {
        using var module = new TempRepo();
        module.WriteFile("keep.txt", "module\n");
        module.CommitAll("module");
        using var repo = new TempRepo();
        repo.Run("-c", "protocol.file.allow=always", "submodule", "add", module.Directory, "module");
        repo.CommitAll("base");
        repo.Run("submodule", "deinit", "-f", "--", "module");
        var directory = Path.Combine(repo.Directory, "module");
        if (Directory.Exists(directory))
            Directory.Delete(directory);
        await using var session = await Open(repo);
        var error = await Assert.ThrowsAsync<RepositoryActionException>(() => session.RemoveRepositoryFileAsync("module", CancellationToken.None));

        Assert.Contains("Submodules", error.Message, StringComparison.Ordinal);
        Assert.Contains("module", repo.RunCapture("ls-files"), StringComparison.Ordinal);
        Assert.Empty(repo.RunCapture("diff", "--cached"));
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
