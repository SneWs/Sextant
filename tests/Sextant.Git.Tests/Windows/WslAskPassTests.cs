namespace Sextant.Git.Tests;

public class WslAskPassTests
{
    [Fact]
    public void Wsl_script_quotes_the_helper_and_does_not_rely_on_linux_environment()
    {
        var script = WslAskPass.Script("/mnt/c/Program Files/Sextant.AskPass.exe", "sextant-askpass-abc");
        Assert.Contains("#!/bin/sh", script, StringComparison.Ordinal);
        Assert.Contains("repo=${SEXTANT_ASKPASS_REPO:-}", script, StringComparison.Ordinal);
        Assert.Contains("exec '/mnt/c/Program Files/Sextant.AskPass.exe' --pipe 'sextant-askpass-abc' --kind \"$kind\" --repo \"$repo\" \"$@\"", script, StringComparison.Ordinal);
        Assert.Equal("'a'\\''b'", WslAskPass.ShQuote("a'b"));
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
