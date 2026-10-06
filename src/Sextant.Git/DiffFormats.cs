using System.Diagnostics;
using System.Globalization;
using System.Text;
using Sextant.Git.Parsing;

namespace Sextant.Git;

/// <summary>
/// One file extension converted before a diff, and restored when a merge of that type is saved.
/// The command is not a shell pipeline. It reads the file on stdin and writes the result to stdout.
/// <c>$FILE</c> is a temporary copy when the tool needs a path.
/// </summary>
public sealed class DiffFormatRule
{
    public string Extension { get; set; } = "";

    public string Transform { get; set; } = "";

    public string Restore { get; set; } = "";
}

public static class DiffFormatRules
{
    /// <summary>An all-files diff formats this many matching files. The rest stay on git's diff until opened alone.</summary>
    public const int MaxFiles = 64;

    public static List<DiffFormatRule> Normalize(IEnumerable<DiffFormatRule>? rules)
    {
        var list = new List<DiffFormatRule>();
        if (rules is null)
            return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (rule is null)
                continue;
            var extension = NormalizeExtension(rule.Extension);
            var transform = NormalizeCommand(rule.Transform);
            var restore = NormalizeCommand(rule.Restore);
            if (extension is null || (transform is null && restore is null) || !seen.Add(extension))
                continue;
            list.Add(new DiffFormatRule
            {
                Extension = extension,
                Transform = transform ?? "",
                Restore = restore ?? "",
            });
        }

        return list;
    }

    public static bool Same(IEnumerable<DiffFormatRule>? left, IEnumerable<DiffFormatRule>? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Extension, b[i].Extension, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a[i].Transform, b[i].Transform, StringComparison.Ordinal)
                || !string.Equals(a[i].Restore, b[i].Restore, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    public static DiffFormatRule? Match(IReadOnlyList<DiffFormatRule> rules, string? path)
    {
        var extension = ExtensionOf(path);
        if (extension.Length == 0)
            return null;
        foreach (var rule in rules)
        {
            if (string.Equals(rule.Extension, extension, StringComparison.OrdinalIgnoreCase))
                return rule;
        }

        return null;
    }

    public static string ExtensionOf(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return "";
        var name = path.Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
            name = name[(slash + 1)..];
        var extension = Path.GetExtension(name);
        return extension.Length < 2 ? "" : extension;
    }

    /// <summary>Empty, a path, or more than one dot is not an extension. <c>json</c> is stored as <c>.json</c>.</summary>
    public static string? NormalizeExtension(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var value = text.Trim();
        if (value is "." or ".." || value.IndexOfAny(['/', '\\', ' ', '*', '?', '"', '<', '>', '|', ':']) >= 0)
            return null;
        if (!value.StartsWith('.'))
            value = "." + value;
        if (value.Length < 2 || value.IndexOf('.', 1) >= 0)
            return null;
        return value.ToLowerInvariant();
    }

    public static string? NormalizeCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.IndexOfAny(['\r', '\n']) >= 0)
            return null;
        return command.Trim();
    }

    public static string? TryCollect(
        IReadOnlyList<(string Extension, string Transform, string Restore)> rows,
        out List<DiffFormatRule> rules)
    {
        rules = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var extensionText = row.Extension?.Trim() ?? "";
            var transformText = row.Transform ?? "";
            var restoreText = row.Restore ?? "";
            if (extensionText.Length == 0 && transformText.Trim().Length == 0 && restoreText.Trim().Length == 0)
                continue;
            var extension = NormalizeExtension(extensionText);
            if (extension is null)
                return "A file type needs an extension such as .json.";
            if (transformText.IndexOfAny(['\r', '\n']) >= 0)
                return "The transform command must be a single line.";
            if (restoreText.IndexOfAny(['\r', '\n']) >= 0)
                return "The restore command must be a single line.";
            var transform = NormalizeCommand(transformText);
            var restore = NormalizeCommand(restoreText);
            if (transform is null && restore is null)
                return "Add a transform or a restore command for " + extension + ".";
            if (transform is not null && !TrySplit(transform, out _, out var transformError))
                return "The transform command could not be read. " + transformError;
            if (restore is not null && !TrySplit(restore, out _, out var restoreError))
                return "The restore command could not be read. " + restoreError;
            if (!seen.Add(extension))
                return extension + " is listed more than once.";
            rules.Add(new DiffFormatRule
            {
                Extension = extension,
                Transform = transform ?? "",
                Restore = restore ?? "",
            });
        }

        return null;
    }

    public static bool TrySplit(string command, out IReadOnlyList<string> arguments, out string? error)
    {
        arguments = [];
        error = null;
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var quote = '\0';
        var any = false;
        for (var i = 0; i < command.Length; i++)
        {
            var character = command[i];
            if (quoted)
            {
                if (quote == '"' && character == '\\' && i + 1 < command.Length && command[i + 1] is '\\' or '"')
                {
                    current.Append(command[++i]);
                    continue;
                }

                if (character == quote)
                {
                    quoted = false;
                    continue;
                }

                current.Append(character);
                continue;
            }

            if (character is ' ' or '\t')
            {
                if (!any)
                    continue;
                tokens.Add(current.ToString());
                current.Clear();
                any = false;
                continue;
            }

            if (character is '"' or '\'')
            {
                quoted = true;
                quote = character;
                any = true;
                continue;
            }

            current.Append(character);
            any = true;
        }

        if (quoted)
        {
            error = "A quote was left open.";
            return false;
        }

        if (any)
            tokens.Add(current.ToString());
        if (tokens.Count == 0)
        {
            error = "The command is empty.";
            return false;
        }

        arguments = tokens;
        return true;
    }
}

