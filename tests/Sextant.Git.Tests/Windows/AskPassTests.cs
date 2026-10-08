using System.Diagnostics;
using Sextant.Git.AskPass;

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
    public void Remembered_passphrase_is_reused_for_later_prompts_in_the_same_repository()
    {
        var session = new AskPassSession();
        var repo = @"C:\Repos\demo";
        var prompt = "Enter passphrase for key '/home/user/.ssh/id_ed25519':";
        Assert.Equal("/home/user/.ssh/id_ed25519", AskPassSession.KeyOf(prompt));
        Assert.False(session.TryReuse(repo, prompt, out _));

        session.Store(repo, prompt, "secret", remember: true);
        Assert.True(session.TryReuse(repo, "Password for 'git@github.com':", out var reused));
        Assert.Equal("secret", reused);
        Assert.True(session.TryReuse(@"c:\repos\demo\", prompt, out reused));
        Assert.Equal("secret", reused);
        Assert.False(session.TryReuse(@"C:\Repos\other", prompt, out _));
        Assert.False(session.TryReuse(repo, "Username for 'https://github.com':", out _));
    }

    [Fact]
    public void Different_keys_do_not_share_a_remembered_passphrase_without_a_repository()
    {
        var session = new AskPassSession();
        session.Store(null, "Enter passphrase for key '/home/user/.ssh/id_ed25519':", "one", remember: true);
        session.Store(null, "Enter passphrase for /home/user/.ssh/id_rsa:", "two", remember: false);

        Assert.True(session.TryReuse(null, "Enter passphrase for key '/home/user/.ssh/id_ed25519':", out var first));
        Assert.Equal("one", first);
        Assert.False(session.TryReuse(null, "Enter passphrase for /home/user/.ssh/id_rsa:", out _));
    }

    [Fact]
    public async Task Command_id_round_trips_with_the_prompt()
    {
        await using var server = new AskPassServer();
        string? seen = "unset";
        string? repo = null;
        server.Prompt = (request, _) =>
        {
            seen = request.CommandId;
            repo = request.Repository;
            return Task.FromResult<string?>("secret");
        };
        server.Start();

        var secret = await AskPassProtocol.ExchangeAsync(server.PipeName, AskPassKind.Password, "Enter passphrase", CancellationToken.None, "abc", @"C:\Repos\demo");
        Assert.Equal("secret", secret);
        Assert.Equal("abc", seen);
        Assert.Equal(@"C:\Repos\demo", repo);
    }

    [Fact]
    public async Task Helper_prints_the_secret_without_a_carriage_return()
    {
        var exe = HelperExecutable();
        Assert.NotNull(exe);

        await using var server = new AskPassServer();
        server.Prompt = (_, _) => Task.FromResult<string?>("s3cret");
        server.Start();

        var stdout = await RunHelperAsync(exe, server.PipeName, "password", "Enter passphrase");
        Assert.Equal(0, stdout.ExitCode);
        Assert.Equal("s3cret\n"u8.ToArray(), stdout.Bytes);
    }

    [Fact]
    public async Task Helper_forwards_the_repository()
    {
        var exe = HelperExecutable();
        Assert.NotNull(exe);

        await using var server = new AskPassServer();
        string? seen = null;
        server.Prompt = (request, _) =>
        {
            seen = request.Repository;
            return Task.FromResult<string?>("s3cret");
        };
        server.Start();

        var stdout = await RunHelperAsync(exe, server.PipeName, "password", "Enter passphrase", repository: @"C:\Repos\demo");
        Assert.Equal(0, stdout.ExitCode);
        Assert.Equal(@"C:\Repos\demo", seen);
    }

    [Fact]
    public async Task Helper_exits_when_the_prompt_is_cancelled()
    {
        var exe = HelperExecutable();
        Assert.NotNull(exe);

        await using var server = new AskPassServer();
        server.Prompt = (_, _) => Task.FromResult<string?>(null);
        server.Start();

        var stdout = await RunHelperAsync(exe, server.PipeName, "confirm", "Are you sure?");
        Assert.Equal(1, stdout.ExitCode);
        Assert.Empty(stdout.Bytes);
    }

    private static string? HelperExecutable()
    {
        var configuration = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.Name;
        if (string.IsNullOrEmpty(configuration))
            return null;
        var exe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Sextant.AskPass", "bin", configuration, "net10.0", "Sextant.AskPass.exe"));
        return File.Exists(exe) ? exe : null;
    }

    private static async Task<(int ExitCode, byte[] Bytes)> RunHelperAsync(string exe, string pipe, string kind, string prompt, string? command = null, string? repository = null)
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
        if (command is not null)
        {
            info.ArgumentList.Add("--command");
            info.ArgumentList.Add(command);
        }

        if (repository is not null)
        {
            info.ArgumentList.Add("--repo");
            info.ArgumentList.Add(repository);
        }

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
