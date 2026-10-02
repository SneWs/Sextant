using System.Diagnostics;
using Sextant.Git;

namespace Sextant;

/// <summary>
/// Opens a worktree file with the desktop's file manager or the application registered for its type.
/// </summary>
public static class DesktopOpen
{
    public static DesktopKind Current => OperatingSystem.IsWindows()
        ? DesktopKind.Windows
        : OperatingSystem.IsMacOS() ? DesktopKind.Mac : DesktopKind.Linux;

    public static string FolderLabel(DesktopKind kind) => kind switch
    {
        DesktopKind.Windows => "Open in File Explorer",
        DesktopKind.Mac => "Open in Finder",
        _ => "Open in File Manager",
    };

    public static string? FileName(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            return null;
        var name = Path.GetFileName(relative.TrimEnd('/', '\\'));
        return string.IsNullOrEmpty(name) ? null : name;
    }

    public static string? FullPath(string toplevel, string relative) => RepoPath.CombineUnder(toplevel, relative);

    /// <summary>The nearest existing directory at or above <paramref name="fullPath"/>.</summary>
    public static string? NearestDirectory(string fullPath)
    {
        var dir = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(dir))
                return dir;
            var parent = Path.GetDirectoryName(dir);
            if (string.Equals(parent, dir, StringComparison.Ordinal))
                return null;
            dir = parent;
        }

        return null;
    }

    public static ShellLaunch RevealFile(DesktopKind kind, string fullPath)
    {
        Reject(fullPath);
        return kind switch
        {
            DesktopKind.Windows => new ShellLaunch("explorer.exe", "/select,\"" + fullPath + "\"", [], false),
            DesktopKind.Mac => new ShellLaunch("open", null, ["-R", "--", fullPath], false),
            _ => OpenDirectory(kind, Parent(fullPath)),
        };
    }

    public static ShellLaunch OpenDirectory(DesktopKind kind, string directory)
    {
        Reject(directory);
        return kind switch
        {
            DesktopKind.Windows => new ShellLaunch("explorer.exe", null, [directory], false),
            DesktopKind.Mac => new ShellLaunch("open", null, ["--", directory], false),
            _ => new ShellLaunch("xdg-open", null, ["--", directory], false),
        };
    }

    public static ShellLaunch EditFile(DesktopKind kind, string fullPath)
    {
        Reject(fullPath);
        return kind switch
        {
            DesktopKind.Windows => new ShellLaunch(fullPath, null, [], true),
            DesktopKind.Mac => new ShellLaunch("open", null, ["--", fullPath], false),
            _ => new ShellLaunch("xdg-open", null, ["--", fullPath], false),
        };
    }

    public static void Start(ShellLaunch launch)
    {
        var info = new ProcessStartInfo
        {
            FileName = launch.FileName,
            UseShellExecute = launch.UseShellExecute,
        };
        if (launch.Arguments is not null)
            info.Arguments = launch.Arguments;
        else
        {
            foreach (var argument in launch.ArgumentList)
                info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info);
    }

    private static string Parent(string fullPath)
    {
        var trimmed = fullPath.TrimEnd('/', '\\');
        var index = trimmed.LastIndexOfAny(['/', '\\']);
        if (index < 0)
            return fullPath;
        if (index == 0)
            return trimmed[..1];
        return trimmed[..index];
    }

    private static void Reject(string path)
    {
        if (path.Contains('"') || path.Contains('\0'))
            throw new InvalidOperationException("That path cannot be opened.");
    }
}

public enum DesktopKind
{
    Windows,
    Mac,
    Linux,
}

public readonly record struct ShellLaunch(string FileName, string? Arguments, IReadOnlyList<string> ArgumentList, bool UseShellExecute);
