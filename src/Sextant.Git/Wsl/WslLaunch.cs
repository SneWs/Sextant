namespace Sextant.Git.Wsl;

/// <summary>
/// Git inside one WSL2 distribution. Commands run as that distribution's default
/// user, so they use its git binary, config, hooks, and credential helpers.
/// </summary>
public sealed record WslGit(string Distribution, string GitExecutable, string Launcher)
{
    public string? Problem { get; init; }

    public bool CanRun => Problem is null && GitExecutable.Length > 0 && Launcher.Length > 0;
}

/// <summary>
/// Turns a git request into <c>wsl.exe -d distro --cd path -e /usr/bin/env … git</c>.
/// Windows environment variables do not cross into the distribution unless they
/// are named on the command.
/// </summary>
public static class WslLaunch
{
    public const string EnvCommand = "/usr/bin/env";

    public static GitRequest Prepare(GitRequest request)
    {
        if (request.Wsl is null)
            return request;

        var distribution = request.Wsl.Distribution;
        var arguments = new List<string> { "-d", distribution };
        var directory = LinuxDirectory(request.WorkingDirectory, distribution);
        if (directory is not null)
        {
            arguments.Add("--cd");
            arguments.Add(directory);
        }

        arguments.Add("-e");
        arguments.Add(EnvCommand);
        arguments.Add("GIT_TERMINAL_PROMPT=0");
        if (request.Environment is not null)
        {
            foreach (var pair in request.Environment)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Key.Equals("PATH", StringComparison.OrdinalIgnoreCase))
                    continue;
                arguments.Add(pair.Key + "=" + WslPath.TranslateText(distribution, pair.Value));
            }
        }

        arguments.Add(request.Wsl.GitExecutable);
        arguments.AddRange(TranslateArguments(request.Arguments, distribution));
        return new GitRequest
        {
            Executable = request.Wsl.Launcher,
            Arguments = arguments,
            Progress = request.Progress,
            StandardInput = request.StandardInput,
        };
    }

    /// <summary>
    /// wsl.exe writes its own failures to stdout, in UTF-16 unless <c>WSL_UTF8=1</c>.
    /// A Linux git failure stays on stderr, so this only claims messages wsl.exe itself prints.
    /// </summary>
    public static string? LauncherNote(byte[] stdout)
    {
        if (stdout.Length == 0)
            return null;
        var text = WslList.Decode(stdout).Trim();
        if (text.Length == 0)
            return null;
        if (text.Contains("Error code: Wsl/", StringComparison.Ordinal)
            || text.StartsWith("There is no distribution", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("The Windows Subsystem for Linux", StringComparison.OrdinalIgnoreCase))
            return text;
        return null;
    }

    public static IReadOnlyList<string> TranslateArguments(IReadOnlyList<string> arguments, string distribution)
    {
        var translated = new string[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
            translated[i] = WslPath.TranslateArgument(distribution, arguments[i]);
        return translated;
    }

    private static string? LinuxDirectory(string? workingDirectory, string distribution)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return null;
        if (!WslPath.IsWindowsAbsolute(workingDirectory) && !workingDirectory.StartsWith('/'))
            return null;
        var linux = WslPath.ToLinux(distribution, workingDirectory);
        return linux.StartsWith('/') ? linux : null;
    }
}
