namespace Sextant.Git.Tests;

public class GitVersionTests
{
    [Theory]
    [InlineData("git version 2.42.0", false)]
    [InlineData("git version 2.43.0", true)]
    [InlineData("git version 2.55.0.windows.5", true)]
    public void Version_floor_is_2_43(string text, bool supported)
    {
        var version = GitVersions.Parse(text);
        Assert.Equal(supported, GitVersions.IsSupported(version));
    }
}
