using Sextant;

namespace Sextant.Git.Tests;

public class AppInfoTests
{
    [Fact]
    public void Display_version_drops_the_tag_prefix_and_the_source_revision()
    {
        Assert.Equal("0.1.3-beta", AppInfo.DisplayVersion("v0.1.3-beta"));
        Assert.Equal("0.1.2-beta2", AppInfo.DisplayVersion("V0.1.2-beta2+abc123"));
        Assert.Equal("0.1.1-beta1", AppInfo.DisplayVersion("0.1.1-beta1"));
        Assert.Equal("1.0.0", AppInfo.DisplayVersion("1.0.0+sha"));
    }
}
