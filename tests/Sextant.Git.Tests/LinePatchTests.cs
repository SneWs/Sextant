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
        var sliced = LinePatch.Slice(patch.Replace("\r\n", "\n", StringComparison.Ordinal), 0, 4);
        Assert.NotNull(sliced);
        Assert.Contains("@@ -1,3 +1,4 @@", sliced, StringComparison.Ordinal);
        Assert.Contains("+line4", sliced, StringComparison.Ordinal);
        Assert.DoesNotContain("+line2b", sliced, StringComparison.Ordinal);
        Assert.DoesNotContain("-line2", sliced, StringComparison.Ordinal);
        Assert.Contains(" line2", sliced, StringComparison.Ordinal);
    }
}
