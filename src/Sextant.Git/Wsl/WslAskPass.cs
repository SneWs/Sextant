using System.Diagnostics;
using Sextant.Git.AskPass;

namespace Sextant.Git.Wsl;

/// <summary>
/// WSL ssh cannot prompt, and a Windows executable started from the distribution
/// does not see Linux environment variables. The askpass program is a script in
/// the distribution that runs the Windows helper with the pipe name on its command.
/// </summary>
public static class WslAskPass
{
    public static string Script(string linuxExecutable, string pipeName)
    {
        return "#!/bin/sh\n"
            + "kind=${SSH_ASKPASS_PROMPT:-password}\n"
            + "repo=${" + AskPassEnvironment.RepositoryVariable + ":-}\n"
            + "exec " + ShQuote(linuxExecutable) + " --pipe " + ShQuote(pipeName) + " --kind \"$kind\" --repo \"$repo\" \"$@\"\n";
    }

    public static async Task<string?> EnsureAsync(WslGit wsl, string windowsExecutable, string pipeName, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !wsl.CanRun || string.IsNullOrWhiteSpace(windowsExecutable))
            return null;
        var linuxExecutable = WslPath.ToLinux(wsl.Distribution, windowsExecutable);
        var script = "/tmp/sextant-askpass-" + pipeName;
        var body = Script(linuxExecutable, pipeName);
        if (!await WriteAsync(wsl, script, body, cancellationToken).ConfigureAwait(false))
            return null;
        return script;
    }

    public static string ShQuote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static async Task<bool> WriteAsync(WslGit wsl, string script, string body, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = wsl.Launcher,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.Environment["WSL_UTF8"] = "1";
        info.ArgumentList.Add("-d");
        info.ArgumentList.Add(wsl.Distribution);
        info.ArgumentList.Add("-e");
        info.ArgumentList.Add("/bin/sh");
        info.ArgumentList.Add("-c");
        var quoted = ShQuote(script);
        // /tmp can be cleared while Sextant is still running. Keep valid helpers intact.
        info.ArgumentList.Add("if test -f " + quoted + " && test -x " + quoted
            + "; then cat > /dev/null; else cat > " + quoted + " && chmod 700 " + quoted + "; fi");
        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start())
                return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }

        await process.StandardInput.WriteAsync(body.AsMemory(), cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0;
    }
}
