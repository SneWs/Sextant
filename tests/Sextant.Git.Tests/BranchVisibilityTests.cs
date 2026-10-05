using Sextant.Git.Parsing;

namespace Sextant.Git.Tests;

public class BranchVisibilityTests
{
    [Fact]
    public void Log_arguments_exclude_hidden_branches_and_keep_the_default_walk()
    {
        var open = GitCommands.Log("repo", 0, 10).ToList();
        Assert.Contains("--tags", open);
        Assert.DoesNotContain(open, argument => argument.StartsWith("--exclude=", StringComparison.Ordinal));
        Assert.True(open.IndexOf("--branches") < open.IndexOf("--tags"));
        Assert.True(open.IndexOf("--tags") < open.IndexOf("--remotes"));

        var hidden = GitCommands.Log(
            "repo",
            0,
            10,
            hiddenBranches: ["refs/heads/feature/grass", "refs/remotes/origin/side", "refs/tags/v1", "refs/remotes/origin/HEAD"]);
        Assert.Equal(
            ["--exclude=feature/grass", "--exclude=origin/side", "--exclude=feature/grass", "--exclude=origin/side"],
            hidden.Where(argument => argument.StartsWith("--exclude=", StringComparison.Ordinal)).ToArray());
        var hiddenList = hidden.ToList();
        Assert.Contains("HEAD", hiddenList);
        Assert.Contains("--branches", hiddenList);
        Assert.Contains("--remotes", hiddenList);
        Assert.DoesNotContain("--tags", hiddenList);
        Assert.True(hiddenList.IndexOf("--branches") < hiddenList.LastIndexOf("--remotes"));

        var withoutHead = GitCommands.Log("repo", 0, 10, includeHead: false, includeStash: true, hiddenBranches: ["refs/heads/main"]);
        Assert.DoesNotContain(withoutHead, argument => argument == "HEAD");
        Assert.Contains("refs/stash", withoutHead);
        Assert.DoesNotContain("--tags", withoutHead);
        Assert.Contains("--exclude=main", withoutHead);

        var oneStash = GitCommands.Log(
            "repo",
            0,
            10,
            includeStash: true,
            hiddenBranches: ["refs/heads/main"],
            visibleStashes: ["stash@{1}"]);
        Assert.Contains("stash@{1}", oneStash);
        Assert.DoesNotContain("refs/stash", oneStash);
        Assert.DoesNotContain("--tags", oneStash);

        var noStash = GitCommands.Log("repo", 0, 10, includeStash: true, visibleStashes: []);
        Assert.DoesNotContain("refs/stash", noStash);
        Assert.DoesNotContain(noStash, argument => argument.StartsWith("stash@", StringComparison.Ordinal));
        Assert.Contains("--tags", noStash);

        var stashed = GitCommands.Log("repo", 0, 10, includeStash: true);
        Assert.Contains("refs/stash", stashed);

        var pinned = GitCommands.Log("repo", 0, 10, revision: "feature/grass", hiddenBranches: ["refs/heads/feature/grass"]);
        Assert.Contains("feature/grass", pinned);
        Assert.DoesNotContain(pinned, argument => argument.StartsWith("--exclude=", StringComparison.Ordinal));
        Assert.DoesNotContain("--branches", pinned);
    }

