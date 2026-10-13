using Sextant.Git.Parsing;
using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class LfsRepoTests
{
    [Fact]
    public void Config_enables_lfs_only_for_a_local_address_or_a_filter_command()
    {
        Assert.False(LfsRepo.ConfigEnables(new Dictionary<string, string>()));
        Assert.False(LfsRepo.ConfigEnables(Filters("", "", "")));
        Assert.False(LfsRepo.ConfigEnables(Filters("  ", "\t", "")));
        Assert.False(LfsRepo.ConfigEnables(new Dictionary<string, string> { ["filter.lfs.required"] = "true" }));
        Assert.False(LfsRepo.ConfigEnables(new Dictionary<string, string> { ["lfs.url"] = "  " }));
        Assert.True(LfsRepo.ConfigEnables(new Dictionary<string, string> { ["lfs.url"] = "https://example.test/repo.git/info/lfs" }));
        Assert.True(LfsRepo.ConfigEnables(Filters("git-lfs clean -- %f", "", "")));
        Assert.True(LfsRepo.ConfigEnables(Filters("", "git-lfs smudge -- %f", "")));
        Assert.True(LfsRepo.ConfigEnables(Filters("", "", "git-lfs filter-process")));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("# *.bin filter=lfs diff=lfs\n", false)]
    [InlineData("  # *.bin filter=lfs\n", false)]
    [InlineData("*.bin filter=lfs-extra\n", false)]
    [InlineData("*.bin notfilter=lfs\n", false)]
    [InlineData("*.bin filter=lfs\n", true)]
    [InlineData("*.bin filter=lfs diff=lfs merge=lfs -text\n", true)]
    [InlineData("*.bin diff=lfs filter=lfs -text\n", true)]
    [InlineData("dir/* filter=lfs\r\n", true)]
    public void Attributes_enable_lfs_only_for_a_filter_lfs_token(string text, bool enabled) =>
        Assert.Equal(enabled, LfsRepo.AttributesEnable(text));

    [Fact]
    public async Task A_fresh_repository_with_empty_lfs_filters_does_not_use_lfs()
    {
        using var repo = new TempRepo();
        DisableLocalFilters(repo);
        repo.WriteFile("readme.txt", "hello\n");
        await using var session = await Open(repo);
        var files = await session.RepositoryFilesAsync(CancellationToken.None);
        Assert.False(await session.UsesLfsAsync(files.Select(file => file.Path).ToArray(), CancellationToken.None));
        Assert.DoesNotContain(session.Snapshot().Commands, command => command.Arguments.Contains("lfs"));
    }

    [Fact]
    public async Task A_local_lfs_url_uses_lfs_without_calling_git_lfs()
    {
        using var repo = new TempRepo();
        repo.Run("config", "lfs.url", "https://127.0.0.1:9/no-such-lfs");
        repo.WriteFile("readme.txt", "hello\n");
        await using var session = await Open(repo);
        var files = await session.RepositoryFilesAsync(CancellationToken.None);
        Assert.True(await session.UsesLfsAsync(files.Select(file => file.Path).ToArray(), CancellationToken.None));
        Assert.DoesNotContain(session.Snapshot().Commands, command => command.Arguments.Contains("lfs"));
    }

    [Fact]
    public async Task An_untracked_attributes_file_uses_lfs_when_no_file_matches_the_pattern()
    {
        using var repo = new TempRepo();
        DisableLocalFilters(repo);
        repo.WriteFile("readme.txt", "hello\n");
        repo.WriteFile("dir/.gitattributes", "*.bin filter=lfs diff=lfs -text\n");
        await using var session = await Open(repo);
        var files = await session.RepositoryFilesAsync(CancellationToken.None);
        Assert.DoesNotContain(files, file => file.LfsTracked);
        Assert.True(await session.UsesLfsAsync(files.Select(file => file.Path).ToArray(), CancellationToken.None));
        Assert.DoesNotContain(session.Snapshot().Commands, command => command.Arguments.Contains("lfs"));
    }

    [Fact]
    public async Task An_indexed_attributes_file_uses_lfs_after_the_worktree_copy_is_gone()
    {
        using var repo = new TempRepo();
        DisableLocalFilters(repo);
        repo.WriteFile(".gitattributes", "*.bin filter=lfs -text\n");
        repo.WriteFile("readme.txt", "hello\n");
        repo.CommitAll("attributes");
        File.Delete(Path.Combine(repo.Directory, ".gitattributes"));
        await using var session = await Open(repo);
        var files = await session.RepositoryFilesAsync(CancellationToken.None);
        Assert.Contains(files, file => file.Path == ".gitattributes");
        Assert.True(await session.UsesLfsAsync(files.Select(file => file.Path).ToArray(), CancellationToken.None));
        Assert.Contains(session.Snapshot().Commands, command => command.Arguments.Contains("cat-file"));
        Assert.DoesNotContain(session.Snapshot().Commands, command => command.Arguments.Contains("lfs"));
    }

    [Fact]
    public async Task Info_attributes_uses_lfs()
    {
        using var repo = new TempRepo();
        DisableLocalFilters(repo);
        repo.WriteFile("readme.txt", "hello\n");
        repo.WriteFile(".git/info/attributes", "*.bin filter=lfs -text\n");
        await using var session = await Open(repo);
        var files = await session.RepositoryFilesAsync(CancellationToken.None);
        Assert.True(await session.UsesLfsAsync(files.Select(file => file.Path).ToArray(), CancellationToken.None));
        Assert.DoesNotContain(session.Snapshot().Commands, command => command.Arguments.Contains("lfs"));
    }

    [Fact]
    public async Task A_comment_and_a_lookalike_token_do_not_use_lfs()
    {
        using var repo = new TempRepo();
        DisableLocalFilters(repo);
        repo.WriteFile(".gitattributes", "# *.bin filter=lfs\n*.bin filter=lfs-extra\n");
        repo.WriteFile("readme.txt", "hello\n");
        await using var session = await Open(repo);
        var files = await session.RepositoryFilesAsync(CancellationToken.None);
        Assert.False(await session.UsesLfsAsync(files.Select(file => file.Path).ToArray(), CancellationToken.None));
    }

    [Fact]
    public async Task Checkout_of_a_repository_without_lfs_does_not_run_lfs()
    {
        using var repo = new TempRepo();
        repo.WriteFile("readme.txt", "one\n");
        repo.CommitAll("first");
        var branch = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("readme.txt", "two\n");
        repo.CommitAll("second");
        await using var session = await Open(repo);
        await session.SwitchAsync(branch, CancellationToken.None);
        var state = session.Snapshot();
        Assert.Equal(branch, state.Branch.HeadName);
        Assert.Equal("one\n", File.ReadAllText(Path.Combine(repo.Directory, "readme.txt")).Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.DoesNotContain(state.Commands, command => command.Arguments.Contains("lfs"));
    }

    private static Dictionary<string, string> Filters(string clean, string smudge, string process) => new()
    {
        ["filter.lfs.clean"] = clean,
        ["filter.lfs.smudge"] = smudge,
        ["filter.lfs.process"] = process,
    };

    private static void DisableLocalFilters(TempRepo repo)
    {
        repo.Run("config", "filter.lfs.process", "");
        repo.Run("config", "filter.lfs.clean", "");
        repo.Run("config", "filter.lfs.smudge", "");
        repo.Run("config", "filter.lfs.required", "false");
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