public readonly record struct DiffFormatRun(bool Ok, byte[] Output, string Error, IReadOnlyList<string> Display, int ExitCode, TimeSpan Duration)
{
    public static DiffFormatRun Failed(string error, IReadOnlyList<string>? display = null) =>
        new(false, [], error, display ?? [], -1, TimeSpan.Zero);
}

public static class DiffFormatTool
{
    public const string FileToken = "$FILE";

    public static async Task<DiffFormatRun> RunAsync(
        string command,
        byte[] input,
        string? workingDirectory,
        string? gitExecutable,
        CancellationToken cancellationToken,
        string? fileExtension = null)
    {
        if (DiffFormatRules.NormalizeCommand(command) is null)
            return DiffFormatRun.Failed("The command must be a single line.");
        if (!DiffFormatRules.TrySplit(command, out var arguments, out var error))
            return DiffFormatRun.Failed(error ?? "The command could not be read.");

        var usesFile = arguments.Any(argument => argument.Contains(FileToken, StringComparison.Ordinal));
        var extension = DiffFormatRules.NormalizeExtension(fileExtension) ?? ".txt";
        var directory = Path.Combine(Path.GetTempPath(), "sextant-format-" + Guid.NewGuid().ToString("N"));
        string? file = null;
        try
        {
            if (usesFile)
            {
                Directory.CreateDirectory(directory);
                file = Path.Combine(directory, "input" + extension);
                await File.WriteAllBytesAsync(file, input, cancellationToken).ConfigureAwait(false);
            }

            var tokens = new List<string>(arguments.Count);
            foreach (var argument in arguments)
                tokens.Add(file is null ? argument : argument.Replace(FileToken, file, StringComparison.Ordinal));

            return await StartAsync(tokens, input, workingDirectory, gitExecutable, file, usesFile, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DiffFormatRun.Failed("The formatter could not write its temporary file. " + exception.Message);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static async Task<DiffFormatRun> StartAsync(
        List<string> tokens,
        byte[] input,
        string? workingDirectory,
        string? gitExecutable,
        string? file,
        bool usesFile,
        CancellationToken cancellationToken)
    {
        var display = tokens.ToArray();
        var info = new ProcessStartInfo
        {
            FileName = tokens[0],
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        if (!string.IsNullOrEmpty(workingDirectory))
            info.WorkingDirectory = workingDirectory;
        for (var i = 1; i < tokens.Count; i++)
            info.ArgumentList.Add(tokens[i]);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment.TryGetValue("PATH", out var path);
        info.Environment["PATH"] = GitProcessRunner.ToolPath(path, gitExecutable ?? "");

        Process process;
        try
        {
            process = new Process { StartInfo = info, EnableRaisingEvents = true };
            if (!process.Start())
                return DiffFormatRun.Failed("The formatter could not be started.", display);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return DiffFormatRun.Failed("The formatter could not be started. " + tokens[0] + " was not found.", display);
        }

        using (process)
        {
            var started = Stopwatch.StartNew();
            using var registration = cancellationToken.Register(() => TryKill(process));
            var stdout = ReadCappedAsync(process, process.StandardOutput.BaseStream, cancellationToken);
            var stderr = ReadTextAsync(process.StandardError.BaseStream);
            try
            {
                await WriteInputAsync(process, input, cancellationToken).ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            var output = await stdout.ConfigureAwait(false);
            var errorText = await stderr.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            started.Stop();
            if (output is null)
                return new DiffFormatRun(false, [], "The formatter wrote more than 8 MB.", display, process.ExitCode, started.Elapsed);
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(errorText)
                    ? "The formatter exited " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + "."
                    : errorText.Trim();
                return new DiffFormatRun(false, [], detail, display, process.ExitCode, started.Elapsed);
            }

            if (output.Length == 0 && usesFile && file is not null && File.Exists(file))
            {
                try
                {
                    var rewritten = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
                    if (rewritten.Length > 0)
                        output = rewritten;
                }
                catch (IOException)
                {
                }
            }

            return new DiffFormatRun(true, output, "", display, 0, started.Elapsed);
        }
    }

    private static async Task WriteInputAsync(Process process, byte[] input, CancellationToken cancellationToken)
    {
        try
        {
            // A $FILE tool often never reads stdin and exits first. That close is a broken pipe, not a failed run.
            if (input.Length > 0)
            {
                await process.StandardInput.BaseStream.WriteAsync(input, cancellationToken).ConfigureAwait(false);
                await process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static async Task<byte[]?> ReadCappedAsync(Process process, Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return memory.ToArray();
            if (memory.Length + read > HistoryLimits.MaxPreviewBytes)
            {
                TryKill(process);
                return null;
            }

            memory.Write(buffer, 0, read);
        }
    }

    private static async Task<string> ReadTextAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
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
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}

public readonly record struct DiffFormatSides(string BeforePath, string AfterPath, bool BeforeMissing, bool AfterMissing);

public static class DiffFormatPatch
{
    public static DiffFormatSides Sides(string slice, string fallback)
    {
        var before = fallback;
        var after = fallback;
        var beforeMissing = false;
        var afterMissing = false;
        var normalized = slice.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
                break;
            if (line.StartsWith("rename from ", StringComparison.Ordinal))
                before = line["rename from ".Length..].Trim();
            else if (line.StartsWith("rename to ", StringComparison.Ordinal))
                after = line["rename to ".Length..].Trim();
            else if (line.StartsWith("--- ", StringComparison.Ordinal))
                Apply(line[4..], beforeSide: true);
            else if (line.StartsWith("+++ ", StringComparison.Ordinal))
                Apply(line[4..], beforeSide: false);
        }

        if (before.Length == 0)
            before = fallback;
        if (after.Length == 0)
            after = fallback;
        return new DiffFormatSides(before, after, beforeMissing, afterMissing);

        void Apply(string token, bool beforeSide)
        {
            var path = StripPrefix(Unquote(token.Trim()));
            if (IsNull(path))
            {
                if (beforeSide)
                    beforeMissing = true;
                else
                    afterMissing = true;
                return;
            }

            if (beforeSide)
                before = path;
            else
                after = path;
        }
    }

    public static string Render(string path, string noIndexText, bool created, bool deleted)
    {
        var parsed = DiffParser.Parse(noIndexText);
        var gitPath = path.Replace('\\', '/');
        var quoted = Quote(gitPath);
        var builder = new StringBuilder();
        builder.Append("diff --git ").Append(quoted.PrefixA).Append(' ').Append(quoted.PrefixB).Append('\n');
        if (parsed.IsBinary)
        {
            builder.Append("Binary files ").Append(quoted.PrefixA).Append(" and ").Append(quoted.PrefixB).Append(" differ\n");
            return builder.ToString();
        }

        if (created)
        {
            builder.Append("new file mode 100644\n");
            builder.Append("--- /dev/null\n");
            builder.Append("+++ ").Append(quoted.PrefixB).Append('\n');
        }
        else if (deleted)
        {
            builder.Append("deleted file mode 100644\n");
            builder.Append("--- ").Append(quoted.PrefixA).Append('\n');
            builder.Append("+++ /dev/null\n");
        }
        else
        {
            builder.Append("--- ").Append(quoted.PrefixA).Append('\n');
            builder.Append("+++ ").Append(quoted.PrefixB).Append('\n');
        }

        foreach (var hunk in parsed.Hunks)
        {
            builder.Append(hunk.Header).Append('\n');
            foreach (var line in hunk.Lines)
            {
                if (line.Kind == DiffLineKind.Meta)
                    builder.Append(line.Text);
                else if (line.Kind == DiffLineKind.Added)
                    builder.Append('+').Append(line.Text);
                else if (line.Kind == DiffLineKind.Removed)
                    builder.Append('-').Append(line.Text);
                else
                    builder.Append(' ').Append(line.Text);
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static bool IsNull(string path) =>
        path.Equals("/dev/null", StringComparison.OrdinalIgnoreCase)
        || path.Equals("nul", StringComparison.OrdinalIgnoreCase)
        || path.Equals("dev/null", StringComparison.OrdinalIgnoreCase);

    private static string Unquote(string token)
    {
        if (token.Length < 2 || token[0] != '"')
            return token;
        var end = token.EndsWith('"') ? token.Length - 1 : token.Length;
        return token[1..end];
    }

    private static string StripPrefix(string path)
    {
        if (path.StartsWith("a/", StringComparison.Ordinal) || path.StartsWith("b/", StringComparison.Ordinal))
            return path[2..];
        return path;
    }

    private readonly record struct QuotedPath(string PrefixA, string PrefixB);

    private static QuotedPath Quote(string gitPath)
    {
        if (gitPath.IndexOfAny([' ', '"', '\\', '\t']) < 0)
            return new QuotedPath("a/" + gitPath, "b/" + gitPath);
        var escaped = gitPath.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return new QuotedPath("\"a/" + escaped + "\"", "\"b/" + escaped + "\"");
    }
}
