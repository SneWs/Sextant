using System.Text;

namespace Sextant.Git.Tests;

public class SparseCheckoutTests
{
    private const string PngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Sparse_checkout_reads_excluded_blobs_and_a_new_worktree_stays_sparse()
    {
        using var repo = new TempRepo();
        repo.WriteFile("keep/file.txt", "keep");
        repo.WriteFile("skip/gone.txt", "gone");
        repo.WriteFile("skip/pic.png", Encoding.ASCII.GetString(Convert.FromBase64String(PngBase64)));
        File.WriteAllBytes(Path.Combine(repo.Directory, "skip", "pic.png"), Convert.FromBase64String(PngBase64));
        repo.CommitAll("files");
        repo.Run("sparse-checkout", "set", "keep");
        var excluded = Path.Combine(repo.Directory, "skip", "gone.txt");
        var picture = Path.Combine(repo.Directory, "skip", "pic.png");
        Assert.False(File.Exists(excluded));
        Assert.False(File.Exists(picture));

        await using var session = await Open(repo);
        Assert.True(session.Snapshot().SparseCheckout);
        var bytes = await session.ReadObjectAsync("HEAD", "skip/gone.txt", CancellationToken.None);
        Assert.Equal("gone", Encoding.UTF8.GetString(bytes!));
        Assert.False(File.Exists(excluded));

        var preview = await session.PreviewImageAsync(
            new ImageRequest("skip/pic.png", null, "HEAD", false, false),
            CancellationToken.None);
        Assert.NotNull(preview);
        Assert.NotNull(preview.After);
        Assert.True(preview.After!.Length > 8);
        Assert.Equal(0x89, preview.After[0]);
        Assert.False(File.Exists(picture));

        var extra = Path.Combine(Path.GetTempPath(), "sextant-wt-" + Guid.NewGuid().ToString("N"));
        try
        {
            await session.AddWorktreeAsync(extra, "feature", null, CancellationToken.None);
            Assert.Contains(session.Snapshot().Worktrees, tree => RepoPath.Same(tree.Path, extra));
            Assert.True(File.Exists(Path.Combine(extra, "keep", "file.txt")));
            Assert.False(File.Exists(Path.Combine(extra, "skip", "gone.txt")));
            Assert.False(File.Exists(Path.Combine(extra, "skip", "pic.png")));
        }
        finally
        {
            RemoveWorktree(repo, extra);
        }
    }

    [Fact]
    public async Task Empty_sparse_patterns_do_not_create_a_worktree()
    {
        using var repo = new TempRepo();
        repo.WriteFile("keep.txt", "keep");
        repo.CommitAll("keep");
        repo.Run("config", "core.sparseCheckout", "true");
        Directory.CreateDirectory(Path.Combine(repo.Directory, ".git", "info"));
        File.WriteAllText(Path.Combine(repo.Directory, ".git", "info", "sparse-checkout"), "");
        await using var session = await Open(repo);
        var extra = Path.Combine(Path.GetTempPath(), "sextant-wt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var error = await Assert.ThrowsAsync<RepositoryActionException>(() =>
                session.AddWorktreeAsync(extra, "feature", null, CancellationToken.None));
            Assert.Contains("no patterns", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(extra));
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
