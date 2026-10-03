using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Sextant.Git;

public sealed class GitProcessRunner
{
    public async Task<GitOutput> RunAsync(GitRequest request, CancellationToken cancellationToken)
    {
        var start = Stopwatch.StartNew();
        var info = new ProcessStartInfo
        {
            FileName = request.Executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        if (!string.IsNullOrEmpty(request.WorkingDirectory))
            info.WorkingDirectory = request.WorkingDirectory;
        foreach (var argument in request.Arguments)
            info.ArgumentList.Add(argument);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (request.Environment is not null)
        {
            foreach (var pair in request.Environment)
                info.Environment[pair.Key] = pair.Value;
        }

        // A macOS app launched from Finder does not inherit the shell PATH, so Homebrew's
        // git-lfs is invisible. git then fails the smudge with "git-lfs: command not found".
        info.Environment.TryGetValue("PATH", out var path);
        info.Environment["PATH"] = ToolPath(path, request.Executable);

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException("Failed to start git.");

        using var registration = cancellationToken.Register(() => TryKill(process));
        var stdoutTask = ReadAllAsync(process.StandardOutput.BaseStream);
        var stderrTask = ReadStandardErrorAsync(process.StandardError.BaseStream, request.Progress);
        if (request.StandardInput is { Length: > 0 })
        {
            await process.StandardInput.BaseStream.WriteAsync(request.StandardInput, cancellationToken).ConfigureAwait(false);
            await process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }

            await DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        start.Stop();

        var display = new List<string>(request.Arguments.Count + 1) { "git" };
        display.AddRange(ArgumentRedactor.RedactAll(request.Arguments));
        return new GitOutput
        {
            ExitCode = process.ExitCode,
            Stdout = stdout,
            StandardError = stderr.Error,
            Progress = stderr.Progress,
            Duration = start.Elapsed,
            DisplayArguments = display,
        };
    }

    /// <summary>
    /// Puts the git executable's directory first, then Homebrew and /usr/local/bin when they exist.
    /// git-lfs and gh are installed beside Homebrew git, and a GUI launch does not see that directory.
    /// </summary>
    internal static string ToolPath(string? path, string executable)
    {
        var directories = new List<string>();
        var gitDirectory = Path.GetDirectoryName(executable);
        if (!string.IsNullOrEmpty(gitDirectory) && Path.IsPathRooted(executable))
            directories.Add(gitDirectory);
        if (!OperatingSystem.IsWindows())
        {
            if (Directory.Exists("/opt/homebrew/bin"))
                directories.Add("/opt/homebrew/bin");
            if (Directory.Exists("/usr/local/bin"))
                directories.Add("/usr/local/bin");
        }

        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var merged = new List<string>();
        void Add(string directory)
        {
            directory = ToDirectorySeparator(directory);
            if (directory.Length == 0 || !seen.Add(directory))
                return;
            merged.Add(directory);
        }

        foreach (var directory in directories)
            Add(directory);
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                Add(entry);
        }

        return string.Join(Path.PathSeparator, merged);
    }

    /// <summary>
    /// PATH entries are compared as text. A rooted directory is stored with
    /// <see cref="Path.DirectorySeparatorChar"/> so <c>/</c> and <c>\</c> name the same entry.
    /// </summary>
    internal static string ToDirectorySeparator(string directory)
    {
        var separator = Path.DirectorySeparatorChar;
        var alternate = Path.AltDirectorySeparatorChar;
        if (separator == alternate || directory.IndexOf(alternate) < 0)
            return directory;
        return directory.Replace(alternate, separator);
    }

    private static void TryKill(Process process)
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
    }

    private static async Task DrainAsync(Task<byte[]> stdout, Task<StderrRead> stderr)
    {
        try
        {
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory).ConfigureAwait(false);
        return memory.ToArray();
    }

    private static async Task<StderrRead> ReadStandardErrorAsync(Stream stream, IProgress<string>? progress)
    {
        var buffer = new byte[2048];
        var pending = new StringBuilder();
        var errors = new StringBuilder();
        string? lastProgress = null;
        var decoder = Encoding.UTF8.GetDecoder();
        var chars = new char[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            var count = decoder.GetChars(buffer, 0, read, chars, 0);
            pending.Append(chars, 0, count);
            Drain(pending, errors, progress, ref lastProgress, flush: false);
        }

        Drain(pending, errors, progress, ref lastProgress, flush: true);
        return new StderrRead(errors.ToString().TrimEnd(), lastProgress);
    }

    private static void Drain(
        StringBuilder pending,
        StringBuilder errors,
        IProgress<string>? progress,
        ref string? lastProgress,
        bool flush)
    {
        while (true)
        {
            var text = pending.ToString();
            var split = text.IndexOfAny(['\r', '\n']);
            if (split < 0)
                break;

            var line = text[..split];
            var consume = split + 1;
            if (split + 1 < text.Length && text[split] == '\r' && text[split + 1] == '\n')
                consume++;
            pending.Remove(0, consume);
            Classify(line, errors, progress, ref lastProgress);
        }

        if (flush && pending.Length > 0)
        {
            Classify(pending.ToString(), errors, progress, ref lastProgress);
            pending.Clear();
        }
    }

    private static void Classify(string line, StringBuilder errors, IProgress<string>? progress, ref string? lastProgress)
    {
        if (line.Length == 0)
            return;
        if (IsProgress(line))
        {
            lastProgress = line.Trim();
            progress?.Report(lastProgress);
            return;
        }

        errors.AppendLine(line);
    }

    private static bool IsProgress(string line)
    {
        var text = line.Trim();
        if (text.Length == 0)
            return false;
        if (text.Contains("error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("fatal", StringComparison.OrdinalIgnoreCase))
            return false;
        if (text.Contains('%', StringComparison.Ordinal))
            return true;

        return text.StartsWith("Enumerating", StringComparison.Ordinal)
            || text.StartsWith("Counting", StringComparison.Ordinal)
            || text.StartsWith("Compressing", StringComparison.Ordinal)
            || text.StartsWith("Receiving", StringComparison.Ordinal)
            || text.StartsWith("Resolving", StringComparison.Ordinal)
            || text.StartsWith("Unpacking", StringComparison.Ordinal)
            || text.StartsWith("Writing", StringComparison.Ordinal)
            || text.StartsWith("remote: Counting", StringComparison.Ordinal)
            || text.StartsWith("remote: Enumerating", StringComparison.Ordinal)
            || text.StartsWith("remote: Compressing", StringComparison.Ordinal)
            || text.StartsWith("remote: Total", StringComparison.Ordinal);
    }

    private readonly record struct StderrRead(string Error, string? Progress);
}
