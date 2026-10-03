namespace Sextant.Git.Tests;

public class GitHubAccountTests
{
    [Fact]
    public void Status_lists_each_signed_in_account()
    {
        var text = """
            github.com
              ✓ Logged in to github.com account SneWs (keyring)
              - Active account: true
              - Git operations protocol: https
              - Token: gho_************************************

              ✓ Logged in to github.com account marcus-grenangen_spn (keyring)
              - Active account: false
              - Token: gho_************************************

            ghe.example.com
              ✓ Logged in to ghe.example.com account work (keyring)
              - Active account: true

            """;
        var accounts = GitHubAccounts.ParseStatus(text);
        Assert.Equal(3, accounts.Count);
        Assert.Equal(new GitHubAccount("github.com", "SneWs", true), accounts[0]);
        Assert.Equal(new GitHubAccount("github.com", "marcus-grenangen_spn", false), accounts[1]);
        Assert.Equal(new GitHubAccount("ghe.example.com", "work", true), accounts[2]);
    }

    [Fact]
    public void A_missing_repository_is_an_access_failure_and_a_missing_object_is_not()
    {
        Assert.True(GitHubAccounts.IsAccessFailure("batch response: Not Found\nfatal: smudge filter lfs failed"));
        Assert.True(GitHubAccounts.IsAccessFailure("remote: Repository not found."));
        Assert.False(GitHubAccounts.IsAccessFailure("[404] Object does not exist on the server"));
        Assert.False(GitHubAccounts.IsAccessFailure(""));
        Assert.Contains("cannot see this repository", GitHubAccounts.AccessHint("batch response: Not Found"), StringComparison.Ordinal);
        Assert.Contains("not on the server", GitHubAccounts.AccessHint("Object does not exist on the server"), StringComparison.Ordinal);
        Assert.False(GitHubAccounts.IsAccessFailure("git-lfs filter-process: git-lfs: command not found"));
        Assert.Contains("git-lfs was not found", GitHubAccounts.AccessHint("git-lfs: command not found"), StringComparison.Ordinal);
    }
}

public class ToolPathTests
{
    [Fact]
    public void Git_directory_is_first_and_is_not_repeated()
    {
        var separator = Path.PathSeparator;
        var gitDirectory = Rooted("opt", "custom", "bin");
        var usrBin = Rooted("usr", "bin");
        var bin = Rooted("bin");
        var executable = Path.Combine(gitDirectory, "git");

        var path = GitProcessRunner.ToolPath(string.Join(separator, usrBin, bin), executable);
        Assert.StartsWith(gitDirectory + separator, path);

        var alternate = gitDirectory.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var again = GitProcessRunner.ToolPath(string.Join(separator, alternate, usrBin), executable);
        var entries = again.Split(separator);
        Assert.Equal(1, entries.Count(entry => entry == gitDirectory));
        Assert.Contains(usrBin, entries);
    }

    private static string Rooted(params string[] parts) =>
        Path.DirectorySeparatorChar + Path.Combine(parts);
}
