namespace Sextant.Git.Tests;

public class PerformanceAdvisorTests
{
    [Fact]
    public void Slow_status_offers_unset_performance_keys()
    {
        var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["feature.manyfiles"] = "true",
        };
        var suggestion = PerformanceAdvisor.Evaluate(TimeSpan.FromSeconds(2), config);
        Assert.NotNull(suggestion);
        Assert.False(suggestion.Value.ManyFiles);
        Assert.True(suggestion.Value.FileSystemMonitor);
        Assert.Null(PerformanceAdvisor.Evaluate(TimeSpan.FromMilliseconds(10), config));

        config["core.fsmonitor"] = "true";
        Assert.Null(PerformanceAdvisor.Evaluate(TimeSpan.FromSeconds(2), config));
    }
}
