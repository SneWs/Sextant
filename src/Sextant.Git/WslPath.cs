using System.Text.RegularExpressions;

namespace Sextant.Git;

/// <summary>
/// Maps a WSL distribution's files between the Linux path git sees and the
/// Windows path the rest of the app can read. <c>\\wsl$\</c> and
/// <c>\\wsl.localhost\</c> are the same distribution.
/// </summary>
public static partial class WslPath
{
    public static bool IsLinuxAbsolute(string path) =>
        path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal);

    public static bool IsWindowsAbsolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            return true;
        return path.Length >= 3
            && char.IsAsciiLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/');
    }

    public static bool TryParseUnc(string path, out string distribution, out string linuxPath)
    {
        distribution = "";
        linuxPath = "";
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var normalized = path.Trim().Replace('/', '\\');
        string? rest = null;
        if (normalized.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase))
            rest = normalized[@"\\wsl$\".Length..];
        else if (normalized.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase))
            rest = normalized[@"\\wsl.localhost\".Length..];
        if (string.IsNullOrEmpty(rest))
            return false;

        var slash = rest.IndexOf('\\');
        distribution = slash < 0 ? rest : rest[..slash];
        if (distribution.Length == 0 || distribution.Contains(':'))
            return false;
        if (slash < 0 || slash == rest.Length - 1)
        {
            linuxPath = "/";
            return true;
        }

        linuxPath = NormalizeLinux("/" + rest[(slash + 1)..].Replace('\\', '/'));
        return true;
    }

    public static string ToLinux(string distribution, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        var trimmed = path.Trim().Trim('"');
        if (IsLinuxAbsolute(trimmed))
            return NormalizeLinux(trimmed);
        if (TryParseUnc(trimmed, out _, out var linux))
            return linux;
        if (TryDrive(trimmed, out var mounted))
            return mounted;
        return trimmed.Replace('\\', '/');
    }

    public static string ToWindows(string distribution, string linuxPath)
    {
        var linux = NormalizeLinux(linuxPath);
        if (TryUnmount(linux, out var drive))
            return drive;
        var relative = linux == "/" ? "" : linux.TrimStart('/');
        var root = @"\\wsl.localhost\" + distribution;
        return relative.Length == 0 ? root : root + @"\" + relative.Replace('/', '\\');
    }

    /// <summary>One Windows spelling for a WSL folder, so saved tabs match a later open.</summary>
    public static string CanonicalWindows(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        return TryParseUnc(path, out var distribution, out var linux)
            ? ToWindows(distribution, linux)
            : path;
    }

    public static string TranslateArgument(string distribution, string argument)
    {
        if (string.IsNullOrEmpty(argument))
            return argument;
        var equals = argument.IndexOf('=');
        if (equals > 0 && argument[0] == '-')
        {
            var value = argument[(equals + 1)..];
            if (!IsWindowsAbsolute(value))
                return argument;
            return argument[..(equals + 1)] + ToLinux(distribution, value);
        }

        return IsWindowsAbsolute(argument) ? ToLinux(distribution, argument) : argument;
    }

    public static string TranslateText(string distribution, string text)
    {
        if (string.IsNullOrEmpty(text) || !OperatingSystem.IsWindows() && text.IndexOf(':') < 0 && text.IndexOf('\\') < 0)
            return text;
        if (!text.Contains('\\') && !text.Contains(":/", StringComparison.Ordinal) && !LooksLikeDrive(text))
            return text;
        return WindowsPathInText().Replace(text, match =>
        {
            var value = match.Value;
            var quoted = value.Length >= 2 && value[0] == '"' && value[^1] == '"';
            var linux = ToLinux(distribution, quoted ? value[1..^1] : value);
            return quoted ? "\"" + linux + "\"" : linux;
        });
    }

    private static bool LooksLikeDrive(string text)
    {
        for (var i = 0; i < text.Length - 2; i++)
        {
            if (char.IsAsciiLetter(text[i]) && text[i + 1] == ':' && (text[i + 2] == '\\' || text[i + 2] == '/'))
                return true;
        }

        return false;
    }

    private static string NormalizeLinux(string path)
    {
        var slash = path.Replace('\\', '/');
        if (slash.Length > 1)
            slash = slash.TrimEnd('/');
        return slash.Length == 0 ? "/" : slash;
    }

    private static bool TryDrive(string path, out string linux)
    {
        linux = "";
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
            return false;
        var rest = path[3..].Replace('\\', '/').TrimEnd('/');
        linux = "/mnt/" + char.ToLowerInvariant(path[0]) + (rest.Length == 0 ? "" : "/" + rest);
        return true;
    }

    private static bool TryUnmount(string linux, out string windows)
    {
        windows = "";
        if (!linux.StartsWith("/mnt/", StringComparison.Ordinal) || linux.Length < 6 || !char.IsAsciiLetter(linux[5]))
            return false;
        if (linux.Length > 6 && linux[6] != '/')
            return false;
        var rest = linux.Length <= 7 ? "" : linux[7..];
        windows = char.ToUpperInvariant(linux[5]) + @":\" + rest.Replace('/', '\\');
        return true;
    }

    [GeneratedRegex(
        "\"[A-Za-z]:[\\\\/][^\"]*\"|\\\\\\\\wsl(?:\\.localhost|\\$)\\\\[^\\\\]+\\\\[^\\s\"]*|[A-Za-z]:[\\\\/][^\\s\"']*",
        RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathInText();
}
