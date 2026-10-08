using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class TagRemoteTests
{
    [Fact]
    public async Task Tag_and_remote_commands_update_refs()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var sha = repo.RunCapture("rev-parse", "HEAD").Trim();
        var other = Path.Combine(Path.GetTempPath(), "sextant-remote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(other);
        try
        {
            await using var session = await Open(repo);
            await session.CreateTagAsync("v1", sha, CancellationToken.None);
            Assert.Contains(session.Snapshot().Refs, reference => reference.Name == "refs/tags/v1");
            await session.DeleteTagAsync("v1", CancellationToken.None);
            Assert.DoesNotContain(session.Snapshot().Refs, reference => reference.Name == "refs/tags/v1");

            await session.AddRemoteAsync("origin", other, CancellationToken.None);
            Assert.Contains("origin", session.Snapshot().Remotes);
            await session.RenameRemoteAsync("origin", "upstream", CancellationToken.None);
            Assert.Contains("upstream", session.Snapshot().Remotes);
            Assert.DoesNotContain("origin", session.Snapshot().Remotes);
            await session.RemoveRemoteAsync("upstream", CancellationToken.None);
            Assert.DoesNotContain("upstream", session.Snapshot().Remotes);
        }
        finally
        {
            if (Directory.Exists(other))
                Directory.Delete(other, recursive: true);
        }
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
