namespace Sextant.Git.Tests;

public class LinuxRepoPathTests
{
    [Fact]
    public void Repo_paths_ignore_case_only_on_windows()
    {
        var left = Path.Combine(Path.GetTempPath(), "SextantRepo");
        Assert.False(RepoPath.Same(left + "A", left + "a"));
    }
}
