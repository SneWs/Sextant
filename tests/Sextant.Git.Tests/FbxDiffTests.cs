using System.Text;
using Sextant.Git.Models;
using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class FbxDiffTests
{
    [Fact]
    public async Task Fbx_preview_loads_both_versions()
    {
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", "false");
        var first = Encoding.ASCII.GetBytes("before-fbx");
        var second = Encoding.ASCII.GetBytes("after-fbx");
        var path = Path.Combine(repo.Directory, "hero.fbx");
        File.WriteAllBytes(path, first);
        repo.CommitAll("model");
        var parent = repo.RunCapture("rev-parse", "HEAD").Trim();
        File.WriteAllBytes(path, second);

        await using var session = await Open(repo);
        var worktree = await session.PreviewImageAsync(
            new ImageRequest("hero.fbx", "", null, false, true),
            CancellationToken.None);
        Assert.NotNull(worktree);
        Assert.Equal(first, worktree.Before);
        Assert.Equal(second, worktree.After);

        repo.CommitAll("model changed");
        var head = repo.RunCapture("rev-parse", "HEAD").Trim();
        var committed = await session.PreviewImageAsync(
            new ImageRequest("hero.fbx", parent, head, false, false),
            CancellationToken.None);
        Assert.NotNull(committed);
        Assert.Equal(first, committed.Before);
        Assert.Equal(second, committed.After);
        Assert.Null(await session.PreviewImageAsync(new ImageRequest("notes.txt", null, head, false, false), CancellationToken.None));
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
