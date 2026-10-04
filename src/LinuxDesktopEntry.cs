using System.Text;

namespace Sextant;

/// <summary>
/// Installs the shipped <c>sextant.desktop</c> into the user's local applications directory on Linux.
/// An entry that is already there is left as it is.
/// </summary>
public static class LinuxDesktopEntry
{
    public const string FileName = "sextant.desktop";

    public const string IconFileName = "sextant.png";

    public static void EnsureInstalled()
    {
        if (!OperatingSystem.IsLinux())
            return;
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
                return;
            var sourceDirectory = Path.GetDirectoryName(executable);
            if (string.IsNullOrEmpty(sourceDirectory))
                return;
            TryInstall(
                sourceDirectory,
                executable,
                ApplicationsDirectory(
                    Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }

    public static string ApplicationsDirectory(string? dataHome, string profile)
    {
        var root = string.IsNullOrWhiteSpace(dataHome)
            ? Path.Combine(profile, ".local", "share")
            : dataHome;
        return Path.Combine(root, "applications");
    }

    /// <summary>
    /// Quotes one <c>Exec</c> argument. A percent sign is doubled so it is not a field code.
    /// </summary>
    public static string QuoteExec(string path)
    {
        var builder = new StringBuilder(path.Length + 2);
        builder.Append('"');
        foreach (var character in path)
        {
            if (character is '\\' or '"' or '`' or '$')
                builder.Append('\\');
            if (character == '%')
                builder.Append('%');
            builder.Append(character);
        }

        builder.Append('"');
        return builder.ToString();
    }

    public static string WithLaunchPath(string template, string executable, string? iconPath)
    {
        var full = Path.GetFullPath(executable);
        var icon = string.IsNullOrEmpty(iconPath) ? null : Path.GetFullPath(iconPath);
        var newline = template.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = template.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder(template.Length + full.Length);
        var wroteIcon = false;
        var wroteTry = false;
        foreach (var line in lines)
        {
            if (TryKey(line, "Exec", out _))
            {
                builder.Append("Exec=").Append(QuoteExec(full)).Append(newline);
                continue;
            }

            if (TryKey(line, "TryExec", out _))
            {
                builder.Append("TryExec=").Append(full).Append(newline);
                wroteTry = true;
                continue;
            }

            if (TryKey(line, "Icon", out var iconValue))
            {
                if (icon is not null)
                    builder.Append("Icon=").Append(icon).Append(newline);
                else if (Path.IsPathRooted(iconValue))
                    builder.Append(line).Append(newline);
                wroteIcon = icon is not null || Path.IsPathRooted(iconValue);
                continue;
            }

            builder.Append(line).Append(newline);
        }

        if (icon is not null && !wroteIcon)
            builder.Append("Icon=").Append(icon).Append(newline);
        if (!wroteTry)
            builder.Append("TryExec=").Append(full).Append(newline);
        return builder.ToString();
    }

    /// <summary>
    /// Copies the desktop file from beside the executable. Returns false when the destination already exists or the source is missing.
    /// </summary>
    public static bool TryInstall(string sourceDirectory, string executable, string applicationsDirectory)
    {
        if (executable.Contains('\n') || executable.Contains('\r'))
            return false;
        var destination = Path.Combine(applicationsDirectory, FileName);
        if (File.Exists(destination))
            return false;
        var source = Path.Combine(sourceDirectory, FileName);
        if (!File.Exists(source))
            return false;
        var icon = Path.Combine(sourceDirectory, IconFileName);
        var text = WithLaunchPath(File.ReadAllText(source), executable, File.Exists(icon) ? icon : null);
        Directory.CreateDirectory(applicationsDirectory);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text);
            File.Move(temporary, destination, overwrite: false);
        }
        catch (IOException)
        {
            TryDelete(temporary);
            return File.Exists(destination);
        }

        return true;
    }

    private static bool TryKey(string line, string key, out string value)
    {
        value = "";
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] == '#')
            return false;
        if (!trimmed.StartsWith(key, StringComparison.Ordinal))
            return false;
        var after = trimmed[key.Length..].TrimStart();
        if (after.Length == 0 || after[0] != '=')
            return false;
        value = after[1..].Trim();
        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
