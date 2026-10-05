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
    public async Task Clean_lfs_worktree_is_not_a_pointer_diff()
    {
        if (!GitLfsInstalled())
            return;
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", "false");
        var textPath = Path.Combine(repo.Directory, "picture.bin");
        var binaryPath = Path.Combine(repo.Directory, "blob.bin");
        var textBytes = "hello-lfs\n"u8.ToArray();
        byte[] binaryBytes = [0, 1, 2, 3, 4, 5, 6, 7];
        File.WriteAllBytes(textPath, textBytes);
        File.WriteAllBytes(binaryPath, binaryBytes);
        await using var session = await Open(repo);
        await session.TrackWithLfsAsync("picture.bin", CancellationToken.None);
        await session.TrackWithLfsAsync("blob.bin", CancellationToken.None);
        repo.CommitAll("tracked");
        Assert.Equal(textBytes, File.ReadAllBytes(textPath));
        Assert.Equal(binaryBytes, File.ReadAllBytes(binaryPath));

        // Same bytes, new mtime. Git re-reads the file. The clean filter must still match the index pointer.
        File.WriteAllBytes(textPath, textBytes);
        File.WriteAllBytes(binaryPath, binaryBytes);
        var clean = await session.WorktreeDiffAsync(false, true, false, CancellationToken.None);
        Assert.NotNull(clean);
        Assert.True(string.IsNullOrEmpty(clean.RawPatch));
        Assert.Empty(clean.LfsFiles);
        Assert.Contains(session.Snapshot().Commands, command =>
            command.Arguments.Contains("diff")
            && command.Arguments.Contains("--no-textconv")
            && !command.Arguments.Contains("--cached")
            && !command.Arguments.Contains("filter.lfs.process="));

        File.WriteAllBytes(textPath, "hello-lfs-changed\n"u8.ToArray());
        File.WriteAllBytes(binaryPath, [0, 1, 2, 3, 9, 9, 9, 9]);
        var dirty = await session.WorktreeDiffAsync(false, true, false, CancellationToken.None);
        Assert.NotNull(dirty);
        Assert.Contains("git-lfs.github.com", dirty.RawPatch, StringComparison.Ordinal);
        Assert.DoesNotContain("hello-lfs-changed", dirty.RawPatch, StringComparison.Ordinal);
        Assert.DoesNotContain("Binary files ", dirty.RawPatch, StringComparison.Ordinal);
        Assert.Equal(2, dirty.LfsFiles.Count);
        Assert.All(dirty.LfsFiles, note => Assert.Null(note.LocalBytes));
        var picture = Assert.Single(dirty.LfsFiles, note => note.Path == "picture.bin");
        Assert.NotNull(picture.Before);
        Assert.NotNull(picture.After);
        Assert.NotEqual(picture.Before.Oid, picture.After.Oid);
        Assert.Equal("hello-lfs-changed\n", File.ReadAllText(textPath));
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

    [Fact]
    public async Task Remote_checkout_keeps_a_cached_lfs_file_and_leaves_a_missing_one()
    {
        if (!GitLfsInstalled())
            return;
        using var origin = new TempRepo();
        origin.Run("config", "core.autocrlf", "false");
        origin.Run("lfs", "track", "*.bin");
        var cached = "cached-but-new\n"u8.ToArray();
        var missing = "needs-download\n"u8.ToArray();
        File.WriteAllBytes(Path.Combine(origin.Directory, "have.bin"), "old-cached\n"u8.ToArray());
        origin.CommitAll("main");
        var main = origin.CurrentBranch();
        origin.Run("switch", "-c", "feature/ColliderCreator");
        File.WriteAllBytes(Path.Combine(origin.Directory, "have.bin"), cached);
        File.WriteAllBytes(Path.Combine(origin.Directory, "need.bin"), missing);
        origin.CommitAll("feature");
        var cachedOid = PointerOid(origin, "feature/ColliderCreator:have.bin");
        var missingOid = PointerOid(origin, "feature/ColliderCreator:need.bin");
        origin.Run("switch", main);

        var clone = Path.Combine(Path.GetTempPath(), "sextant-test-" + Guid.NewGuid().ToString("N"));
        var credentialMarker = Path.Combine(Path.GetTempPath(), "sextant-cred-" + Guid.NewGuid().ToString("N"));
        var credentialHelper = credentialMarker + ".sh";
        try
        {
            origin.Run("clone", origin.Directory, clone);
            WriteLfsObject(clone, cachedOid, cached);
            RemoveLfsObject(clone, missingOid);
            if (OperatingSystem.IsWindows())
                Git(origin.Git, clone, "config", "credential.helper", "");
            else
                Git(origin.Git, clone, "config", "credential.helper", HangCredentialHelper(credentialMarker, credentialHelper));
            Git(origin.Git, clone, "config", "lfs.url", "https://127.0.0.1:9/no-such-lfs");
            await using var session = await RepositorySession.OpenAsync(new GitProcessRunner(), origin.Git, clone, CancellationToken.None);
            var started = Stopwatch.StartNew();
            await session.SwitchTrackAsync("origin/feature/ColliderCreator", CancellationToken.None);
            Assert.True(started.Elapsed < TimeSpan.FromSeconds(4), "checkout waited on a credential helper");
            Assert.False(File.Exists(credentialMarker));
            var state = session.Snapshot();
            Assert.Equal("feature/ColliderCreator", state.Branch.HeadName);
            Assert.Equal("origin/feature/ColliderCreator", state.Branch.Upstream);
            Assert.Equal(cached, File.ReadAllBytes(Path.Combine(clone, "have.bin")));
            var pointer = File.ReadAllText(Path.Combine(clone, "need.bin"));
            Assert.Contains("git-lfs.github.com/spec/v1", pointer, StringComparison.Ordinal);
            Assert.Contains(missingOid, pointer, StringComparison.Ordinal);
            var made = state.Commands.Last(command => command.Arguments.Contains("switch"));
            Assert.Equal(0, made.ExitCode);
            Assert.Contains("-c", made.Arguments);
            Assert.Contains("--track", made.Arguments);
            Assert.Contains(state.Commands, command => command.Arguments.Contains("lfs") && command.Arguments.Contains("checkout") && command.ExitCode == 0);
        }
        finally
        {
            TryDelete(credentialMarker);
            TryDelete(credentialHelper);
            for (var attempt = 0; attempt < 5 && Directory.Exists(clone); attempt++)
            {
                try
                {
                    Directory.Delete(clone, recursive: true);
                }
                catch (IOException)
                {
                    Thread.Sleep(40);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(40);
                }
            }
        }
    }

    private static string PointerOid(TempRepo repo, string revision)
    {
        var pointer = repo.RunCapture(
            "-c", "filter.lfs.smudge=",
            "-c", "filter.lfs.process=",
            "-c", "filter.lfs.required=false",
            "show", revision);
        var line = pointer.Split('\n').First(part => part.StartsWith("oid sha256:", StringComparison.Ordinal));
        return line["oid sha256:".Length..].Trim();
    }

    private static void WriteLfsObject(string repo, string oid, byte[] bytes)
    {
        var path = LfsObjectPath(repo, oid);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
            File.Delete(path);
        File.WriteAllBytes(path, bytes);
    }

    private static void RemoveLfsObject(string repo, string oid)
    {
        var path = LfsObjectPath(repo, oid);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string LfsObjectPath(string repo, string oid) =>
        Path.Combine(repo, ".git", "lfs", "objects", oid[..2], oid[2..4], oid);

    private static string HangCredentialHelper(string marker, string helper)
    {
        File.WriteAllText(helper, "#!/bin/sh\nprintf called > \"" + marker + "\"\nsleep 8\n");
        using var chmod = Process.Start(new ProcessStartInfo("chmod")
        {
            ArgumentList = { "755", helper },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        chmod?.WaitForExit();
        return helper;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void Git(string git, string directory, params string[] args)
    {
        var info = new ProcessStartInfo(git)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(directory);
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        var stderr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} exited {process.ExitCode}{Environment.NewLine}{stderr}");
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
