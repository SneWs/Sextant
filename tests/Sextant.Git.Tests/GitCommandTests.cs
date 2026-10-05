namespace Sextant.Git.Tests;

public class GitCommandTests
{
    [Fact]
    public void Reads_use_no_optional_locks_and_writes_do_not()
    {
        Assert.Contains("--no-optional-locks", GitCommands.Status("repo"));
        Assert.Contains("--no-optional-locks", GitCommands.Log("repo", 0, 10));
        Assert.DoesNotContain("--no-optional-locks", GitCommands.Commit("repo", "msg"));
        Assert.DoesNotContain("--no-verify", GitCommands.Commit("repo", "msg"));
        Assert.Contains("--no-verify", GitCommands.Commit("repo", "msg", noVerify: true));
        Assert.Contains("-A", GitCommands.StageAll("repo"));
        Assert.Equal(":", GitCommands.UnstageAll("repo")[^1]);
        Assert.Equal(["rm", "-r", "--cached", "-f", "--", "."], GitCommands.UnstageAllUnborn("repo").Skip(2));
        Assert.DoesNotContain("--no-optional-locks", GitCommands.Fetch("repo"));
        Assert.DoesNotContain("--all", GitCommands.Fetch("repo"));
        Assert.DoesNotContain("--prune", GitCommands.Fetch("repo"));
        Assert.Equal(["-C", "repo", "fetch", "--all", "--progress"], GitCommands.FetchAll("repo"));
        Assert.Equal(["-C", "repo", "fetch", "--all", "--prune", "--progress"], GitCommands.FetchAllPrune("repo"));
        Assert.Equal(["-C", "repo", "branch", "topic", "abc"], GitCommands.CreateBranchAt("repo", "topic", "abc"));
        Assert.Equal(["-C", "repo", "--no-optional-locks", "format-patch", "-1", "--stdout", "abc"], GitCommands.FormatPatch("repo", "abc"));
        Assert.Equal(["-C", "repo", "--no-optional-locks", "check-attr", "--stdin", "-z", "filter"], GitCommands.CheckLfsAttr("repo", null));
        Assert.Equal(["-C", "repo", "--no-optional-locks", "check-attr", "--stdin", "-z", "--source", "abc", "filter"], GitCommands.CheckLfsAttr("repo", "abc"));
        Assert.Equal(["-C", "repo", "lfs", "track", "--filename", "--", "a.bin"], GitCommands.LfsTrack("repo", "a.bin"));
        Assert.Equal(["-C", "repo", "lfs", "untrack", "--", "a.bin"], GitCommands.LfsUntrack("repo", "a.bin"));
        Assert.Equal(["-C", "repo", "lfs", "fetch"], GitCommands.LfsFetch("repo"));
        Assert.Equal(["-C", "repo", "lfs", "pull"], GitCommands.LfsPull("repo"));
        Assert.Equal(["-C", "repo", "lfs", "pull", "--include=dir/a.bin"], GitCommands.LfsPullFile("repo", "dir/a.bin"));
        Assert.Equal(@"dir/a\[1].bin", GitCommands.LfsInclude("dir/a[1].bin"));
        Assert.Equal(@"dir/a\*.bin", GitCommands.LfsInclude("dir/a*.bin"));
        Assert.Equal(@"dir/a\?.bin", GitCommands.LfsInclude("dir/a?.bin"));
        Assert.Equal("dir/a,b.bin", GitCommands.LfsInclude("dir/a,b.bin"));
        Assert.True(GitCommands.LfsNameHasComma("dir/a,b.bin"));
        Assert.False(GitCommands.LfsNameHasComma("dir/a.bin"));
        Assert.Equal(["-C", "repo", "lfs", "checkout", "--", "dir/a,b.bin"], GitCommands.LfsCheckout("repo", "dir/a,b.bin"));
        Assert.Equal(["-C", "repo", "lfs", "checkout"], GitCommands.LfsCheckoutAll("repo"));
        Assert.DoesNotContain("--include", GitCommands.LfsCheckout("repo", "dir/a,b.bin"));
        Assert.Equal("dir/a].bin", GitCommands.LfsInclude("dir/a].bin"));
        Assert.Equal("dir/a.bin", GitCommands.LfsInclude("dir\\a.bin"));
        Assert.DoesNotContain("--all", GitCommands.LfsFetch("repo"));
        Assert.DoesNotContain("checkout", GitCommands.LfsFetch("repo"));
        Assert.Equal(["-C", "repo", "apply", "--", "change.patch"], GitCommands.ApplyPatch("repo", "change.patch"));
        Assert.DoesNotContain("--cached", GitCommands.ApplyPatch("repo", "change.patch"));
        Assert.DoesNotContain("--index", GitCommands.ApplyPatch("repo", "change.patch"));
        Assert.DoesNotContain("am", GitCommands.ApplyPatch("repo", "change.patch"));
        Assert.DoesNotContain("--no-optional-locks", GitCommands.CreateBranchAt("repo", "topic", "abc"));
        Assert.Equal(["-C", "repo", "switch", "--detach", "v1"], GitCommands.SwitchDetach("repo", "v1"));
        Assert.Equal(["-C", "repo", "switch", "-c", "feature/name", "--track", "origin/feature/name"], GitCommands.SwitchCreateTrack("repo", "feature/name", "origin/feature/name"));
        Assert.Equal("feature/name", GitCommands.LocalBranchOfRemote("origin/feature/name"));
        Assert.Equal("master", GitCommands.LocalBranchOfRemote("upstream/master"));
        Assert.Null(GitCommands.LocalBranchOfRemote("origin"));
        Assert.Contains("--no-optional-locks", GitCommands.VerifyLocalBranch("repo", "feature/name"));
        Assert.Equal("refs/heads/feature/name", GitCommands.VerifyLocalBranch("repo", "feature/name")[^1]);
        Assert.Equal(["-C", "repo", "push", "--progress", "origin", "refs/tags/v1"], GitCommands.PushTag("repo", "origin", "v1"));
        Assert.Equal(["-C", "repo", "push", "--progress", "origin", "--delete", "refs/tags/v1"], GitCommands.DeleteRemoteTag("repo", "origin", "v1"));
        Assert.DoesNotContain("--no-optional-locks", GitCommands.SwitchDetach("repo", "v1"));
        Assert.DoesNotContain("--no-optional-locks", GitCommands.PushTag("repo", "origin", "v1"));
        Assert.DoesNotContain("--no-optional-locks", GitCommands.DeleteRemoteTag("repo", "origin", "v1"));
        Assert.Equal(["-C", "repo", "branch", "-d", "topic"], GitCommands.DeleteBranch("repo", "topic"));
        Assert.Equal(["-C", "repo", "branch", "-D", "topic"], GitCommands.ForceDeleteBranch("repo", "topic"));
        Assert.Equal(["-C", "repo", "push", "--progress", "origin", "--delete", "topic"], GitCommands.DeleteRemoteBranch("repo", "origin", "topic"));
        Assert.Equal(["-C", "repo", "--no-optional-locks", "rev-list", "--count", "HEAD..origin/topic"], GitCommands.NotInHeadCount("repo", "origin/topic"));
        Assert.Contains("--no-edit", GitCommands.Merge("repo", "topic"));
        Assert.Equal(["-C", "repo", "rebase", "topic"], GitCommands.Rebase("repo", "topic"));
        Assert.Contains("--no-edit", GitCommands.Pull("repo"));
        Assert.Contains("--rebase", GitCommands.Pull("repo"));
        Assert.DoesNotContain("--no-verify", GitCommands.Push("repo"));
        Assert.Contains("--no-verify", GitCommands.Push("repo", noVerify: true));
        Assert.DoesNotContain("--no-verify", GitCommands.PushUpstream("repo", "origin", "topic"));
        Assert.Contains("--no-verify", GitCommands.PushUpstream("repo", "origin", "topic", noVerify: true));
        Assert.Equal(["-u", "origin", "topic"], GitCommands.PushUpstream("repo", "origin", "topic", noVerify: true).TakeLast(3));
        Assert.Contains("--force-with-lease", GitCommands.PushForceWithLease("repo"));
        Assert.DoesNotContain(GitCommands.PushForceWithLease("repo"), argument => argument == "--force");
        Assert.Equal(["-C", "repo", "commit", "--amend", "-C", "HEAD"], GitCommands.Amend("repo", null));
        Assert.Contains("-F", GitCommands.Amend("repo", "msg"));
        Assert.Contains("--root", GitCommands.RebaseInteractive("repo", null));
        Assert.Equal("abc", GitCommands.RebaseInteractive("repo", "abc")[^1]);
        Assert.DoesNotContain("--root", GitCommands.RebaseInteractive("repo", "abc"));
        Assert.Contains("--", GitCommands.Stage("repo", "a file.txt"));
        Assert.Equal(["rm", "--cached", "-f", "--", "a.txt"], GitCommands.UnstageUnborn("repo", "a.txt").Skip(2));
        Assert.Equal(["rm", "-f", "--", "a.txt"], GitCommands.DiscardUnborn("repo", "a.txt").Skip(2));
        Assert.Contains("restore", GitCommands.Unstage("repo", "a.txt"));
        Assert.Equal("-f", GitCommands.DiscardUntracked("repo", "a.txt")[3]);
        Assert.DoesNotContain("-d", GitCommands.DiscardUntracked("repo", "a.txt"));
        Assert.DoesNotContain("-x", GitCommands.DiscardUntracked("repo", "a.txt"));
        Assert.Equal(
            ["-C", "repo", "restore", "--source=HEAD", "--worktree", "--staged", "--", "a.txt", "old.txt"],
            GitCommands.DiscardTrackedPaths("repo", ["a.txt", "old.txt"]));
        Assert.Equal(["-C", "repo", "rm", "-f", "--", "a.txt"], GitCommands.DiscardUnbornPaths("repo", ["a.txt"]));
        Assert.Equal(["-C", "repo", "clean", "-fd", "--", "new.txt"], GitCommands.DiscardUntrackedPaths("repo", ["new.txt"]));
        Assert.DoesNotContain("-x", GitCommands.DiscardUntrackedPaths("repo", ["new.txt"]));
    }

