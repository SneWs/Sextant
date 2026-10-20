using Sextant.Git.Models;
using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class StashTests
{
    [Fact]
    public async Task Stash_push_pop_and_drop_round_trip()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "two\n");
        await using var session = await Open(repo);
        await session.StashPushAsync("wip note", CancellationToken.None);
        var stashed = session.Snapshot();
        Assert.Empty(stashed.Entries);
        Assert.Single(stashed.Stashes);
        Assert.Contains("wip note", stashed.Stashes[0].Subject, StringComparison.Ordinal);
        Assert.Contains(stashed.Commits, row => row.Commit.Subject.Contains("wip note", StringComparison.Ordinal));

        await session.StashPopAsync(stashed.Stashes[0].Ref, CancellationToken.None);
        var restored = session.Snapshot();
        Assert.Empty(restored.Stashes);
        Assert.Contains(restored.Entries, entry => entry.Path == "a.txt");
        Assert.Contains("two", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stash_show_lists_tracked_edits_and_untracked_files()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "three\n");
        repo.WriteFile("new.txt", "fresh\n");
        repo.WriteFile("sub/deep.txt", "deep\n");
        await using var session = await Open(repo);
        await session.StashPushAsync("wip note", CancellationToken.None, includeUntracked: true);
        var stash = Assert.Single(session.Snapshot().Stashes);

        var files = await session.StashFilesAsync(stash.Ref, CancellationToken.None);
        Assert.NotNull(files);
        Assert.Contains(files, file => file.Path == "a.txt" && file.Kind == ChangeKind.Modified);
        Assert.Contains(files, file => file.Path == "new.txt" && file.Kind == ChangeKind.Added);
        Assert.Contains(files, file => file.Path == "sub/deep.txt" && file.Kind == ChangeKind.Added);

        var patch = await session.StashDiffAsync(stash.Ref, null, true, CancellationToken.None);
        Assert.NotNull(patch);
        Assert.Contains("three", patch.RawPatch, StringComparison.Ordinal);
        Assert.Contains("fresh", patch.RawPatch, StringComparison.Ordinal);
        Assert.Contains("deep", patch.RawPatch, StringComparison.Ordinal);

        var one = await session.StashDiffAsync(stash.Ref, "new.txt", true, CancellationToken.None);
        Assert.NotNull(one);
        Assert.Contains("fresh", one.RawPatch, StringComparison.Ordinal);
        Assert.DoesNotContain("three", one.RawPatch, StringComparison.Ordinal);
        Assert.DoesNotContain("deep", one.RawPatch, StringComparison.Ordinal);

        var stat = await session.StashStatAsync(stash.Ref, CancellationToken.None);
        Assert.Equal(3, stat.Files);
        Assert.Equal(3, stat.Added);
        Assert.Equal(1, stat.Removed);

        var commit = session.Snapshot().Commits.Single(row =>
            string.Equals(row.Commit.Sha, stash.Sha, StringComparison.OrdinalIgnoreCase));
        var parentDiff = await session.CommitFilesAsync(stash.Sha, commit.Commit.Parents[0], CancellationToken.None);
        Assert.NotNull(parentDiff);
        Assert.DoesNotContain(parentDiff, file => file.Path == "new.txt");
    }

    [Fact]
    public async Task Stash_show_lists_a_plain_edit_and_a_staged_file()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "two\n");
        repo.WriteFile("left.txt", "left\n");
        await using var session = await Open(repo);
        await session.StashPushAsync("tracked only", CancellationToken.None);
        var plain = session.Snapshot().Stashes[0];
        var plainFiles = await session.StashFilesAsync(plain.Ref, CancellationToken.None);
        Assert.NotNull(plainFiles);
        Assert.Equal(["a.txt"], plainFiles.Select(file => file.Path).ToArray());
        var plainPatch = await session.StashDiffAsync(plain.Ref, null, true, CancellationToken.None);
        Assert.NotNull(plainPatch);
        Assert.Contains("two", plainPatch.RawPatch, StringComparison.Ordinal);
        Assert.DoesNotContain("left", plainPatch.RawPatch, StringComparison.Ordinal);

        repo.WriteFile("b.txt", "staged\n");
        repo.WriteFile("c.txt", "unstaged\n");
        repo.Run("add", "b.txt");
        await session.StashPushAsync("index only", CancellationToken.None, staged: true);
        var staged = session.Snapshot().Stashes[0];
        var stagedFiles = await session.StashFilesAsync(staged.Ref, CancellationToken.None);
        Assert.NotNull(stagedFiles);
        Assert.Equal(["b.txt"], stagedFiles.Select(file => file.Path).ToArray());
        Assert.Equal(ChangeKind.Added, stagedFiles[0].Kind);
    }

    [Fact]
    public async Task Stash_push_paths_leaves_the_other_files()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.WriteFile("b.txt", "one\n");
        repo.WriteFile("c.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "A\n");
        repo.WriteFile("b.txt", "B\n");
        repo.WriteFile("c.txt", "C\n");
        repo.WriteFile("new.txt", "fresh\n");
        await using var session = await Open(repo);
        await session.StashPushAsync("picked", CancellationToken.None, includeUntracked: true, paths: ["a.txt", "new.txt"]);

        var state = session.Snapshot();
        var stash = Assert.Single(state.Stashes);
        Assert.Contains("picked", stash.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(state.Entries, entry => entry.Path is "a.txt" or "new.txt");
        Assert.Contains(state.Entries, entry => entry.Path == "b.txt");
        Assert.Contains(state.Entries, entry => entry.Path == "c.txt");
        Assert.Equal("one\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")));
        Assert.Equal("B\n", File.ReadAllText(Path.Combine(repo.Directory, "b.txt")));
        Assert.False(File.Exists(Path.Combine(repo.Directory, "new.txt")));

        var files = await session.StashFilesAsync(stash.Ref, CancellationToken.None);
        Assert.NotNull(files);
        Assert.Contains(files, file => file.Path == "a.txt");
        Assert.Contains(files, file => file.Path == "new.txt");
        Assert.DoesNotContain(files, file => file.Path is "b.txt" or "c.txt");
    }

    [Fact]
    public async Task Stash_push_path_saves_a_staged_file_and_a_partial_file()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.WriteFile("b.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "staged\n");
        repo.Run("add", "a.txt");
        repo.WriteFile("b.txt", "leave\n");
        await using var session = await Open(repo);
        await session.StashPushAsync("index file", CancellationToken.None, paths: ["a.txt"]);
        var staged = session.Snapshot();
        Assert.DoesNotContain(staged.Entries, entry => entry.Path == "a.txt");
        Assert.Contains(staged.Entries, entry => entry.Path == "b.txt" && !entry.Staged);
        Assert.Equal("one\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")));

        repo.WriteFile("a.txt", "one\nstaged-line\n");
        repo.Run("add", "a.txt");
        repo.WriteFile("a.txt", "one\nstaged-line\nunstaged-line\n");
        await session.StashPushAsync("both sides", CancellationToken.None, paths: ["a.txt"]);
        var partial = session.Snapshot();
        Assert.DoesNotContain(partial.Entries, entry => entry.Path == "a.txt");
        Assert.Contains(partial.Entries, entry => entry.Path == "b.txt");
        Assert.Equal("one\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")));
        var patch = await session.StashDiffAsync(partial.Stashes[0].Ref, "a.txt", true, CancellationToken.None);
        Assert.NotNull(patch);
        Assert.Contains("staged-line", patch.RawPatch, StringComparison.Ordinal);
        Assert.Contains("unstaged-line", patch.RawPatch, StringComparison.Ordinal);
        Assert.DoesNotContain("leave", patch.RawPatch, StringComparison.Ordinal);
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
