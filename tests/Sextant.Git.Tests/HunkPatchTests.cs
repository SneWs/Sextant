using Sextant.Git.Staging;

namespace Sextant.Git.Tests;

public class HunkPatchTests
{
    [Fact]
    public void Hunk_slice_keeps_only_the_selected_hunk()
    {
        var patch = "diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -1,1 +1,1 @@\n-a\n+b\n@@ -8,1 +8,1 @@\n-c\n+d\n";
        var slice = HunkPatch.Slice(patch, 1);
        Assert.Contains("@@ -8,1 +8,1 @@", slice, StringComparison.Ordinal);
        Assert.DoesNotContain("@@ -1,1 +1,1 @@", slice, StringComparison.Ordinal);
        Assert.StartsWith("diff --git", slice, StringComparison.Ordinal);
    }
}
