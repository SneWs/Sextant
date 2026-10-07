using System.Diagnostics;
using System.Text;
using Sextant.Git.Parsing;

namespace Sextant.Git.Tests;

public class LfsLockTests
{
    [Fact]
    public void Commands_keep_paths_literal_and_force_is_opt_in()
    {
        const string path = "--force 雪 file.bin";
        Assert.Equal(
            ["-C", "repo", "--no-optional-locks", "ls-files", "--cached", "--others", "--exclude-standard", "--deduplicate", "-z"],
            GitCommands.RepositoryFiles("repo"));
        Assert.Equal(
            ["-C", "repo", "--no-optional-locks", "lfs", "locks"],
            GitCommands.LfsLocks("repo"));
        Assert.Equal(
            ["-C", "repo", "--no-optional-locks", "lfs", "locks", "--cached", "--json"],
            GitCommands.LfsLocksCached("repo"));
        Assert.Equal(
            ["-C", "repo", "lfs", "lock", "--json", "--", path],
            GitCommands.LfsLock("repo", path));
        Assert.Equal(
            ["-C", "repo", "lfs", "unlock", "--json", "--", path],
            GitCommands.LfsUnlock("repo", path, force: false));
        Assert.Equal(
            ["-C", "repo", "lfs", "unlock", "--json", "--force", "--", path],
            GitCommands.LfsUnlock("repo", path, force: true));
    }