    [Fact]
    public void Diff_and_show_keep_lfs_pointers_and_status_does_not()
    {
        Assert.DoesNotContain("filter.lfs.process=", GitCommands.DiffWorktree("repo", false, false));
        Assert.DoesNotContain("filter.lfs.smudge=", GitCommands.DiffWorktree("repo", false, false));
        Assert.DoesNotContain("filter.lfs.required=false", GitCommands.DiffWorktree("repo", false, false));
        Assert.Contains("--no-textconv", GitCommands.DiffWorktree("repo", false, false));
        Assert.DoesNotContain("diff.lfs.textconv=", GitCommands.DiffWorktree("repo", false, false));
        Assert.Contains("filter.lfs.process=", GitCommands.DiffWorktree("repo", true, false));
        Assert.Contains("--no-textconv", GitCommands.DiffWorktree("repo", true, false));
        Assert.Contains("filter.lfs.process=", GitCommands.DiffUntracked("repo", "a.bin"));
        Assert.Contains("--no-textconv", GitCommands.DiffRange("repo", "a", "b", "file.bin"));
        Assert.Contains("--no-textconv", GitCommands.ShowPatch("repo", "abc", "a.bin"));
        Assert.Contains("--no-textconv", GitCommands.ShowStage("repo", 2, "a.bin"));
        Assert.Contains("--no-textconv", GitCommands.Blame("repo", "HEAD", "a.bin"));
        Assert.DoesNotContain("--no-textconv", GitCommands.Status("repo"));
        Assert.DoesNotContain("filter.lfs.process=", GitCommands.Status("repo"));
        Assert.DoesNotContain("filter.lfs.smudge=", GitCommands.Status("repo"));
        Assert.DoesNotContain(GitCommands.PushForceWithLease("repo"), argument => argument == "--force");

        var added = GitCommands.WorktreeAdd("repo", "C:/wt", "feature", "HEAD", noCheckout: true);
        Assert.Contains("--no-checkout", added);
        Assert.Contains("-b", added);
        Assert.DoesNotContain("--force", added);
        Assert.Contains("--no-cone", GitCommands.SparseSet("wt", false, ["keep"]));
        Assert.DoesNotContain("--no-cone", GitCommands.SparseSet("wt", true, ["keep"]));
        Assert.Contains("--", GitCommands.SparseSet("wt", true, ["keep"]));
        Assert.Equal("HEAD:./skip/gone.txt", GitCommands.ObjectSpec("HEAD", "skip\\gone.txt"));
        Assert.Equal(":./a.txt", GitCommands.ObjectSpec("", "a.txt"));
        Assert.Contains("--no-optional-locks", GitCommands.CatFileBlob("repo", "HEAD:./a"));
        Assert.DoesNotContain("--no-optional-locks", GitCommands.CatFileFiltered("repo", "HEAD:./a"));
        Assert.Equal(["-C", "wt", "checkout"], GitCommands.CheckoutCurrent("wt"));
    }
}
