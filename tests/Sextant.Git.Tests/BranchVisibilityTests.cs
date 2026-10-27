using System.Diagnostics;
using System.Text;
using Sextant.Git.Models;
using Sextant.Git.Parsing;
using Sextant.Git.Repo;

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
            ["--exclude=feature/grass", "--exclude=origin/side", "--exclude=feature/grass", "--exclude=origin/side", "--exclude=*/HEAD"],
            hidden.Where(argument => argument.StartsWith("--exclude=", StringComparison.Ordinal)).ToArray());
        var hiddenList = hidden.ToList();
        Assert.Contains("HEAD", hiddenList);
        Assert.Contains("--branches", hiddenList);
        Assert.Contains("--remotes", hiddenList);
        Assert.DoesNotContain("--tags", hiddenList);
        Assert.DoesNotContain("--first-parent", hiddenList);
        Assert.True(hiddenList.IndexOf("--branches") < hiddenList.IndexOf("--exclude=*/HEAD"));
        Assert.True(hiddenList.IndexOf("--exclude=*/HEAD") < hiddenList.LastIndexOf("--remotes"));
        Assert.Contains("--first-parent", GitCommands.Log("repo", 0, 10, firstParent: true));

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
    public void Log_names_visible_branches_when_excludes_exceed_the_command_line()
    {
        var hidden = new string[1500];
        for (var i = 0; i < hidden.Length; i++)
            hidden[i] = "refs/heads/b" + i.ToString("0000");
        var kept = GitCommands.LogInvocation(
            "repo",
            0,
            10,
            hiddenBranches: hidden,
            visibleBranches: ["refs/heads/main", "refs/tags/v1"],
            firstParent: true);
        Assert.Null(kept.StandardInput);
        Assert.Contains("refs/heads/main", kept.Arguments);
        Assert.Contains("HEAD", kept.Arguments);
        Assert.Contains("--first-parent", kept.Arguments);
        Assert.DoesNotContain("refs/tags/v1", kept.Arguments);
        Assert.DoesNotContain(kept.Arguments, argument => argument.StartsWith("--exclude=", StringComparison.Ordinal));
        Assert.DoesNotContain("--branches", kept.Arguments);
        Assert.DoesNotContain("--remotes", kept.Arguments);
        Assert.DoesNotContain("--stdin", kept.Arguments);

        var stillShort = GitCommands.LogInvocation(
            "repo",
            0,
            10,
            hiddenBranches: ["refs/heads/feature/grass"],
            visibleBranches: ["refs/heads/main"]);
        Assert.Contains("--exclude=feature/grass", stillShort.Arguments);
        Assert.Contains("--branches", stillShort.Arguments);
        Assert.Null(stillShort.StandardInput);

        var nowhere = GitCommands.LogInvocation(
            "repo",
            0,
            10,
            includeHead: false,
            hiddenBranches: hidden,
            visibleBranches: []);
        Assert.Contains("--glob=refs/../none", nowhere.Arguments);
        Assert.DoesNotContain(nowhere.Arguments, argument => argument == "HEAD");
        Assert.Null(nowhere.StandardInput);
    }

    [Fact]
    public void Log_reads_visible_branches_from_stdin_when_those_names_do_not_fit()
    {
        var hidden = new string[800];
        var visible = new string[800];
        for (var i = 0; i < hidden.Length; i++)
        {
            var name = new string('n', 40) + i.ToString("0000");
            hidden[i] = "refs/heads/h" + name;
            visible[i] = "refs/heads/v" + name;
        }

        var invocation = GitCommands.LogInvocation(
            "repo",
            0,
            10,
            includeHead: false,
            hiddenBranches: hidden,
            visibleBranches: visible);
        Assert.Contains("--stdin", invocation.Arguments);
        Assert.DoesNotContain(invocation.Arguments, argument => argument.StartsWith("refs/", StringComparison.Ordinal));
        Assert.DoesNotContain(invocation.Arguments, argument => argument.StartsWith("--exclude=", StringComparison.Ordinal));
        Assert.NotNull(invocation.StandardInput);
        var text = Encoding.UTF8.GetString(invocation.StandardInput);
        Assert.Contains(visible[0], text);
        Assert.Contains(visible[^1], text);
        Assert.EndsWith("\n", text);
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
        Assert.False(BranchVisibility.OnlyOneBranch([], new HashSet<string>(StringComparer.Ordinal)));
        Assert.True(BranchVisibility.OnlyOneBranch(
            [new GitRef("aaa", "refs/heads/keep", true, null)],
            new HashSet<string>(StringComparer.Ordinal)));
        Assert.False(BranchVisibility.OnlyOneBranch(
            [
                new GitRef("aaa", "refs/heads/keep", true, null),
                new GitRef("bbb", "refs/heads/other", false, null),
            ],
            new HashSet<string>(StringComparer.Ordinal)));
        Assert.True(BranchVisibility.OnlyOneBranch(
            [
                new GitRef("aaa", "refs/heads/keep", true, null),
                new GitRef("bbb", "refs/heads/other", false, null),
                new GitRef("ccc", "refs/remotes/origin/HEAD", false, null),
            ],
            new HashSet<string>(["refs/heads/other"], StringComparer.Ordinal)));
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
        Assert.False(session.Snapshot().HasHistoryQuery);

        await session.SetHiddenBranchesAsync(["refs/heads/feature/grass"], CancellationToken.None);
        Assert.Equal(["root", "side-only", "trunk-only"], Subjects(session));
        Assert.DoesNotContain(session.Snapshot().Commits, row => row.Commit.Subject == "feature-only");
        var excluded = LastLog(session);
        Assert.Contains("--exclude=feature/grass", excluded);
        Assert.DoesNotContain("--tags", excluded);
        Assert.Contains("HEAD", excluded);
        Assert.Contains("--exclude=*/HEAD", excluded);
        Assert.DoesNotContain("--first-parent", excluded);

        await session.SetHistoryAsync(HistoryQueryParser.Parse("feature-only"), CancellationToken.None);
        Assert.Empty(session.Snapshot().Commits);
        Assert.Equal("\"feature-only\"  ·  hiding feature/grass", session.Snapshot().HistoryLabel);
        Assert.True(session.Snapshot().HasHistoryQuery);

        await session.SetHistoryAsync(HistoryQueryParser.Parse("branch:feature/grass"), CancellationToken.None);
        Assert.Contains(session.Snapshot().Commits, row => row.Commit.Subject == "feature-only");
        Assert.Equal("feature/grass", session.Snapshot().HistoryLabel);

        await session.SetHistoryAsync(null, CancellationToken.None);
        Assert.Equal(["root", "side-only", "trunk-only"], Subjects(session));
        Assert.False(session.Snapshot().HasHistoryQuery);
        Assert.Equal("Hiding feature/grass", session.Snapshot().HistoryLabel);

        await session.SetHiddenBranchesAsync(
            BranchVisibility.HiddenExcept(session.Snapshot().Refs, trunkRef),
            CancellationToken.None);
        Assert.Equal(["root", "trunk-only"], Subjects(session));
        Assert.Equal("Hiding feature/grass and origin/side", session.Snapshot().HistoryLabel);
        Assert.Contains("--first-parent", LastLog(session));
        Assert.Contains("--exclude=*/HEAD", LastLog(session));

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
    public async Task Remote_head_does_not_bring_a_hidden_branch_back()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "root\n");
        repo.CommitAll("root");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "feature/grass");
        repo.WriteFile("c.txt", "feature\n");
        repo.CommitAll("feature-only");
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "trunk\n");
        repo.CommitAll("trunk-only");
        repo.Run("update-ref", "refs/remotes/origin/master", trunk);
        repo.Run("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/master");

        await using var session = await Open(repo);
        await session.SetHiddenBranchesAsync(
            BranchVisibility.HiddenExcept(session.Snapshot().Refs, "refs/heads/feature/grass"),
            CancellationToken.None);
        Assert.Equal(["feature-only", "root"], Subjects(session));
        Assert.DoesNotContain(Subjects(session), subject => subject == "trunk-only");
        Assert.Contains("--exclude=*/HEAD", LastLog(session));
        Assert.Contains("--first-parent", LastLog(session));
    }

    [Fact]
    public async Task One_visible_branch_keeps_its_own_commits()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "root\n");
        repo.CommitAll("root");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "feature/grass");
        repo.WriteFile("c.txt", "feature\n");
        repo.CommitAll("feature-only");
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "trunk\n");
        repo.CommitAll("trunk-only");
        repo.Run("merge", "--no-edit", "feature/grass");
        repo.WriteFile("a.txt", "after\n");
        repo.CommitAll("after-merge");

        await using var session = await Open(repo);
        Assert.Contains(Subjects(session), subject => subject == "feature-only");
        await session.SetHiddenBranchesAsync(
            BranchVisibility.HiddenExcept(session.Snapshot().Refs, "refs/heads/" + trunk),
            CancellationToken.None);
        var subjects = Subjects(session);
        Assert.Contains("root", subjects);
        Assert.Contains("trunk-only", subjects);
        Assert.Contains("after-merge", subjects);
        Assert.Contains(subjects, subject => subject.StartsWith("Merge branch", StringComparison.Ordinal));
        Assert.DoesNotContain("feature-only", subjects);
        Assert.Contains("--first-parent", LastLog(session));
        var rows = session.Snapshot().Commits;
        Assert.All(rows, row => Assert.Equal(0, row.Lanes.NodeLane));
        Assert.All(rows, row => Assert.True(row.Lanes.LaneCount <= 1));
        Assert.All(rows, row => Assert.True(row.Commit.Parents.Count <= 1));
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

    [Fact]
    public async Task Hiding_every_branch_but_one_keeps_the_git_command_short()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "root\n");
        repo.CommitAll("root");
        var trunk = repo.CurrentBranch();
        var trunkRef = "refs/heads/" + trunk;
        repo.Run("switch", "-c", "feature/grass");
        repo.WriteFile("c.txt", "feature\n");
        repo.CommitAll("feature-only");
        var feature = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "trunk\n");
        repo.CommitAll("trunk-only");

        var script = new StringBuilder();
        for (var i = 0; i < 1500; i++)
            script.Append("update refs/heads/b").Append(i.ToString("0000")).Append(' ').Append(feature).Append('\n');
        UpdateRefs(repo, script.ToString());

        await using var session = await Open(repo);
        await session.SetHiddenBranchesAsync(
            BranchVisibility.HiddenExcept(session.Snapshot().Refs, trunkRef),
            CancellationToken.None);
        Assert.Equal(["root", "trunk-only"], Subjects(session));
        Assert.DoesNotContain(Subjects(session), subject => subject == "feature-only");
        var log = LastLog(session);
        Assert.Contains(trunkRef, log);
        Assert.Contains("--first-parent", log);
        Assert.DoesNotContain(log, argument => argument.StartsWith("--exclude=", StringComparison.Ordinal));
        Assert.DoesNotContain("--stdin", log);
        var length = 0;
        foreach (var argument in log)
            length += argument.Length + 1;
        Assert.True(length < GitCommands.MaxCommandLine, $"log command length {length}");
    }

    private static void UpdateRefs(TempRepo repo, string script)
    {
        var info = new ProcessStartInfo
        {
            FileName = repo.Git,
            WorkingDirectory = repo.Directory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(repo.Directory);
        info.ArgumentList.Add("update-ref");
        info.ArgumentList.Add("--stdin");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        process.StandardInput.Write(script);
        process.StandardInput.Close();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error);
    }

    private static string[] Subjects(RepositorySession session) =>
        session.Snapshot().Commits.Select(row => row.Commit.Subject).OrderBy(subject => subject, StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<string> LastLog(RepositorySession session) =>
        session.Snapshot().Commands.Last(command => command.Arguments.Contains("log")).Arguments;

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
