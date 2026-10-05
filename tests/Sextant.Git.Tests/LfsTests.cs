using System.Diagnostics;
using System.Text;
using Sextant.Git.Parsing;

namespace Sextant.Git.Tests;

public class LfsTests
{
    [Fact]
    public void Check_attr_keeps_only_lfs_filters()
    {
        var output = "keep.txt\0filter\0unspecified\0dir/a.bin\0filter\0lfs\0"u8;
        var tracked = CheckAttrParser.LfsTracked(output);
        Assert.Equal(["dir/a.bin"], tracked);
        Assert.True(CheckAttrParser.IsTracked(tracked, "dir/a.bin", null));
        Assert.True(CheckAttrParser.IsTracked(tracked, "c.txt", "dir/a.bin"));
        Assert.False(CheckAttrParser.IsTracked(tracked, "keep.txt", null));
        Assert.Empty(CheckAttrParser.LfsTracked("keep.txt\0filter\0unspecified"u8));
        Assert.Empty(CheckAttrParser.LfsTracked("dir/a.bin\0filter"u8));
        Assert.Equal(["dir/a.bin"], CheckAttrParser.LfsTracked("dir/a.bin\0filter\0lfs\0trailing"u8));
    }

    [Fact]
    public void Lfs_pointer_parses_and_a_diff_keeps_both_sides()
    {
        var before = "version https://git-lfs.github.com/spec/v1\noid sha256:aaaa\nsize 4\n";
        var after = "version https://git-lfs.github.com/spec/v1\noid sha256:bbbb\nsize 8\n";
        Assert.True(LfsPointers.TryParse(before, out var pointer));
        Assert.Equal("sha256:aaaa", pointer!.Oid);
        Assert.Equal(4, pointer.Size);
        Assert.Equal(before, pointer.Render());

        var document = new DiffDocument(false, false, false, false, false,
        [
            new DiffHunk(1, 3, 1, 3, "@@ -1,3 +1,3 @@",
            [
                new DiffLine(DiffLineKind.Removed, "version https://git-lfs.github.com/spec/v1"),
                new DiffLine(DiffLineKind.Removed, "oid sha256:aaaa"),
                new DiffLine(DiffLineKind.Removed, "size 4"),
                new DiffLine(DiffLineKind.Added, "version https://git-lfs.github.com/spec/v1"),
                new DiffLine(DiffLineKind.Added, "oid sha256:bbbb"),
                new DiffLine(DiffLineKind.Added, "size 8"),
                new DiffLine(DiffLineKind.Meta, "\\ No newline at end of file"),
            ]),
        ], "");
        Assert.True(LfsPointers.TryReadDiff(document, out var oldPointer, out var newPointer));
        Assert.Equal(4, oldPointer!.Size);
        Assert.Equal(8, newPointer!.Size);
        Assert.Equal(after, newPointer.Render());
    }

