namespace Sextant.Git.Tests;

public class ForcePushTests
{
    [Fact]
    public async Task Force_with_lease_updates_a_rewritten_tip_and_names_the_remote_commit()
    {
        using var origin = new TempRepo();
        origin.WriteFile("a.txt", "one\n");
        origin.CommitAll("first");
        var bare = Path.Combine(Path.GetTempPath(), "sextant-bare-" + Guid.NewGuid().ToString("N"));
        var clone = Path.Combine(Path.GetTempPath(), "sextant-clone-" + Guid.NewGuid().ToString("N"));
        try
        {
            origin.Run("clone", "--bare", origin.Directory, bare);
            var runner = new GitProcessRunner();
            await RepositoryAdmin.CloneAsync(runner, origin.Git, bare, clone, null, CancellationToken.None);
            origin.SetIdentity(clone);
            await using var session = await RepositorySession.OpenAsync(runner, origin.Git, clone, CancellationToken.None);
            File.WriteAllText(Path.Combine(clone, "a.txt"), "two\n");
            await session.StageFileAsync("a.txt", CancellationToken.None);
            await session.CommitAsync("second\n", CancellationToken.None);
            await session.PushAsync(null, CancellationToken.None);

            File.WriteAllText(Path.Combine(clone, "a.txt"), "three\n");
            await session.StageFileAsync("a.txt", CancellationToken.None);
            await session.AmendAsync("second rewritten\n", CancellationToken.None);
            var replaced = await session.ListUpstreamOnlyAsync(CancellationToken.None);
            Assert.Contains(replaced, commit => commit.Subject == "second");

            await session.PushForceWithLeaseAsync(null, CancellationToken.None);
            Assert.Equal("second rewritten", GitSubject(origin.Git, bare));
        }
        finally
        {
            TryDelete(bare);
            TryDelete(clone);
        }
    }

    private static string GitSubject(string git, string gitDirectory)
    {
        var info = new System.Diagnostics.ProcessStartInfo(git)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("--git-dir");
        info.ArgumentList.Add(gitDirectory);
        info.ArgumentList.Add("log");
        info.ArgumentList.Add("-1");
        info.ArgumentList.Add("--format=%s");
        using var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return text.Trim();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
