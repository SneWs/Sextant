using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class PullRebaseTests
{
    [Fact]
    public async Task Pull_rebase_autostashes_dirty_worktree_and_rebases_local_commit()
    {
        using var origin = new TempRepo();
        origin.WriteFile("a.txt", "one\n");
        origin.WriteFile("notes.txt", "note\n");
        origin.CommitAll("base");
        var clone = Path.Combine(Path.GetTempPath(), "sextant-clone-" + Guid.NewGuid().ToString("N"));
        try
        {
            origin.Run("clone", origin.Directory, clone);
            origin.SetIdentity(clone);
            File.WriteAllText(Path.Combine(clone, "local.txt"), "local\n");
            GitIn(origin, clone, "add", "local.txt");
            GitIn(origin, clone, "commit", "-m", "local");
            origin.WriteFile("a.txt", "two\n");
            origin.CommitAll("second");
            File.WriteAllText(Path.Combine(clone, "notes.txt"), "note edited\n");

            await using var session = await RepositorySession.OpenAsync(
                new GitProcessRunner(), origin.Git, clone, CancellationToken.None);
            await session.PullRebaseAsync(null, CancellationToken.None);

            Assert.Contains(session.Snapshot().Commands, command =>
                command.Arguments.Contains("pull")
                && command.Arguments.Contains("--rebase")
                && command.Arguments.Contains("--autostash"));
            Assert.Equal("note edited\n", File.ReadAllText(Path.Combine(clone, "notes.txt")).Replace("\r\n", "\n"));
            var log = GitInCapture(origin, clone, "log", "--format=%s", "-2");
            Assert.Equal(new[] { "local", "second" }, log.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            DeleteDirectory(clone);
        }
    }

    // Git marks object files read-only, which makes recursive delete fail on Windows.
    private static void DeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;
                foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                    entry.Attributes = FileAttributes.Normal;
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(40);
            }
        }
    }

    private static void GitIn(TempRepo origin, string directory, params string[] args)
    {
        var command = new List<string> { "-C", directory };
        command.AddRange(args);
        origin.Run(command.ToArray());
    }

    private static string GitInCapture(TempRepo origin, string directory, params string[] args)
    {
        var command = new List<string> { "-C", directory };
        command.AddRange(args);
        return origin.RunCapture(command.ToArray());
    }
}
