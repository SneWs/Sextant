namespace Sextant.Git.Tests;

internal static class GitLfsAvailability
{
    private static readonly Lazy<Task<bool>> Installed = new(CheckInstalledAsync);

    public static Task<bool> IsInstalledAsync() => Installed.Value;

    private static async Task<bool> CheckInstalledAsync()
    {
        var executable = GitLocator.FindOnPath();
        if (executable is null)
            return false;

        return await ProbeAsync(async token =>
        {
            var output = await new GitProcessRunner().RunAsync(new GitRequest
            {
                Executable = executable,
                Arguments = ["lfs", "version"],
            }, token).ConfigureAwait(false);
            return output.ExitCode;
        }, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
    }

    internal static async Task<bool> ProbeAsync(Func<CancellationToken, Task<int>> run, TimeSpan allowance)
    {
        using var timeout = new CancellationTokenSource(allowance);
        try
        {
            return await run(timeout.Token).ConfigureAwait(false) == 0;
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"git lfs version did not exit within {allowance.TotalSeconds} seconds. The probe was cancelled; Git LFS availability could not be determined.",
                exception);
        }
    }
}
