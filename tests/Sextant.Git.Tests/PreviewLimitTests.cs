namespace Sextant.Git.Tests;

public class PreviewLimitTests
{
    [Fact]
    public void Preview_cap_is_eight_mebibytes()
    {
        Assert.True(PreviewLimit.Allows(HistoryLimits.MaxPreviewBytes));
        Assert.False(PreviewLimit.Allows(HistoryLimits.MaxPreviewBytes + 1));
        Assert.False(PreviewLimit.Allows(-1));
    }
}
