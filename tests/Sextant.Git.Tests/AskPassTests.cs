using System.Diagnostics;

namespace Sextant.Git.Tests;

public class AskPassTests
{
    [Fact]
    public async Task Pipe_round_trip_returns_the_secret_and_a_cancel()
    {
        await using var server = new AskPassServer();
        server.Prompt = (request, _) => Task.FromResult<string?>(request.Kind == AskPassKind.Password ? "secret" : null);
        server.Start();

        var secret = await AskPassProtocol.ExchangeAsync(server.PipeName, AskPassKind.Password, "Enter passphrase", CancellationToken.None);
        var cancelled = await AskPassProtocol.ExchangeAsync(server.PipeName, AskPassKind.Confirm, "Are you sure?", CancellationToken.None);

        Assert.Equal("secret", secret);
        Assert.Null(cancelled);
    }

    [Fact]
    public async Task Empty_unlock_is_distinct_from_cancel()
    {
        await using var server = new AskPassServer();
        server.Prompt = (_, _) => Task.FromResult<string?>("");
        server.Start();

        var secret = await AskPassProtocol.ExchangeAsync(server.PipeName, AskPassKind.Password, "passphrase", CancellationToken.None);
        Assert.Equal("", secret);
    }

    [Fact]
    public void Wsl_script_quotes_the_helper_and_does_not_rely_on_linux_environment()
    {
        var script = WslAskPass.Script("/mnt/c/Program Files/Sextant.AskPass.exe", "sextant-askpass-abc");
        Assert.Contains("#!/bin/sh", script, StringComparison.Ordinal);
        Assert.Contains("exec '/mnt/c/Program Files/Sextant.AskPass.exe' --pipe 'sextant-askpass-abc' --kind \"$kind\" \"$@\"", script, StringComparison.Ordinal);
        Assert.Equal("'a'\\''b'", WslAskPass.ShQuote("a'b"));
    }

    [Fact]
    public void Wsl_launch_forwards_ssh_askpass_and_not_path()
    {
        var environment = AskPassEnvironment.ForWslGit("/tmp/sextant-askpass-pipe");
        environment["PATH"] = @"C:\Windows";
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
        Assert.DoesNotContain(launched.Arguments, argument => argument.StartsWith("PATH=", StringComparison.Ordinal));
        Assert.DoesNotContain(launched.Arguments, argument => argument.StartsWith("GIT_ASKPASS=", StringComparison.Ordinal));
        Assert.DoesNotContain("DISPLAY", string.Join(' ', launched.Arguments), StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_askpass_environment_points_at_the_helper_and_the_pipe()
    {
        var environment = AskPassEnvironment.ForWindowsGit(@"C:\Apps\Sextant.AskPass.exe", "sextant-askpass-pipe");
        Assert.Equal(@"C:\Apps\Sextant.AskPass.exe", environment["SSH_ASKPASS"]);
        Assert.Equal("force", environment["SSH_ASKPASS_REQUIRE"]);
        Assert.Equal("sextant-askpass-pipe", environment["SEXTANT_ASKPASS"]);
        Assert.False(environment.ContainsKey("GIT_ASKPASS"));
        Assert.False(environment.ContainsKey("PATH"));
        Assert.Equal(AskPassKind.Confirm, AskPassEnvironment.KindOf("confirm"));
        Assert.Equal(AskPassKind.Message, AskPassEnvironment.KindOf("none"));
        Assert.Equal(AskPassKind.Password, AskPassEnvironment.KindOf(null));
    }

    [Fact]
    public async Task Non_windows_does_not_launch_wsl()
    {
        if (OperatingSystem.IsWindows())
            return;

        var runner = new GitProcessRunner
        {
            AskPass = new AskPassLaunch("/tmp/Sextant.AskPass", "sextant-askpass-pipe"),
        };
        var output = await runner.RunAsync(new GitRequest
        {
            Executable = "git",
            Arguments = ["--version"],
            Wsl = new WslGit("Ubuntu", "/usr/bin/git", "wsl"),
        }, CancellationToken.None);

        Assert.Equal(0, output.ExitCode);
        Assert.DoesNotContain(output.DisplayArguments, argument => argument.Equals("wsl", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Helper_prints_the_secret_without_a_carriage_return()
    {
        var exe = HelperExecutable();
        if (exe is null)
            return;

        await using var server = new AskPassServer();
        server.Prompt = (_, _) => Task.FromResult<string?>("s3cret");
        server.Start();

        var stdout = await RunHelperAsync(exe, server.PipeName, "password", "Enter passphrase");
        Assert.Equal(0, stdout.ExitCode);
        Assert.Equal("s3cret\n"u8.ToArray(), stdout.Bytes);
    }

    [Fact]
    public async Task Helper_exits_when_the_prompt_is_cancelled()
    {
        var exe = HelperExecutable();
        if (exe is null)
            return;

        await using var server = new AskPassServer();
        server.Prompt = (_, _) => Task.FromResult<string?>(null);
        server.Start();

        var stdout = await RunHelperAsync(exe, server.PipeName, "confirm", "Are you sure?");
        Assert.Equal(1, stdout.ExitCode);
        Assert.Empty(stdout.Bytes);
    }

    private static string? HelperExecutable()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var configuration = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.Parent?.Name;
        if (string.IsNullOrEmpty(configuration))
            return null;
        var exe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Sextant.AskPass", "bin", configuration, "net10.0", "Sextant.AskPass.exe"));
        return File.Exists(exe) ? exe : null;
    }

    private static async Task<(int ExitCode, byte[] Bytes)> RunHelperAsync(string exe, string pipe, string kind, string prompt)
    {
        var info = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("--pipe");
        info.ArgumentList.Add(pipe);
        info.ArgumentList.Add("--kind");
        info.ArgumentList.Add(kind);
        info.ArgumentList.Add(prompt);
        using var process = Process.Start(info);
        Assert.NotNull(process);
        var bytes = await ReadAllAsync(process.StandardOutput.BaseStream);
        await process.WaitForExitAsync();
        return (process.ExitCode, bytes);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }
}
