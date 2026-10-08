using Sextant.Git.Models;
using Sextant.Git.Parsing;
using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class SubmoduleTests
{
    [Fact]
    public void Submodule_status_reads_state_sha_path_and_describe()
    {
        var text = """
             0123456789abcdef vendor/lib (heads/main)
            -0123456789abcdef vendor/empty
            +0123456789abcdef vendor/dirty (v1)
            U0123456789abcdef vendor/bad

            """;
        var entries = SubmoduleParser.Parse(text);
        Assert.Equal(4, entries.Count);
        Assert.Equal(SubmoduleState.Matches, entries[0].State);
        Assert.Equal("vendor/lib", entries[0].Path);
        Assert.Equal("0123456789abcdef", entries[0].Sha);
        Assert.Equal("heads/main", entries[0].Describe);
        Assert.Equal(SubmoduleState.Uninitialized, entries[1].State);
        Assert.Equal("vendor/empty", entries[1].Path);
        Assert.Null(entries[1].Describe);
        Assert.Equal(SubmoduleState.Modified, entries[2].State);
        Assert.Equal("v1", entries[2].Describe);
        Assert.Equal(SubmoduleState.Conflict, entries[3].State);
    }

    [Fact]
    public async Task Submodule_is_listed_and_not_updated()
    {
        using var child = new TempRepo();
        child.WriteFile("lib.txt", "lib");
        child.CommitAll("lib");
        using var parent = new TempRepo();
        parent.WriteFile("readme.txt", "parent");
        parent.CommitAll("parent");
        parent.Run("config", "protocol.file.allow", "always");
        parent.Run("-c", "protocol.file.allow=always", "submodule", "add", child.Directory, "vendor/lib");
        parent.CommitAll("add submodule");

        await using var session = await Open(parent);
        var entry = Assert.Single(session.Snapshot().Submodules);
        Assert.Equal("vendor/lib", entry.Path.Replace('\\', '/'));
        Assert.NotEqual(SubmoduleState.Uninitialized, entry.State);
        Assert.DoesNotContain(session.Snapshot().Commands, command =>
            command.Arguments.Contains("submodule") && command.Arguments.Contains("update"));
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