    [Fact]
    public async Task Lfs_pointer_diff_does_not_download_and_over_cap_does_not_start_git()
    {
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", "false");
        repo.WriteFile(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        var oid = "sha256:" + new string('a', 64);
        var first = "version https://git-lfs.github.com/spec/v1\noid " + oid + "\nsize 4\n";
        var second = "version https://git-lfs.github.com/spec/v1\noid " + oid + "\nsize 8\n";
        repo.WriteFile("data.bin", first);
        repo.WriteFile("note.txt", "hello filters\n");
        repo.CommitAll("pointer");
        var parent = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.WriteFile("data.bin", second);
        repo.CommitAll("pointer changed");
        var head = repo.RunCapture("rev-parse", "HEAD").Trim();
        var file = Path.Combine(repo.Directory, "data.bin");
        var onDisk = File.ReadAllBytes(file);

        await using var session = await Open(repo);
        var diff = await session.CommitDiffAsync(head, parent, "data.bin", false, CancellationToken.None);
        Assert.NotNull(diff);
        var note = Assert.Single(diff.LfsFiles);
        Assert.Equal(4, note.Before!.Size);
        Assert.Equal(8, note.After!.Size);
        Assert.Contains(session.Snapshot().Commands, command => command.Arguments.Contains("--no-textconv"));
        Assert.Contains(session.Snapshot().Commands, command => command.Arguments.Contains("filter.lfs.process="));
        Assert.Equal(onDisk, File.ReadAllBytes(file));

        var beforeCount = session.Snapshot().Commands.Count;
        var over = await session.LoadRequestedBlobAsync("note.txt", "HEAD", false, HistoryLimits.MaxPreviewBytes + 1, null, CancellationToken.None);
        Assert.True(over.TooLarge);
        Assert.Equal(beforeCount, session.Snapshot().Commands.Count);

        var text = await session.LoadRequestedBlobAsync("note.txt", "HEAD", false, 16, null, CancellationToken.None);
        Assert.False(text.TooLarge);
        Assert.Contains("hello filters", Encoding.UTF8.GetString(text.Bytes), StringComparison.Ordinal);

        try
        {
            await session.LoadRequestedBlobAsync("data.bin", null, false, 4, second, CancellationToken.None);
        }
        catch (GitCommandFailedException)
        {
        }

        Assert.Equal(onDisk, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task Lfs_attribute_marks_the_worktree_and_a_commit()
    {
        using var repo = new TempRepo();
        repo.WriteFile(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        repo.WriteFile("a.txt", "one\n");
        repo.WriteFile("b.bin", "two\n");
        repo.CommitAll("base");
        var sha = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.WriteFile("a.txt", "one!\n");
        repo.WriteFile("b.bin", "two!\n");
        await using var session = await Open(repo);
        var work = session.Snapshot().LfsPaths;
        Assert.Contains("b.bin", work);
        Assert.DoesNotContain("a.txt", work);
        var tracked = await session.LfsTrackedAsync(["a.txt", "b.bin"], sha, CancellationToken.None);
        Assert.Contains("b.bin", tracked);
        Assert.DoesNotContain("a.txt", tracked);
        Assert.Contains(session.Snapshot().Commands, command => command.Arguments.Contains("check-attr"));
    }

    [Fact]
    public async Task Track_with_lfs_stores_a_pointer_and_download_restores_the_file()
    {
        if (!GitLfsInstalled())
            return;
        using var repo = new TempRepo();
        repo.WriteFile("a.bin", "hello-lfs\n");
        await using var session = await Open(repo);
        await session.TrackWithLfsAsync("a.bin", CancellationToken.None);
        var staged = repo.RunCapture(
            "-c", "filter.lfs.smudge=",
            "-c", "filter.lfs.process=",
            "-c", "filter.lfs.required=false",
            "show", ":a.bin");
        Assert.Contains("git-lfs.github.com", staged, StringComparison.Ordinal);
        Assert.Contains("a.bin", session.Snapshot().LfsPaths);
        repo.CommitAll("tracked");
        var pointer = repo.RunCapture(
            "-c", "filter.lfs.smudge=",
            "-c", "filter.lfs.process=",
            "-c", "filter.lfs.required=false",
            "show", "HEAD:a.bin");
        File.WriteAllText(Path.Combine(repo.Directory, "a.bin"), pointer);
        await session.LfsPullFileAsync("a.bin", CancellationToken.None);
        Assert.Equal("hello-lfs\n", File.ReadAllText(Path.Combine(repo.Directory, "a.bin")).Replace("\r\n", "\n", StringComparison.Ordinal));
        await session.UntrackLfsAsync("a.bin", CancellationToken.None);
        var after = await session.LfsTrackedAsync(["a.bin"], null, CancellationToken.None);
        Assert.DoesNotContain("a.bin", after);
    }

    [Fact]
    public async Task Download_of_a_comma_name_restores_that_file_only()
    {
        if (!GitLfsInstalled())
            return;
        using var repo = new TempRepo();
        repo.WriteFile("a,b.bin", "hello-comma\n");
        repo.WriteFile("c.bin", "hello-other\n");
        await using var session = await Open(repo);
        await session.TrackWithLfsAsync("a,b.bin", CancellationToken.None);
        await session.TrackWithLfsAsync("c.bin", CancellationToken.None);
        repo.CommitAll("tracked");
        WritePointer(repo, "a,b.bin");
        WritePointer(repo, "c.bin");
        await session.LfsPullFileAsync("a,b.bin", CancellationToken.None);
        Assert.Equal("hello-comma\n", ReadText(repo, "a,b.bin"));
        Assert.Contains("git-lfs.github.com", ReadText(repo, "c.bin"), StringComparison.Ordinal);
    }

    private static void WritePointer(TempRepo repo, string path)
    {
        var pointer = repo.RunCapture(
            "-c", "filter.lfs.smudge=",
            "-c", "filter.lfs.process=",
            "-c", "filter.lfs.required=false",
            "show", "HEAD:" + path);
        File.WriteAllText(Path.Combine(repo.Directory, path), pointer);
    }

    private static string ReadText(TempRepo repo, string path) =>
        File.ReadAllText(Path.Combine(repo.Directory, path)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static bool GitLfsInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "lfs version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
                return false;
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
