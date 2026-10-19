using Sextant.Git.Models;

namespace Sextant.Git.Tests;

public class PreviewLimitTests
{
    [Fact]
    public void Preview_cap_is_eight_mebibytes()
    {
        Assert.True(PreviewLimit.Allows(HistoryLimits.MaxPreviewBytes));
        Assert.False(PreviewLimit.Allows(HistoryLimits.MaxPreviewBytes + 1));
        Assert.False(PreviewLimit.Allows(-1));
        Assert.True(PreviewLimit.AllowsImage(HistoryLimits.MaxImageBytes));
        Assert.True(PreviewLimit.AllowsImage(HistoryLimits.MaxPreviewBytes + 1));
        Assert.False(PreviewLimit.AllowsImage(HistoryLimits.MaxImageBytes + 1));
        Assert.True(PreviewLimit.AllowsPreview("pic.png", HistoryLimits.MaxImageBytes));
        Assert.False(PreviewLimit.AllowsPreview("model.fbx", HistoryLimits.MaxPreviewBytes + 1));
        Assert.Equal("20 MB", ImageFiles.FormatBytes(HistoryLimits.MaxImageBytes));
    }
}
