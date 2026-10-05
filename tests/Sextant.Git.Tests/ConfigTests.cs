using Sextant.Git.Parsing;

namespace Sextant.Git.Tests;

public class ConfigTests
{
    [Fact]
    public void Config_flag_accepts_git_true_values()
    {
        foreach (var value in new[] { "true", "yes", "on", "1", " TRUE " })
        {
            var config = new Dictionary<string, string> { ["core.sparseCheckout"] = value };
            Assert.True(ConfigParser.IsEnabled(config, "core.sparseCheckout"));
        }

        Assert.False(ConfigParser.IsEnabled(new Dictionary<string, string> { ["core.sparseCheckout"] = "false" }, "core.sparseCheckout"));
        Assert.False(ConfigParser.IsEnabled(new Dictionary<string, string>(), "core.sparseCheckout"));
    }
}
