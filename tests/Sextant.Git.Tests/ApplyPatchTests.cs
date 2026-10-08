using Sextant.Git.Models;
using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class ApplyPatchTests
{
    [Fact]
    public async Task Apply_patch_uses_a_saved_commit_patch_and_leaves_it_unstaged()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var first = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("second");
        var second = repo.RunCapture("rev-parse", "HEAD").Trim();
        await using var session = await Open(repo);
        var bytes = await session.FormatPatchAsync(second, CancellationToken.None);
        var patch = Path.Combine(Path.GetTempPath(), "sextant-apply-" + Guid.NewGuid().ToString("N") + ".patch");
        try
        {
            await File.WriteAllBytesAsync(patch, bytes);
            repo.Run("reset", "--hard", first);
            await session.ApplyPatchFileAsync(patch, CancellationToken.None);
            Assert.Equal("two\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")).Replace("\r\n", "\n", StringComparison.Ordinal));
            var state = session.Snapshot();
            Assert.Equal(first, state.Branch.Oid);
            Assert.Contains(state.Entries, entry => entry.Path == "a.txt" && entry.Unstaged && !entry.Staged);
            Assert.Equal(SequencerKind.None, state.Sequencer);
        }
        finally
        {
            try
            {
                File.Delete(patch);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Apply_patch_refuses_a_diff_that_does_not_match()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var patch = Path.Combine(Path.GetTempPath(), "sextant-apply-" + Guid.NewGuid().ToString("N") + ".patch");
        try
        {
            await File.WriteAllTextAsync(patch, """
                diff --git a/a.txt b/a.txt
                --- a/a.txt
                +++ b/a.txt
                @@ -1 +1 @@
                -zzz
                +two
                """);
            await using var session = await Open(repo);
            await Assert.ThrowsAsync<GitCommandFailedException>(() => session.ApplyPatchFileAsync(patch, CancellationToken.None));
            Assert.Equal("one\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")).Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.Empty(session.Snapshot().Entries);
            Assert.Equal(SequencerKind.None, session.Snapshot().Sequencer);
        }
        finally
        {
            try
            {
                File.Delete(patch);
            }
            catch (IOException)
            {
            }
        }
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
