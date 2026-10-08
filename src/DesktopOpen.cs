using System.Diagnostics;
using Sextant.Git.Repo;

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

    /// <summary>
    /// Last segment of a repository path. Git paths use either separator, and
    /// <see cref="Path.GetFileName"/> only treats <c>\</c> as a separator on Windows.
    /// </summary>
    public static string? FileName(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            return null;
        
        var trimmed = relative.TrimEnd('/', '\\');
        if (trimmed.Length == 0)
            return null;
        
        var index = trimmed.LastIndexOfAny(['/', '\\']);
        var name = index < 0 ? trimmed : trimmed[(index + 1)..];
        
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
            _ => LinuxOpen(directory),
        };
    }

    public static ShellLaunch EditFile(DesktopKind kind, string fullPath)
    {
        Reject(fullPath);
        return kind switch
        {
            DesktopKind.Windows => new ShellLaunch(fullPath, null, [], true),
            DesktopKind.Mac => new ShellLaunch("open", null, ["--", fullPath], false),
            _ => LinuxOpen(fullPath),
        };
    }

    /// <summary>
    /// <c>xdg-open</c> rejects a <c>--</c> end-of-options marker and exits without opening the path.
    /// The path is absolute, so it is not read as an option.
    /// </summary>
    private static ShellLaunch LinuxOpen(string path) =>
        new("xdg-open", null, [path], false);

    /// <summary>Opens an http or https link in the registered browser. Other schemes are refused.</summary>
    public static ShellLaunch OpenUrl(DesktopKind kind, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || url.Contains('"')
            || url.Contains('\0')
            || url.Contains('\r')
            || url.Contains('\n'))
            throw new InvalidOperationException("That link cannot be opened.");

        return kind switch
        {
            DesktopKind.Windows => new ShellLaunch(url, null, [], true),
            DesktopKind.Mac => new ShellLaunch("open", null, ["--", url], false),
            _ => new ShellLaunch("xdg-open", null, [url], false),
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
        {
            info.Arguments = launch.Arguments;
        }
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