    [Fact]
    public void Caption_names_hidden_branches_unless_a_revision_is_pinned()
    {
        Assert.Equal("", BranchVisibility.Describe([]));
        Assert.Equal("Hiding feature/grass", BranchVisibility.Describe(["refs/heads/feature/grass"]));
        Assert.Equal(
            "Hiding feature/grass and origin/side",
            BranchVisibility.Describe(["refs/remotes/origin/side", "refs/heads/feature/grass"]));
        Assert.Equal(
            "Hiding 3 branches",
            BranchVisibility.Describe(["refs/heads/a", "refs/heads/b", "refs/remotes/origin/c"]));
        Assert.Equal("Hiding 1 stash", BranchVisibility.Describe(["stash:abc"]));
        Assert.Equal(
            "Hiding feature/grass and 1 stash",
            BranchVisibility.Describe(["stash:abc", "refs/heads/feature/grass"]));
        Assert.Equal("Hiding 2 stashes", BranchVisibility.Describe(["stash:aaa", "stash:bbb"]));
        Assert.Null(BranchVisibility.Caption(null, []));
        Assert.Equal("Hiding feature/grass", BranchVisibility.Caption(null, ["refs/heads/feature/grass"]));
        var search = HistoryQueryParser.Parse("feature-only");
        Assert.Equal("\"feature-only\"  ·  hiding feature/grass", BranchVisibility.Caption(search, ["refs/heads/feature/grass"]));
        var pinned = HistoryQueryParser.Parse("branch:feature/grass");
        Assert.Equal("feature/grass", BranchVisibility.Caption(pinned, ["refs/heads/feature/grass"]));
        Assert.Equal(
            ["refs/heads/other", "refs/remotes/origin/side"],
            BranchVisibility.HiddenExcept(
                [
                    new GitRef("aaa", "refs/heads/keep", false, null),
                    new GitRef("bbb", "refs/heads/other", false, null),
                    new GitRef("ccc", "refs/remotes/origin/side", false, null),
                    new GitRef("ddd", "refs/remotes/origin/HEAD", false, null),
                    new GitRef("eee", "refs/tags/v1", false, null),
                ],
                "refs/heads/keep"));
    }

