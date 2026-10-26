namespace Sextant.Git.Tests;

public class LinePatchTests
{
    [Fact]
    public void Line_patch_keeps_only_the_selected_change()
    {
        var patch = """
            diff --git a/a.txt b/a.txt
            --- a/a.txt
            +++ b/a.txt
            @@ -1,3 +1,4 @@
             line1
            -line2
            +line2b
             line3
            +line4
            """;
        var sliced = Staging.LinePatch.Slice(patch.Replace("\r\n", "\n", StringComparison.Ordinal), 0, 4);
        Assert.NotNull(sliced);
        Assert.Contains("@@ -1,3 +1,4 @@", sliced, StringComparison.Ordinal);
        Assert.Contains("+line4", sliced, StringComparison.Ordinal);
        Assert.DoesNotContain("+line2b", sliced, StringComparison.Ordinal);
        Assert.DoesNotContain("-line2", sliced, StringComparison.Ordinal);
        Assert.Contains(" line2", sliced, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_lines_keep_each_chosen_change_and_drop_the_rest()
    {
        var patch = """
            diff --git a/a.txt b/a.txt
            --- a/a.txt
            +++ b/a.txt
            @@ -1 +1,4 @@
             one
            +alpha
            +beta
            +gamma
            """;
        var sliced = Staging.LinePatch.Slice(patch, 0, [2, 1], reverse: false);
        Assert.NotNull(sliced);
        Assert.Contains("@@ -1,1 +1,3 @@", sliced, StringComparison.Ordinal);
        Assert.Contains("+alpha", sliced, StringComparison.Ordinal);
        Assert.Contains("+beta", sliced, StringComparison.Ordinal);
        Assert.DoesNotContain("gamma", sliced, StringComparison.Ordinal);
    }

    [Fact]
    public void Unstaging_several_lines_keeps_the_other_additions_as_context()
    {
        var patch = """
            diff --git a/a.txt b/a.txt
            --- a/a.txt
            +++ b/a.txt
            @@ -1 +1,4 @@
             one
            +alpha
            +beta
            +gamma
            """;
        var sliced = Staging.LinePatch.Slice(patch, 0, [1, 2], reverse: true);
        Assert.NotNull(sliced);
        Assert.Contains("@@ -1,2 +1,4 @@", sliced, StringComparison.Ordinal);
        Assert.Contains("+alpha", sliced, StringComparison.Ordinal);
        Assert.Contains("+beta", sliced, StringComparison.Ordinal);
        Assert.Contains(" gamma", sliced, StringComparison.Ordinal);
        Assert.DoesNotContain("+gamma", sliced, StringComparison.Ordinal);
    }

    [Fact]
    public void Unstaging_one_added_line_keeps_a_blank_addition_as_context()
    {
        var patch = """
            diff --git a/README.md b/README.md
            --- a/README.md
            +++ b/README.md
            @@ -1,3 +1,5 @@
             # This is a README ----------
             With some more lines
             and more lines
            +
            +Adding more lines...!
            """;
        var textOnly = Staging.LinePatch.Slice(patch, 0, [4], reverse: true);
        Assert.NotNull(textOnly);
        Assert.Contains("@@ -1,4 +1,5 @@", textOnly, StringComparison.Ordinal);
        Assert.Contains("+Adding more lines...!", textOnly, StringComparison.Ordinal);
        Assert.Contains("\n \n", textOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("\n+\n", textOnly, StringComparison.Ordinal);

        var blankOnly = Staging.LinePatch.Slice(patch, 0, [3], reverse: true);
        Assert.NotNull(blankOnly);
        Assert.Contains("@@ -1,4 +1,5 @@", blankOnly, StringComparison.Ordinal);
        Assert.Contains("\n+\n", blankOnly, StringComparison.Ordinal);
        Assert.Contains(" Adding more lines...!", blankOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("+Adding more lines...!", blankOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void Unstaging_leaves_out_deletions_that_were_not_chosen()
    {
        var patch = """
            diff --git a/a.txt b/a.txt
            --- a/a.txt
            +++ b/a.txt
            @@ -1,4 +1 @@
             one
            -alpha
            -beta
            -gamma
            """;
        var sliced = Staging.LinePatch.Slice(patch, 0, [2, 3], reverse: true);
        Assert.NotNull(sliced);
        Assert.Contains("@@ -1,3 +1,1 @@", sliced, StringComparison.Ordinal);
        Assert.Contains("-beta", sliced, StringComparison.Ordinal);
        Assert.Contains("-gamma", sliced, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha", sliced, StringComparison.Ordinal);
    }

    [Fact]
    public void A_context_line_cannot_be_staged_on_its_own()
    {
        var patch = """
            diff --git a/a.txt b/a.txt
            --- a/a.txt
            +++ b/a.txt
            @@ -1 +1,2 @@
             one
            +alpha
            """;
        Assert.Null(Staging.LinePatch.Slice(patch, 0, [0], reverse: false));
        Assert.Null(Staging.LinePatch.Slice(patch, 0, [], reverse: true));
    }
}
