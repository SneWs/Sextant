using System.Diagnostics;
using Sextant.Git.AskPass;
using Sextant.Git.Wsl;

namespace Sextant.Git.Tests;

public class WslAskPassTests
{
    [WslFact]
    public async Task Ensure_reuses_existing_helpers_and_recreates_missing_or_non_executable_helpers()
    {
        var distribution = Environment.GetEnvironmentVariable("SEXTANT_TEST_WSL_DISTRO")!;
        var wsl = new WslGit(distribution, "/usr/bin/git", "wsl.exe");
        var pipe = "sextant-askpass-test-" + Guid.NewGuid().ToString("N");
        var executable = @"C:\Program Files\Sextant.AskPass.exe";
        var script = "/tmp/sextant-askpass-" + pipe;
        var quoted = WslAskPass.ShQuote(script);
        var expected = WslAskPass.Script(WslPath.ToLinux(distribution, executable), pipe);
        try
        {
            Assert.Equal(script, await WslAskPass.EnsureAsync(wsl, executable, pipe, CancellationToken.None));
            Assert.Equal(expected, await RunShellAsync(wsl, "test -x " + quoted + " && cat " + quoted));

            await RunShellAsync(wsl, "printf '# preserved\\n' >> " + quoted);
            Assert.Equal(script, await WslAskPass.EnsureAsync(wsl, executable, pipe, CancellationToken.None));
            Assert.Equal(expected + "# preserved\n", await RunShellAsync(wsl, "cat " + quoted));

            await RunShellAsync(wsl, "rm -- " + quoted);
            Assert.Equal(script, await WslAskPass.EnsureAsync(wsl, executable, pipe, CancellationToken.None));
            Assert.Equal(expected, await RunShellAsync(wsl, "test -x " + quoted + " && cat " + quoted));

            await RunShellAsync(wsl, "chmod 600 " + quoted);
            Assert.Equal(script, await WslAskPass.EnsureAsync(wsl, executable, pipe, CancellationToken.None));
            Assert.Equal(expected, await RunShellAsync(wsl, "test -x " + quoted + " && cat " + quoted));
        }
        finally
        {
            await RunShellAsync(wsl, "rm -f -- " + quoted);
        }
    }

    private static async Task<string> RunShellAsync(WslGit wsl, string command)
    {
        var info = new ProcessStartInfo(wsl.Launcher)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.Environment["WSL_UTF8"] = "1";
        foreach (var argument in new[] { "-d", wsl.Distribution, "-e", "/bin/sh", "-c", command })
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }

    [Fact]
    public void Wsl_script_quotes_the_helper_and_does_not_rely_on_linux_environment()
    {
        var script = WslAskPass.Script("/mnt/c/Program Files/Sextant.AskPass.exe", "sextant-askpass-abc");
        Assert.Contains("#!/bin/sh", script, StringComparison.Ordinal);
        Assert.Contains("repo=${SEXTANT_ASKPASS_REPO:-}", script, StringComparison.Ordinal);
        Assert.Contains("exec '/mnt/c/Program Files/Sextant.AskPass.exe' --pipe 'sextant-askpass-abc' --kind \"$kind\" --repo \"$repo\" \"$@\"", script, StringComparison.Ordinal);
        Assert.Equal("'a'\\''b'", WslAskPass.ShQuote("a'b"));
    }

    public sealed class WslFactAttribute : FactAttribute
    {
        public WslFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SEXTANT_TEST_WSL_DISTRO")))
                Skip = "Set SEXTANT_TEST_WSL_DISTRO to run against an installed WSL distribution.";
        }
    }

    [Fact]
    public void Wsl_launch_forwards_ssh_askpass_and_not_path()
    {
        var environment = AskPassEnvironment.ForWslGit("/tmp/sextant-askpass-pipe");
        environment["PATH"] = @"C:\Windows";
        environment[AskPassEnvironment.RepositoryVariable] = "/home/user/repo";
        var request = new GitRequest
        {
            Executable = @"C:\Program Files\Git\cmd\git.exe",
            Arguments = ["fetch"],
            WorkingDirectory = "/home/user/repo",
            Environment = environment,
            Wsl = new WslGit("Ubuntu", "/usr/bin/git", "wsl.exe"),
        };

        var launched = WslLaunch.Prepare(request);
        Assert.Contains("SSH_ASKPASS=/tmp/sextant-askpass-pipe", launched.Arguments);
        Assert.Contains("SSH_ASKPASS_REQUIRE=force", launched.Arguments);
        Assert.Contains("SEXTANT_ASKPASS_REPO=/home/user/repo", launched.Arguments);
        Assert.DoesNotContain(launched.Arguments, argument => argument.StartsWith("PATH=", StringComparison.Ordinal));
        Assert.DoesNotContain(launched.Arguments, argument => argument.StartsWith("GIT_ASKPASS=", StringComparison.Ordinal));
        Assert.DoesNotContain("DISPLAY", string.Join(' ', launched.Arguments), StringComparison.Ordinal);
    }
}
