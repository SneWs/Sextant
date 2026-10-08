using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class RepoPathTests
{
    [Fact]
    public void Repo_paths_match_through_a_directory_symlink()
    {
        var root = Path.Combine(Path.GetTempPath(), "sextant-links-" + Guid.NewGuid().ToString("N"));
        var real = Path.Combine(root, "real");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(real);
        try
        {
            Directory.CreateSymbolicLink(link, real);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Directory.Delete(root, recursive: true);
            return;
        }

        try
        {
            var viaLink = Path.Combine(link, "repo");
            var viaReal = Path.Combine(real, "repo");
            Directory.CreateDirectory(viaReal);
            Assert.True(RepoPath.Same(viaLink, viaReal));
            Assert.True(RepoPath.Same(Path.Combine(link, "missing"), Path.Combine(real, "missing")));
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(outside);
            var escape = Path.Combine(real, "escape");
            Directory.CreateSymbolicLink(escape, outside);
            Assert.Null(RepoPath.CombineUnder(real, "escape/a.txt"));
        }
        finally
        {
            var escape = Path.Combine(real, "escape");
            if (Directory.Exists(escape))
                Directory.Delete(escape, recursive: false);
            if (Directory.Exists(link))
                Directory.Delete(link, recursive: false);
            Directory.Delete(root, recursive: true);
        }
    }
}
