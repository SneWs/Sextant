using System.ComponentModel;
using System.Diagnostics;

namespace Sextant.Git.Wsl;

public sealed record WslRepository(
    string Distribution,
    string LinuxPath,
    string WindowsPath,
    WslGit Git,
    string? Problem);

public sealed record WslOutput(int ExitCode, string Stdout, string StandardError);

/// <summary>
/// Asks a WSL2 distribution for its git binary and for the Linux folder of a repository.
/// </summary>
public static class WslProbe
{
    public static string? FindLauncher()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var system = Path.Combine(Environment.SystemDirectory, "wsl.exe");
        if (File.Exists(system))
            return system;
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(entry.Trim(), "wsl.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static async Task<IReadOnlyList<WslDistro>> ListUserDistrosAsync(CancellationToken cancellationToken)
    {
        if (!WslList.IsWindows11())
            return [];
        var launcher = FindLauncher();
        if (launcher is null)
            return [];
        try
        {
            var output = await RunAsync(launcher, ["--list", "--verbose"], cancellationToken, TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);
            if (output.ExitCode != 0 && string.IsNullOrWhiteSpace(output.Stdout))
                return [];
            return WslList.UserDistros(WslList.Parse(output.Stdout));
        }
        catch (Exception) when (IsProbeFailure(cancellationToken))
        {
            return [];
        }
    }

    public static async Task<WslGit> BindingAsync(string distribution, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return Failed(distribution, "", "WSL is only available on Windows.");
        var launcher = FindLauncher() ?? "wsl.exe";
        string git;
        try
        {
            git = await GitExecutableAsync(launcher, distribution, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsProbeFailure(cancellationToken))
        {
            return Failed(distribution, launcher, exception.Message);
        }

        if (git.Length == 0)
            return Failed(distribution, launcher, "Git was not found in " + distribution + ". Install git inside that distribution.");

        var version = await RunAsync(launcher, ["-d", distribution, "-e", git, "--version"], cancellationToken, TimeSpan.FromSeconds(30))
            .ConfigureAwait(false);
        if (version.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(version.StandardError) ? "Git could not be started in " + distribution + "." : version.StandardError.Trim();
            return Failed(distribution, launcher, detail);
        }

        var parsed = GitVersions.Parse(version.Stdout);
        if (!GitVersions.IsSupported(parsed))
            return Failed(distribution, launcher, "Git " + parsed.Raw.Trim() + " in " + distribution + " is older than 2.43.");
        return new WslGit(distribution, git, launcher);
    }

    public static async Task<string?> HomeAsync(string distribution, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var launcher = FindLauncher();
        if (launcher is null)
            return null;
        var output = await RunAsync(launcher, ["-d", distribution, "--cd", "~", "-e", "pwd"], cancellationToken, TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);
        var home = FirstPath(output.Stdout);
        return output.ExitCode == 0 && home.StartsWith('/') ? home : null;
    }

    public static async Task<string?> ToWindowsAsync(string distribution, string linuxPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var launcher = FindLauncher();
        if (launcher is null)
            return WslPath.ToWindows(distribution, linuxPath);
        var output = await RunAsync(
            launcher,
            ["-d", distribution, "-e", "wslpath", "-w", linuxPath],
            cancellationToken,
            TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        var converted = output.Stdout.Trim();
        if (output.ExitCode == 0 && WslPath.TryParseUnc(converted, out _, out _))
            return WslPath.CanonicalWindows(converted);
        return WslPath.ToWindows(distribution, linuxPath);
    }

    public static async Task<WslRepository> ResolveAsync(string distribution, string picked, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WslRepository(
                distribution,
                picked,
                picked,
                new WslGit(distribution, "", ""),
                "WSL is only available on Windows.");
        }

        var git = await BindingAsync(distribution, cancellationToken).ConfigureAwait(false);
        var linux = await ToLinuxPathAsync(git.Launcher, distribution, picked, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(linux) || !linux.StartsWith('/'))
        {
            return new WslRepository(
                distribution,
                picked,
                picked,
                git,
                "That folder could not be read from " + distribution + ".");
        }

        if (!git.CanRun)
            return new WslRepository(distribution, linux, WslPath.ToWindows(distribution, linux), git, git.Problem);

        var top = await RunAsync(
            git.Launcher,
            ["-d", distribution, "-e", git.GitExecutable, "-C", linux, "rev-parse", "--show-toplevel"],
            cancellationToken,
            TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        if (top.ExitCode != 0)
        {
            var error = top.StandardError;
            var dubious = error.Contains("dubious ownership", StringComparison.OrdinalIgnoreCase)
                || error.Contains("safe.directory", StringComparison.OrdinalIgnoreCase);
            if (!dubious)
            {
                var problem = error.Contains("not a git repository", StringComparison.OrdinalIgnoreCase)
                    ? "That folder is not a Git repository in " + distribution + "."
                    : string.IsNullOrWhiteSpace(error) ? "Git failed in " + distribution + "." : error.Trim();
                return new WslRepository(distribution, linux, WslPath.ToWindows(distribution, linux), git, problem);
            }
        }

        var linuxTop = top.ExitCode == 0 ? FirstPath(top.Stdout) : linux;
        if (!linuxTop.StartsWith('/'))
            linuxTop = linux;
        var windows = await ToWindowsAsync(distribution, linuxTop, cancellationToken).ConfigureAwait(false)
            ?? WslPath.ToWindows(distribution, linuxTop);
        return new WslRepository(distribution, linuxTop, WslPath.CanonicalWindows(windows), git, null);
    }

    private static async Task<string> ToLinuxPathAsync(
        string launcher,
        string distribution,
        string picked,
        CancellationToken cancellationToken)
    {
        if (WslPath.IsLinuxAbsolute(picked.Trim()))
            return WslPath.ToLinux(distribution, picked);
        if (launcher.Length == 0 || !File.Exists(launcher))
            return WslPath.ToLinux(distribution, picked);
        var output = await RunAsync(
            launcher,
            ["-d", distribution, "-e", "wslpath", "-u", picked],
            cancellationToken,
            TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        var converted = FirstPath(output.Stdout);
        if (output.ExitCode == 0 && converted.StartsWith('/'))
            return converted;
        return WslPath.ToLinux(distribution, picked);
    }

    private static async Task<string> GitExecutableAsync(string launcher, string distribution, CancellationToken cancellationToken)
    {
        var found = await RunAsync(
            launcher,
            ["-d", distribution, "-e", "/bin/sh", "-lc", "command -v git"],
            cancellationToken,
            TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        var path = LastLinuxPath(found.Stdout);
        if (found.ExitCode == 0 && path.StartsWith('/'))
            return path;

        var fallback = await RunAsync(
            launcher,
            ["-d", distribution, "-e", "/usr/bin/git", "--version"],
            cancellationToken,
            TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return fallback.ExitCode == 0 ? "/usr/bin/git" : "";
    }

    private static WslGit Failed(string distribution, string launcher, string problem) =>
        new(distribution, "", launcher) { Problem = problem };

    private static string FirstPath(string text)
    {
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                return trimmed;
        }

        return "";
    }

    private static string LastLinuxPath(string text)
    {
        var found = "";
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('/'))
                found = trimmed;
        }

        return found;
    }

    private static bool IsProbeFailure(CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested;

    internal static async Task<WslOutput> RunAsync(
        string launcher,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        if (!OperatingSystem.IsWindows())
            return new WslOutput(-1, "", "WSL is only available on Windows.");
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var info = new ProcessStartInfo
        {
            FileName = launcher,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        info.Environment["WSL_UTF8"] = "1";
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
                return new WslOutput(-1, "", "Failed to start wsl.exe.");
        }
        catch (Win32Exception exception)
        {
            return new WslOutput(-1, "", exception.Message);
        }

        var token = timeoutSource.Token;
        using var registration = token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        });

        var stdoutTask = ReadBytesAsync(process.StandardOutput.BaseStream);
        var stderrTask = ReadBytesAsync(process.StandardError.BaseStream);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new WslOutput(-1, "", "WSL did not answer.");
        }

        var stdoutBytes = await stdoutTask.ConfigureAwait(false);
        var stdout = WslList.Decode(stdoutBytes);
        var stderr = WslList.Decode(await stderrTask.ConfigureAwait(false));
        if (process.ExitCode != 0)
        {
            var note = WslLaunch.LauncherNote(stdoutBytes);
            if (note is not null)
                return new WslOutput(process.ExitCode, "", string.IsNullOrWhiteSpace(stderr) ? note : stderr.Trim() + Environment.NewLine + note);
        }

        return new WslOutput(process.ExitCode, stdout, stderr);
    }

    private static async Task<byte[]> ReadBytesAsync(Stream stream)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory).ConfigureAwait(false);
        return memory.ToArray();
    }
}
