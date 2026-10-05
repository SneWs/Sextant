using System.Text;

namespace Sextant.Git.Tests;

public class BranchTests
{
    [Fact]
    public async Task Branch_at_a_commit_stays_put_and_a_patch_is_that_commit()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var first = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("second");
        var second = repo.RunCapture("rev-parse", "HEAD").Trim();
        await using var session = await Open(repo);
        await session.CreateBranchAtAsync("older", first, CancellationToken.None);
        var state = session.Snapshot();
        Assert.Equal(second, state.Branch.Oid);
        Assert.Contains(state.Refs, reference => reference.Name == "refs/heads/older" && string.Equals(reference.Oid, first, StringComparison.OrdinalIgnoreCase));
        var patch = Encoding.UTF8.GetString(await session.FormatPatchAsync(first, CancellationToken.None));
        Assert.Contains("first", patch, StringComparison.Ordinal);
        Assert.Contains("one", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("two", patch, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Branch_from_a_tag_stays_put_and_checkout_detaches_there()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var first = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("tag", "v1");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("second");
        var second = repo.RunCapture("rev-parse", "HEAD").Trim();
        var branch = repo.CurrentBranch();
        await using var session = await Open(repo);
        await session.CreateBranchAtAsync("from-tag", "v1", CancellationToken.None);
        var created = session.Snapshot();
        Assert.Equal(branch, created.Branch.HeadName);
        Assert.Equal(second, created.Branch.Oid, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(created.Refs, reference => reference.Name == "refs/heads/from-tag" && string.Equals(reference.Oid, first, StringComparison.OrdinalIgnoreCase));

        await session.SwitchDetachAsync("v1", CancellationToken.None);
        var detached = session.Snapshot();
        Assert.True(detached.Branch.Detached);
        Assert.Equal(first, detached.Branch.Oid, StringComparer.OrdinalIgnoreCase);
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