    [Fact]
    public void Parser_reads_cli_array_preserving_unicode_whitespace_and_owner()
    {
        var bytes = Encoding.UTF8.GetBytes("""
            [
              {"id":"lock-1","path":"素材/雪 and  spaces.bin","owner":{"name":"Zoë 雪"},"locked_at":"2026-10-07T12:00:00Z"},
              {"id":"lock-2","path":" leading\tline\nfile.bin","owner":{"name":"Other Person"}},
              {"id":"lock-3","path":"--force.bin"},
              {"id":"lock-4","path":"no-owner.bin","owner":null},
              {"id":"lock-5","path":"unnamed-owner.bin","owner":{}}
            ]
            """);
        Assert.Equal(
            [
                new LfsLock("lock-1", "素材/雪 and  spaces.bin", "Zoë 雪"),
                new LfsLock("lock-2", " leading\tline\nfile.bin", "Other Person"),
                new LfsLock("lock-3", "--force.bin", ""),
                new LfsLock("lock-4", "no-owner.bin", ""),
                new LfsLock("lock-5", "unnamed-owner.bin", ""),
            ],
            LfsLockParser.Parse(bytes));
        Assert.Empty(LfsLockParser.Parse("[] "u8.ToArray()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"locks\":[]}")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[1]")]
    [InlineData("[{}]")]
    [InlineData("[{\"id\":\"1\"}]")]
    [InlineData("[{\"path\":\"file.bin\"}]")]
    [InlineData("[{\"id\":\"\",\"path\":\"file.bin\"}]")]
    [InlineData("[{\"id\":\"1\",\"path\":\"\"}]")]
    [InlineData("[{\"id\":1,\"path\":\"file.bin\"}]")]
    [InlineData("[{\"id\":\"1\",\"path\":null}]")]
    [InlineData("[{\"id\":\"1\",\"path\":false}]")]
    [InlineData("[{\"id\":\"1\",\"path\":\"good.bin\"},{}]")]
    public void Parser_rejects_malformed_or_incomplete_lock_information(string json)
    {
        var error = Assert.Throws<RepositoryActionException>(() =>
            LfsLockParser.Parse(Encoding.UTF8.GetBytes(json)));
        Assert.Contains("invalid lock information", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repository_files_include_tracked_and_untracked_but_not_ignored_paths()
    {
        using var repo = CreateRepo();
        repo.WriteFile(".gitattributes", "*.bin filter=lfs -text\n");
        string[] tracked = ["tracked.txt", "tracked.tmp", "missing.txt", "assets/with spaces.bin", "-literal.bin", " leading 雪.bin"];
        foreach (var path in tracked)
            repo.WriteFile(path, "tracked\n");
        repo.CommitAll("tracked files");
        repo.WriteFile(".gitignore", "*.tmp\nignored/\n");
        repo.CommitAll("ignore new files");
        File.Delete(Path.Combine(repo.Directory, "missing.txt"));
        repo.WriteFile("untracked/new  file.bin", "untracked\n");
        repo.WriteFile("untracked/plain.txt", "untracked\n");
        repo.WriteFile("ignored.tmp", "ignored\n");
        repo.WriteFile("ignored/deep/file.bin", "ignored\n");
        repo.WriteFile(".git/info/exclude", "locally-excluded.bin\n");
        repo.WriteFile("locally-excluded.bin", "ignored\n");
        var whitespace = new List<string>();
        if (!OperatingSystem.IsWindows())
        {
            whitespace.Add("tab\tname.bin");
            whitespace.Add("line\nname.bin");
            foreach (var path in whitespace)
                repo.WriteFile(path, "untracked\n");
        }

        await using var session = await Open(repo, CancellationToken.None);
        var files = await session.RepositoryFilesAsync(CancellationToken.None);
        var expected = tracked.Concat([".gitattributes", ".gitignore", "untracked/new  file.bin", "untracked/plain.txt"])
            .Concat(whitespace).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, files.Select(file => file.Path).Order(StringComparer.Ordinal));
        Assert.All(files, file => Assert.Equal(file.Path.EndsWith(".bin", StringComparison.Ordinal), file.LfsTracked));
        Assert.False(File.Exists(Path.Combine(repo.Directory, "missing.txt")));
        Assert.Equal(GitCommands.RepositoryFiles(session.Toplevel),
            session.Snapshot().Commands.Last(command => command.Arguments.Contains("ls-files")).Arguments.Skip(1));
    }

    [Fact]
    public async Task Repository_files_deduplicate_unmerged_index_stages()
    {
        using var repo = CreateRepo();
        repo.WriteFile(".gitattributes", "*.bin filter=lfs -text\n");
        repo.WriteFile("conflict.bin", "base\n");
        repo.CommitAll("base");
        var original = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("conflict.bin", "other\n");
        repo.CommitAll("other");
        repo.Run("switch", original);
        repo.WriteFile("conflict.bin", "ours\n");
        repo.CommitAll("ours");
        Assert.Throws<InvalidOperationException>(() => repo.Run("merge", "--no-edit", "other"));
        Assert.Equal(3, repo.RunCapture("ls-files", "--unmerged").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);

        await using var session = await Open(repo, CancellationToken.None);
        var files = await session.RepositoryFilesAsync(CancellationToken.None);
        Assert.Equal([".gitattributes", "conflict.bin"], files.Select(file => file.Path).Order(StringComparer.Ordinal));
        Assert.True(Assert.Single(files, file => file.Path == "conflict.bin").LfsTracked);
    }

    [Fact]
    public async Task Repository_files_include_sparse_excluded_paths_without_materializing_them()
    {
        using var repo = CreateRepo();
        repo.WriteFile(".gitattributes", "*.bin filter=lfs -text\n");
        repo.WriteFile("keep/file.txt", "keep\n");
        repo.WriteFile("skip/雪 file.bin", "excluded\n");
        repo.CommitAll("files");
        repo.Run("sparse-checkout", "set", "--cone", "--sparse-index", "keep");
        var excluded = Path.Combine(repo.Directory, "skip", "雪 file.bin");
        Assert.False(File.Exists(excluded));
        repo.WriteFile("keep/new.bin", "untracked\n");

        await using var session = await Open(repo, CancellationToken.None);
        var files = await session.RepositoryFilesAsync(CancellationToken.None);
        Assert.Equal(
            [".gitattributes", "keep/file.txt", "keep/new.bin", "skip/雪 file.bin"],
            files.Select(file => file.Path).Order(StringComparer.Ordinal));
        Assert.True(Assert.Single(files, file => file.Path == "skip/雪 file.bin").LfsTracked);
        Assert.True(Assert.Single(files, file => file.Path == "keep/new.bin").LfsTracked);
        Assert.False(File.Exists(excluded));
    }

    [Fact]
    public async Task Actual_lfs_lists_all_pages_and_roundtrips_literal_locks_and_normal_unlocks()
    {
        if (!GitLfsInstalled())
            return;
        LfsLock[] existing =
        [
            new("other-1", "other person's.bin", "Other Person"),
            new("own-1", "existing.bin", "Test"),
            new("other-2", "素材/remote 雪.bin", "Zoë"),
        ];
        await using var server = new LocalLfsLockServer(existing, pageSize: 1);
        using var repo = CreateLfsRepo(server, existing.Select(item => item.Path).Concat(["素材/a  雪.bin", "--force.bin"]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        await using var session = await Open(repo, token);

        var initial = await session.LfsLocksAsync(token);
        Assert.Equal(existing, initial);
        Assert.Contains(server.Requests, request => request.Cursor == "1");
        Assert.Contains(server.Requests, request => request.Cursor == "2");

        foreach (var path in new[] { "素材/a  雪.bin", "--force.bin" })
        {
            await session.LockLfsFileAsync(path, token);
            var locked = await session.LfsLocksAsync(token);
            var acquired = Assert.Single(locked, item => item.Path == path);
            Assert.Equal("Test", acquired.Owner);
            Assert.False(string.IsNullOrEmpty(acquired.Id));
            Assert.Equal(existing.Length + 1, locked.Count);
            Assert.Contains(server.Requests, request => request.Method == "POST" && request.Route == "locks" && request.Path == path);

            await session.UnlockLfsFileAsync(path, force: false, token);
            Assert.Equal(existing, await session.LfsLocksAsync(token));
            Assert.Contains(server.Requests, request =>
                request.Method == "POST" && request.Route == $"locks/{acquired.Id}/unlock" && !request.Force);
        }

        Assert.DoesNotContain(session.Snapshot().Commands, command =>
            command.Arguments.Contains("lfs") && command.ExitCode != 0);
    }

    [Fact]
    public async Task Actual_lfs_refuses_somebody_elses_lock_until_force_is_requested()
    {
        if (!GitLfsInstalled())
            return;
        var other = new LfsLock("other-1", "other person's 雪.bin", "Other Person");
        await using var server = new LocalLfsLockServer([other]);
        using var repo = CreateLfsRepo(server, [other.Path]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        await using var session = await Open(repo, token);
        Assert.Equal([other], await session.LfsLocksAsync(token));

        var error = await Assert.ThrowsAsync<GitCommandFailedException>(() =>
            session.UnlockLfsFileAsync(other.Path, force: false, token));
        Assert.NotEqual(0, error.ExitCode);
        Assert.DoesNotContain("--force", error.Arguments);
        Assert.Equal([other], await session.LfsLocksAsync(token));
        Assert.Contains(server.Requests, request =>
            request.Method == "POST" && request.Route == $"locks/{other.Id}/unlock" && !request.Force);

        await session.UnlockLfsFileAsync(other.Path, force: true, token);
        Assert.Empty(await session.LfsLocksAsync(token));
        Assert.Contains(server.Requests, request =>
            request.Method == "POST" && request.Route == $"locks/{other.Id}/unlock" && request.Force);
    }

    [Fact]
    public async Task Actual_lfs_propagates_server_lock_rejection_without_changing_locks()
    {
        if (!GitLfsInstalled())
            return;
        var existing = new LfsLock("other-1", "already locked.bin", "Other Person");
        await using var server = new LocalLfsLockServer([existing]);
        using var repo = CreateLfsRepo(server, [existing.Path]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var session = await Open(repo, timeout.Token);

        var error = await Assert.ThrowsAsync<GitCommandFailedException>(() =>
            session.LockLfsFileAsync(existing.Path, timeout.Token));
        Assert.NotEqual(0, error.ExitCode);
        Assert.Contains("Lock already exists", error.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(server.Requests, request =>
            request.Method == "POST" && request.Route == "locks" && request.Path == existing.Path);
        Assert.Equal([existing], await session.LfsLocksAsync(timeout.Token));
    }

    [Fact]
    public async Task Actual_lfs_does_not_treat_a_server_error_as_an_empty_lock_list()
    {
        if (!GitLfsInstalled())
            return;
        var existing = new LfsLock("other-1", "locked.bin", "Other Person");
        await using var server = new LocalLfsLockServer([existing]);
        using var repo = CreateLfsRepo(server, [existing.Path]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var session = await Open(repo, timeout.Token);
        Assert.Equal([existing], await session.LfsLocksAsync(timeout.Token));

        server.ListStatus = 500;
        var error = await Assert.ThrowsAsync<GitCommandFailedException>(() => session.LfsLocksAsync(timeout.Token));
        Assert.NotEqual(0, error.ExitCode);
        Assert.DoesNotContain("--json", error.Arguments);
        Assert.NotEqual(0, session.Snapshot().Commands.Last().ExitCode);
    }

    private static TempRepo CreateRepo()
    {
        var repo = new TempRepo();
        try
        {
            repo.Run("config", "core.autocrlf", "false");
            repo.Run("config", "core.excludesFile", Path.Combine(repo.Directory, ".git", "info", "exclude"));
            // These tests exercise attributes and locks, not LFS object upload or download.
            repo.Run("config", "filter.lfs.process", "");
            repo.Run("config", "filter.lfs.clean", "");
            repo.Run("config", "filter.lfs.smudge", "");
            repo.Run("config", "filter.lfs.required", "false");
            return repo;
        }
        catch
        {
            repo.Dispose();
            throw;
        }
    }

    internal static TempRepo CreateLfsRepo(LocalLfsLockServer server, IEnumerable<string> paths)
    {
        var repo = CreateRepo();
        try
        {
            repo.Run("config", "lfs.url", server.Url);
            repo.Run("config", "lfs.locksverify", "false");
            repo.Run("config", "lfs.transfer.maxretries", "1");
            repo.Run("config", "credential.helper", "");
            repo.Run("remote", "add", "origin", server.Url);
            repo.WriteFile(".gitattributes", "*.bin filter=lfs -text\n");
            foreach (var path in paths)
                repo.WriteFile(path, "lockable file\n");
            repo.CommitAll("lockable files");
            return repo;
        }
        catch
        {
            repo.Dispose();
            throw;
        }
    }

    internal static bool GitLfsInstalled()
    {
        try
        {
            var info = new ProcessStartInfo(GitLocator.FindOnPath() ?? "git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("lfs");
            info.ArgumentList.Add("version");
            using var process = Process.Start(info);
            if (process is null)
                return false;
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static Task<RepositorySession> Open(TempRepo repo, CancellationToken token) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, token);
}
