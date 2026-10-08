using Sextant.Git.Models;
using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class MergetoolTests
{
    [Fact]
    public async Task Custom_mergetool_stages_the_file_that_command_writes()
    {
        using var repo = new TempRepo();
        StartMerge(repo);
        await using var session = await Open(repo);
        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.MergeAsync("other", CancellationToken.None));

        await session.MergetoolAsync("a.txt", "cp \"$REMOTE\" \"$MERGED\"", CancellationToken.None);

        var state = session.Snapshot();
        Assert.Equal(SequencerKind.Merge, state.Sequencer);
        Assert.DoesNotContain(state.Entries, entry => entry.Kind == ChangeKind.Unmerged);
        Assert.Contains(state.Entries, entry => entry.Path == "a.txt" && entry.Staged);
        var text = File.ReadAllText(Path.Combine(repo.Directory, "a.txt"));
        Assert.Equal("other\n", text.Replace("\r\n", "\n"));
        Assert.False(File.Exists(Path.Combine(repo.Directory, "a.txt.orig")));
    }

    [Fact]
    public async Task Custom_mergetool_that_fails_leaves_the_conflict()
    {
        using var repo = new TempRepo();
        StartMerge(repo);
        await using var session = await Open(repo);
        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.MergeAsync("other", CancellationToken.None));

        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.MergetoolAsync("a.txt", "false", CancellationToken.None));

        var state = session.Snapshot();
        Assert.Contains(state.Entries, entry => entry.Path == "a.txt" && entry.Kind == ChangeKind.Unmerged);
        Assert.Contains("<<<<<<<", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
    }

    private static void StartMerge(TempRepo repo)
    {
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("a.txt", "other\n");
        repo.CommitAll("other");
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
