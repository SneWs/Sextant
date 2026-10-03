using Sextant.Git;

namespace Sextant.Git.Tests;

public class DiffLineNumberTests
{
    [Fact]
    public void Inline_numbers_follow_the_hunk_starts()
    {
        var oldLine = 10;
        var newLine = 20;
        Assert.Equal(new DiffLineNumbers.LineNumber("10", "20"), DiffLineNumbers.For(DiffLineKind.Context, ref oldLine, ref newLine));
        Assert.Equal(new DiffLineNumbers.LineNumber("11", ""), DiffLineNumbers.For(DiffLineKind.Removed, ref oldLine, ref newLine));
        Assert.Equal(new DiffLineNumbers.LineNumber("", "21"), DiffLineNumbers.For(DiffLineKind.Added, ref oldLine, ref newLine));
        Assert.Equal(new DiffLineNumbers.LineNumber("", ""), DiffLineNumbers.For(DiffLineKind.Meta, ref oldLine, ref newLine));
        Assert.Equal(new DiffLineNumbers.LineNumber("12", "22"), DiffLineNumbers.For(DiffLineKind.Context, ref oldLine, ref newLine));
        Assert.Equal(13, oldLine);
        Assert.Equal(23, newLine);
    }

    [Fact]
    public void A_missing_side_stays_blank_and_does_not_start_at_one()
    {
        var oldLine = 0;
        var newLine = 1;
        Assert.Equal(new DiffLineNumbers.LineNumber("", "1"), DiffLineNumbers.For(DiffLineKind.Added, ref oldLine, ref newLine));
        Assert.Equal(new DiffLineNumbers.LineNumber("", "2"), DiffLineNumbers.For(DiffLineKind.Added, ref oldLine, ref newLine));
        Assert.Equal(new DiffLineNumbers.LineNumber("", ""), DiffLineNumbers.For(DiffLineKind.Meta, ref oldLine, ref newLine));
        Assert.Equal(0, oldLine);
        Assert.Equal(3, newLine);

        oldLine = 4;
        newLine = 0;
        Assert.Equal(new DiffLineNumbers.LineNumber("4", ""), DiffLineNumbers.For(DiffLineKind.Removed, ref oldLine, ref newLine));
        Assert.Equal(new DiffLineNumbers.LineNumber("5", ""), DiffLineNumbers.For(DiffLineKind.Context, ref oldLine, ref newLine));
        Assert.Equal(6, oldLine);
        Assert.Equal(0, newLine);
    }

    [Fact]
    public void Side_by_side_keeps_a_removed_line_with_its_own_number()
    {
        var hunk = new DiffHunk(1, 4, 1, 3, "@@ -1,4 +1,3 @@",
        [
            new DiffLine(DiffLineKind.Context, "keep"),
            new DiffLine(DiffLineKind.Removed, "old-a"),
            new DiffLine(DiffLineKind.Removed, "old-b"),
            new DiffLine(DiffLineKind.Added, "new-a"),
            new DiffLine(DiffLineKind.Context, "tail"),
            new DiffLine(DiffLineKind.Meta, "\\ No newline at end of file"),
        ]);

        var rows = DiffLineNumbers.SideBySide(hunk);
        Assert.Equal(
        [
            new DiffLineNumbers.SideLine("keep", "1", false, "keep", "1", false),
            new DiffLineNumbers.SideLine("old-a", "2", true, "new-a", "2", true),
            new DiffLineNumbers.SideLine("old-b", "3", true, null, "", false),
            new DiffLineNumbers.SideLine("tail", "4", false, "tail", "3", false),
        ],
        rows);
    }

    [Fact]
    public void An_added_line_with_nothing_removed_has_no_left_number()
    {
        var hunk = new DiffHunk(0, 0, 1, 2, "@@ -0,0 +1,2 @@",
        [
            new DiffLine(DiffLineKind.Added, "one"),
            new DiffLine(DiffLineKind.Added, ""),
        ]);

        Assert.Equal(
        [
            new DiffLineNumbers.SideLine(null, "", false, "one", "1", true),
            new DiffLineNumbers.SideLine(null, "", false, "", "2", true),
        ],
        DiffLineNumbers.SideBySide(hunk));
    }

    [Fact]
    public void A_loaded_file_numbers_from_one_and_skips_the_trailing_newline()
    {
        Assert.Equal("1", DiffLineNumbers.FileLine(0, 3, true));
        Assert.Equal("2", DiffLineNumbers.FileLine(1, 3, true));
        Assert.Equal("", DiffLineNumbers.FileLine(2, 3, true));
        Assert.Equal("1", DiffLineNumbers.FileLine(0, 2, false));
        Assert.Equal("2", DiffLineNumbers.FileLine(1, 2, false));
        Assert.Equal("", DiffLineNumbers.FileLine(2, 2, false));
    }
}