    [Fact]
    public async Task Hidden_branches_drop_commits_reached_only_from_those_branches()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "root\n");
        repo.CommitAll("root");
        repo.WriteFile("a.txt", "trunk\n");
        repo.CommitAll("trunk-only");
        var trunk = repo.CurrentBranch();
        var trunkRef = "refs/heads/" + trunk;
        repo.Run("switch", "-c", "feature/grass", "HEAD~1");
        repo.WriteFile("c.txt", "feature\n");
        repo.CommitAll("feature-only");
        repo.Run("tag", "v-feature");
        repo.Run("switch", "-c", "side", "HEAD~1");
        repo.WriteFile("s.txt", "side\n");
        repo.CommitAll("side-only");
        var side = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("update-ref", "refs/remotes/origin/side", side);
        repo.Run("switch", trunk);
        repo.Run("branch", "-D", "side");

        await using var session = await Open(repo);
        Assert.Equal(
            ["feature-only", "root", "side-only", "trunk-only"],
            Subjects(session));
        Assert.Null(session.Snapshot().HistoryLabel);

        await session.SetHiddenBranchesAsync(["refs/remotes/origin/side"], CancellationToken.None);
        Assert.Equal(["feature-only", "root", "trunk-only"], Subjects(session));
        Assert.Equal("Hiding origin/side", session.Snapshot().HistoryLabel);

        await session.SetHiddenBranchesAsync(["refs/heads/feature/grass"], CancellationToken.None);
        Assert.Equal(["root", "side-only", "trunk-only"], Subjects(session));
        Assert.DoesNotContain(session.Snapshot().Commits, row => row.Commit.Subject == "feature-only");
        var excluded = LastLog(session);
        Assert.Contains("--exclude=feature/grass", excluded);
        Assert.DoesNotContain("--tags", excluded);
        Assert.Contains("HEAD", excluded);

        await session.SetHistoryAsync(HistoryQueryParser.Parse("feature-only"), CancellationToken.None);
        Assert.Empty(session.Snapshot().Commits);
        Assert.Equal("\"feature-only\"  ·  hiding feature/grass", session.Snapshot().HistoryLabel);

        await session.SetHistoryAsync(HistoryQueryParser.Parse("branch:feature/grass"), CancellationToken.None);
        Assert.Contains(session.Snapshot().Commits, row => row.Commit.Subject == "feature-only");
        Assert.Equal("feature/grass", session.Snapshot().HistoryLabel);

        await session.SetHistoryAsync(null, CancellationToken.None);
        Assert.Equal(["root", "side-only", "trunk-only"], Subjects(session));

        await session.SetHiddenBranchesAsync(
            BranchVisibility.HiddenExcept(session.Snapshot().Refs, trunkRef),
            CancellationToken.None);
        Assert.Equal(["root", "trunk-only"], Subjects(session));
        Assert.Equal("Hiding feature/grass and origin/side", session.Snapshot().HistoryLabel);

        await session.SetHiddenBranchesAsync([trunkRef], CancellationToken.None);
        Assert.Equal(["feature-only", "root", "side-only"], Subjects(session));
        Assert.DoesNotContain(LastLog(session), argument => argument == "HEAD");
        Assert.Contains("--exclude=" + trunk, LastLog(session));

        await session.SetHiddenBranchesAsync([], CancellationToken.None);
        Assert.Equal(["feature-only", "root", "side-only", "trunk-only"], Subjects(session));
        Assert.Null(session.Snapshot().HistoryLabel);
        Assert.Contains("--tags", LastLog(session));
    }

    [Fact]
    public async Task Hidden_stash_drops_that_stash_and_a_hidden_branch_keeps_a_visible_one()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "root\n");
        repo.CommitAll("root");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "feature/grass");
        repo.WriteFile("c.txt", "feature\n");
        repo.CommitAll("feature-only");
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "wip\n");
        repo.Run("stash", "push", "-m", "kept-stash");
        repo.WriteFile("a.txt", "other\n");
        repo.Run("stash", "push", "-m", "dropped-stash");

        await using var session = await Open(repo);
        Assert.Contains(Subjects(session), subject => subject.Contains("kept-stash", StringComparison.Ordinal));
        Assert.Contains(Subjects(session), subject => subject.Contains("dropped-stash", StringComparison.Ordinal));
        var dropped = session.Snapshot().Stashes.Single(stash => stash.Subject.Contains("dropped-stash", StringComparison.Ordinal));
        var kept = session.Snapshot().Stashes.Single(stash => stash.Subject.Contains("kept-stash", StringComparison.Ordinal));

        await session.SetHiddenBranchesAsync([BranchVisibility.StashToken(dropped.Sha)], CancellationToken.None);
        Assert.DoesNotContain(Subjects(session), subject => subject.Contains("dropped-stash", StringComparison.Ordinal));
        Assert.Contains(Subjects(session), subject => subject.Contains("kept-stash", StringComparison.Ordinal));
        Assert.Contains("feature-only", Subjects(session));
        Assert.Equal("Hiding 1 stash", session.Snapshot().HistoryLabel);
        Assert.Contains(kept.Ref, LastLog(session));
        Assert.DoesNotContain("refs/stash", LastLog(session));
        Assert.DoesNotContain(dropped.Ref, LastLog(session));

        await session.SetHiddenBranchesAsync(
            [BranchVisibility.StashToken(dropped.Sha), "refs/heads/feature/grass"],
            CancellationToken.None);
        Assert.DoesNotContain(Subjects(session), subject => subject == "feature-only");
        Assert.Contains(Subjects(session), subject => subject.Contains("kept-stash", StringComparison.Ordinal));
        Assert.DoesNotContain("--tags", LastLog(session));
        Assert.Contains("--exclude=feature/grass", LastLog(session));
        Assert.Contains(kept.Ref, LastLog(session));
    }

    [Fact]
    public async Task Opening_with_hidden_branches_skips_their_commits()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "root\n");
        repo.CommitAll("root");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "feature/grass");
        repo.WriteFile("c.txt", "feature\n");
        repo.CommitAll("feature-only");
        repo.Run("switch", trunk);

        await using var session = await RepositorySession.OpenAsync(
            new GitProcessRunner(),
            repo.Git,
            repo.Directory,
            CancellationToken.None,
            ["refs/heads/feature/grass"]);
        Assert.Equal(["root"], Subjects(session));
        Assert.Equal("Hiding feature/grass", session.Snapshot().HistoryLabel);
        Assert.Equal(1, session.Snapshot().Commands.Count(command => command.Arguments.Contains("log")));
        Assert.Contains("--exclude=feature/grass", LastLog(session));
    }

    private static string[] Subjects(RepositorySession session) =>
        session.Snapshot().Commits.Select(row => row.Commit.Subject).OrderBy(subject => subject, StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<string> LastLog(RepositorySession session) =>
        session.Snapshot().Commands.Last(command => command.Arguments.Contains("log")).Arguments;

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
