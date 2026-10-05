using System.Diagnostics;
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

    [Fact]
    public async Task Remote_branch_checkout_creates_a_local_branch_or_uses_the_one_that_exists()
    {
        using var origin = new TempRepo();
        origin.WriteFile("a.txt", "one\n");
        origin.CommitAll("first");
        var main = origin.CurrentBranch();
        var mainSha = origin.RunCapture("rev-parse", "HEAD").Trim();
        origin.Run("switch", "-c", "feature/ColliderCreator");
        origin.WriteFile("a.txt", "remote\n");
        origin.CommitAll("feature");
        var featureSha = origin.RunCapture("rev-parse", "HEAD").Trim();
        origin.Run("switch", main);

        var clone = Path.Combine(Path.GetTempPath(), "sextant-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            origin.Run("clone", origin.Directory, clone);
            Git(origin.Git, clone, "branch", "feature/ColliderCreator", "origin/" + main);
            await using (var session = await OpenAt(origin.Git, clone))
            {
                await session.SwitchTrackAsync("origin/feature/ColliderCreator", CancellationToken.None);
                var taken = session.Snapshot();
                Assert.Equal("feature/ColliderCreator", taken.Branch.HeadName);
                Assert.Equal(mainSha, taken.Branch.Oid, StringComparer.OrdinalIgnoreCase);
                Assert.Equal("origin/" + main, taken.Branch.Upstream);
                Assert.Equal("one\n", File.ReadAllText(Path.Combine(clone, "a.txt")).Replace("\r\n", "\n", StringComparison.Ordinal));
                var usedLocal = taken.Commands.Last(command => command.Arguments.Contains("switch"));
                Assert.Contains("feature/ColliderCreator", usedLocal.Arguments);
                Assert.DoesNotContain("-c", usedLocal.Arguments);
                Assert.DoesNotContain("--track", usedLocal.Arguments);

                await session.SwitchAsync(main, CancellationToken.None);
            }

            Git(origin.Git, clone, "branch", "-D", "feature/ColliderCreator");
            await using var created = await OpenAt(origin.Git, clone);
            await created.SwitchTrackAsync("origin/feature/ColliderCreator", CancellationToken.None);
            var state = created.Snapshot();
            Assert.Equal("feature/ColliderCreator", state.Branch.HeadName);
            Assert.Equal(featureSha, state.Branch.Oid, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("origin/feature/ColliderCreator", state.Branch.Upstream);
            Assert.Equal("remote\n", File.ReadAllText(Path.Combine(clone, "a.txt")).Replace("\r\n", "\n", StringComparison.Ordinal));
            var made = state.Commands.Last(command => command.Arguments.Contains("switch"));
            Assert.Contains("-c", made.Arguments);
            Assert.Contains("--track", made.Arguments);
            Assert.Contains("origin/feature/ColliderCreator", made.Arguments);
            Assert.Equal(0, made.ExitCode);
        }
        finally
        {
            DeleteDirectory(clone);
        }
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        OpenAt(repo.Git, repo.Directory);

    private static Task<RepositorySession> OpenAt(string git, string directory) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), git, directory, CancellationToken.None);

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

    private static void DeleteDirectory(string directory)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
                return;
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
