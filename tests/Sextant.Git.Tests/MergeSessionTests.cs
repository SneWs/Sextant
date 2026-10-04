using Sextant.Git.Parsing;

namespace Sextant.Git.Tests;

public class MergeSessionTests
{
    [Fact]
    public void Regions_line_up_and_taking_a_side_keeps_the_other_conflict()
    {
        var session = MergeSession.FromPieces(ConflictParser.Parse(Sample));
        Assert.Equal(2, session.ConflictCount);
        Assert.Contains("<<<<<<<", session.Result, StringComparison.Ordinal);
        Assert.DoesNotContain("<<<<<<<", session.Ours, StringComparison.Ordinal);
        Assert.Contains("main2", session.Ours, StringComparison.Ordinal);
        Assert.Contains("other2", session.Theirs, StringComparison.Ordinal);

        var second = session.Conflicts[1];
        Assert.Equal(
            MergeSession.LineIndex(session.Result, second.ResultOffset),
            MergeSession.LineIndex(session.Ours, second.OursOffset));
        Assert.Equal(
            MergeSession.LineIndex(session.Result, second.ResultOffset),
            MergeSession.LineIndex(session.Theirs, second.TheirsOffset));
        Assert.Equal(
            MergeSession.LineIndex(session.Result, second.ResultOffset),
            MergeSession.LineIndex(session.Base, second.BaseOffset));

        Assert.True(session.TakeTheirs(0));
        Assert.Equal(2, session.ConflictCount);
        Assert.Contains("other\n", session.Result, StringComparison.Ordinal);
        Assert.Contains("<<<<<<<", session.Result, StringComparison.Ordinal);
        Assert.Equal("other\n", session.Result.Substring(session.Conflicts[0].ResultOffset, session.Conflicts[0].ResultLength));
        Assert.Contains("main2", session.Result, StringComparison.Ordinal);

        var shifted = session.Conflicts[1].ResultOffset;
        session.ApplyEdit(session.Conflicts[0].ResultOffset, 0, "X");
        Assert.Equal(shifted + 1, session.Conflicts[1].ResultOffset);
        Assert.StartsWith("Xother\n", session.Result.Substring(session.Conflicts[0].ResultOffset), StringComparison.Ordinal);
    }

    [Fact]
    public void An_edit_that_crosses_a_conflict_drops_that_region()
    {
        var session = MergeSession.FromPieces(ConflictParser.Parse(Sample));
        var start = session.Result.IndexOf("e\n<<<<<<<", StringComparison.Ordinal);
        Assert.True(start > 0);
        var length = session.Result.IndexOf("middle", StringComparison.Ordinal) - start;
        session.ApplyEdit(start, length, "joined\n");
        Assert.Equal(1, session.ConflictCount);
        Assert.Contains("joined\n", session.Result, StringComparison.Ordinal);
        Assert.Contains("main2", session.Result, StringComparison.Ordinal);
        Assert.DoesNotContain("main\n=======\n", session.Result, StringComparison.Ordinal);
    }

    [Fact]
    public void Taking_both_sides_can_clear_the_markers_and_keeps_crlf()
    {
        const string file = "before\r\n<<<<<<< HEAD\r\nmain\r\n=======\r\nother\r\n>>>>>>> other\r\n";
        var session = MergeSession.FromPieces(ConflictParser.Parse(file));
        Assert.True(session.TakeTheirs(0));
        var disk = session.TextForDisk();
        Assert.Equal("before\r\nother\r\n", disk);
        Assert.DoesNotContain("<<<<<<<", disk, StringComparison.Ordinal);
    }

    [Fact]
    public void Mergetool_uses_git_config_until_a_command_is_set()
    {
        Assert.Equal(
            ["-C", "repo", "mergetool", "--no-prompt", "--", "a.txt"],
            GitCommands.Mergetool("repo", "a.txt", "  "));
        var custom = GitCommands.Mergetool("repo", "a.txt", "meld \"$LOCAL\" \"$MERGED\" \"$REMOTE\"");
        Assert.Contains("mergetool.sextant.cmd=meld \"$LOCAL\" \"$MERGED\" \"$REMOTE\"", custom);
        Assert.Contains("mergetool.keepBackup=false", custom);
        Assert.Contains("mergetool.sextant.trustExitCode=true", custom);
        Assert.Equal(["--", "a.txt"], custom.TakeLast(2));
        Assert.Contains("-t", custom);
        Assert.Contains("sextant", custom);
        Assert.Null(MergeToolCommand.Normalize("meld\n"));
        Assert.Null(MergeToolCommand.Normalize(null));
        Assert.True(MergeToolCommand.UseInAppEditor(null, new Dictionary<string, string>()));
        Assert.True(MergeToolCommand.UseInAppEditor("  ", new Dictionary<string, string> { ["merge.tool"] = "  " }));
        Assert.False(MergeToolCommand.UseInAppEditor(null, new Dictionary<string, string> { ["merge.tool"] = "meld" }));
        Assert.False(MergeToolCommand.UseInAppEditor("meld \"$LOCAL\" \"$MERGED\" \"$REMOTE\"", new Dictionary<string, string>()));
    }

    private const string Sample =
        "before\n" +
        "<<<<<<< HEAD\n" +
        "main\n" +
        "||||||| ancestor\n" +
        "base\n" +
        "=======\n" +
        "other\n" +
        ">>>>>>> other\n" +
        "middle\n" +
        "<<<<<<< HEAD\n" +
        "main2\n" +
        "=======\n" +
        "other2\n" +
        ">>>>>>> other\n" +
        "after\n";
}
