using Sextant.Git.Repo;

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

    [Fact]
    public async Task Restore_file_keeps_head_index_and_other_files_unchanged()
    {
        using var repo = new TempRepo();
        const string path = "file[1].txt";
        repo.WriteFile(path, "old\n");
        repo.WriteFile("file1.txt", "other old\n");
        repo.CommitAll("first");
        var first = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.WriteFile(path, "new\n");
        repo.WriteFile("file1.txt", "other new\n");
        repo.CommitAll("second");
        var head = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.WriteFile(path, "staged\n");
        repo.Run("add", "--", path);
        repo.WriteFile(path, "unstaged\n");
        repo.WriteFile("file1.txt", "other dirty\n");
        var index = repo.RunCapture("show", ":" + path);

        await using var session = await Open(repo);
        await session.RestoreFileAsync(first, path, CancellationToken.None);

        Assert.Equal(head, repo.RunCapture("rev-parse", "HEAD").Trim());
        Assert.Equal(index, repo.RunCapture("show", ":" + path));
        Assert.Equal("old\n", File.ReadAllText(Path.Combine(repo.Directory, path)));
        Assert.Equal("other dirty\n", File.ReadAllText(Path.Combine(repo.Directory, "file1.txt")));
        Assert.Equal(2, int.Parse(repo.RunCapture("rev-list", "--count", "HEAD").Trim()));
    }

    [Fact]
    public async Task Restore_file_rejects_missing_paths_and_directories_without_changing_files()
    {
        using var repo = new TempRepo();
        repo.WriteFile("dir/child.txt", "old\n");
        repo.CommitAll("first");
        var first = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.WriteFile("dir/child.txt", "new\n");
        repo.CommitAll("second");

        await using var session = await Open(repo);
        await Assert.ThrowsAsync<RepositoryActionException>(() => session.RestoreFileAsync(first, "dir", CancellationToken.None));
        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.RestoreFileAsync(first, "missing.txt", CancellationToken.None));
        Assert.Equal("new\n", File.ReadAllText(Path.Combine(repo.Directory, "dir", "child.txt")));
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
