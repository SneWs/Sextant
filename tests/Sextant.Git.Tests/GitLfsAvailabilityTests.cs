namespace Sextant.Git.Tests;

public class GitLfsAvailabilityTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task Availability_uses_the_completed_probe_exit_code(int exitCode, bool expected)
    {
        Assert.Equal(expected, await GitLfsAvailability.ProbeAsync(
            _ => Task.FromResult(exitCode), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Availability_waits_for_the_probe_to_complete()
    {
        var exit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = GitLfsAvailability.ProbeAsync(_ => exit.Task, TimeSpan.FromSeconds(10));
        Assert.False(probe.IsCompleted);
        exit.SetResult(0);
        Assert.True(await probe);
    }

    [Fact]
    public async Task A_timed_out_probe_finishes_cancellation_and_reports_timeout_not_missing_lfs()
    {
        var cleanedUp = false;
        var error = await Assert.ThrowsAsync<TimeoutException>(() => GitLfsAvailability.ProbeAsync(async token =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            }
            finally
            {
                cleanedUp = true;
            }
        }, TimeSpan.FromMilliseconds(30)));

        Assert.True(cleanedUp);
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.Contains("availability could not be determined", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unexpected_probe_failure_is_not_hidden()
    {
        var failure = new InvalidOperationException("probe failed");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => GitLfsAvailability.ProbeAsync(
            _ => Task.FromException<int>(failure), TimeSpan.FromSeconds(10)));
        Assert.Same(failure, error);
    }

    [Fact]
    public async Task Availability_is_shared_across_test_classes()
    {
        var first = GitLfsAvailability.IsInstalledAsync();
        var second = GitLfsAvailability.IsInstalledAsync();
        Assert.Same(first, second);
        await first;
    }
}
