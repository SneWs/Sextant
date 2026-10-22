namespace Sextant.Git;

/// <summary>
/// Watches the git directory and, when it is a separate folder, the working tree.
/// A window that is visible on another screen can follow editor saves and external
/// git updates without being activated. A recursive work-tree watch can fail the
/// Linux inotify limit; that watch is dropped and the git-directory watch stays.
/// </summary>
public sealed class GitDirectoryWatcher : IDisposable
{
    private const int DebounceMilliseconds = 200;
    private const NotifyFilters Filters =
        NotifyFilters.LastWrite
        | NotifyFilters.FileName
        | NotifyFilters.Size
        | NotifyFilters.CreationTime
        | NotifyFilters.DirectoryName;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly List<string> _gitRoots = [];
    private readonly object _disposeGate = new();
    private readonly Timer _timer;
    private readonly Action<bool> _callback;
    private int _disposed;
    private int _refsChanged;

    public GitDirectoryWatcher(string gitDirectory, Action<bool> callback, string? workTree)
    {
        _callback = callback;
        _timer = new Timer(Fire, null, Timeout.Infinite, Timeout.Infinite);
        try
        {
            var gitRoot = Normalize(gitDirectory);
            _gitRoots.Add(gitRoot);
            var primary = CreateGitWatcher(gitDirectory, required: true);

            var common = ResolveCommonDirectory(gitDirectory);
            FileSystemWatcher? commonWatcher = null;
            if (common is not null)
            {
                var commonRoot = Normalize(common);
                if (!SamePath(commonRoot, gitRoot))
                {
                    _gitRoots.Add(commonRoot);
                    commonWatcher = CreateGitWatcher(common, required: false);
                }
            }

            FileSystemWatcher? treeWatcher = null;
            if (!string.IsNullOrEmpty(workTree) && Directory.Exists(workTree))
            {
                var tree = Normalize(workTree);
                var insideGit = false;
                foreach (var root in _gitRoots)
                {
                    if (SamePath(tree, root) || IsUnder(tree, root))
                    {
                        insideGit = true;
                        break;
                    }
                }

                if (!insideGit)
                    treeWatcher = CreateWorktreeWatcher(workTree);
            }

            // Handlers are attached before events are enabled, and every git root is
            // recorded first so a work-tree event can ignore those directories.
            if (primary is not null)
                primary.EnableRaisingEvents = true;
            if (commonWatcher is not null)
                commonWatcher.EnableRaisingEvents = true;
            if (treeWatcher is not null)
                treeWatcher.EnableRaisingEvents = true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public event Action? Failed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        lock (_disposeGate)
        {
            foreach (var watcher in _watchers)
                Silence(watcher);
            _timer.Dispose();
        }
    }

    private FileSystemWatcher? CreateGitWatcher(string directory, bool required)
    {
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = Filters,
            };
            FileSystemEventHandler onEvent = (_, args) => QueueGit(args.FullPath);
            watcher.Changed += onEvent;
            watcher.Created += onEvent;
            watcher.Deleted += onEvent;
            watcher.Renamed += (_, args) =>
            {
                QueueGit(args.FullPath);
                QueueGit(args.OldFullPath);
            };
            watcher.Error += (_, _) => OnWatcherError(watcher, required);
            _watchers.Add(watcher);
            return watcher;
        }
        catch
        {
            if (watcher is not null)
                Drop(watcher);
            if (required)
                throw;
            return null;
        }
    }

    private FileSystemWatcher? CreateWorktreeWatcher(string directory)
    {
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = Filters,
            };
            FileSystemEventHandler onEvent = (_, args) => QueueWorktree(args.FullPath);
            watcher.Changed += onEvent;
            watcher.Created += onEvent;
            watcher.Deleted += onEvent;
            watcher.Renamed += (_, args) =>
            {
                QueueWorktree(args.FullPath);
                QueueWorktree(args.OldFullPath);
            };
            watcher.Error += (_, _) => OnWatcherError(watcher, required: false);
            _watchers.Add(watcher);
            return watcher;
        }
        catch
        {
            if (watcher is not null)
                Drop(watcher);
            return null;
        }
    }

    private void OnWatcherError(FileSystemWatcher watcher, bool required)
    {
        // Disabling a watcher during Dispose can raise Error on this thread.
        if (Volatile.Read(ref _disposed) == 1)
            return;
        if (required)
            Failed?.Invoke();
        else
            Drop(watcher);
    }

    private void Drop(FileSystemWatcher watcher)
    {
        lock (_disposeGate)
            Silence(watcher);
    }

    private static void Silence(FileSystemWatcher watcher)
    {
        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private void QueueGit(string? path)
    {
        if (!IsRelevant(path))
            return;
        Arm(IsRef(path));
    }

    private void QueueWorktree(string? path)
    {
        // Object writes under a watched git directory must not keep resetting the
        // shared debounce, or a ref change waits until the fetch goes quiet.
        if (string.IsNullOrEmpty(path) || Volatile.Read(ref _disposed) == 1)
            return;
        string full;
        try
        {
            full = Normalize(path);
        }
        catch (Exception)
        {
            return;
        }

        foreach (var root in _gitRoots)
        {
            if (IsUnder(full, root))
                return;
        }

        Arm(refsChanged: false);
    }

    private void Arm(bool refsChanged)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;
        if (refsChanged)
            Interlocked.Exchange(ref _refsChanged, 1);
        try
        {
            _timer.Change(DebounceMilliseconds, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Fire(object? _)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;
        var refsChanged = Interlocked.Exchange(ref _refsChanged, 0) == 1;
        try
        {
            _callback(refsChanged);
        }
        catch (Exception)
        {
        }
    }

    private static bool IsRelevant(string? path) =>
        IsRef(path) || (!string.IsNullOrEmpty(path) && Path.GetFileName(path).Equals("index", StringComparison.OrdinalIgnoreCase));

    private static bool IsRef(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        var name = Path.GetFileName(path);
        if (name.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
            || name.Equals("packed-refs", StringComparison.OrdinalIgnoreCase)
            || name.Equals("MERGE_HEAD", StringComparison.OrdinalIgnoreCase))
            return true;
        return path.Replace('\\', '/').Contains("/refs/", StringComparison.Ordinal);
    }

    private static string? ResolveCommonDirectory(string gitDirectory)
    {
        var file = Path.Combine(gitDirectory, "commondir");
        if (!File.Exists(file))
            return null;
        try
        {
            var text = File.ReadAllText(file).Trim();
            if (text.Length == 0)
                return null;
            var combined = Path.IsPathRooted(text)
                ? text
                : Path.Combine(gitDirectory, text);
            var full = Normalize(combined);
            return Directory.Exists(full) ? full : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool SamePath(string left, string right) =>
        left.Equals(right, PathComparison);

    private static bool IsUnder(string path, string root)
    {
        if (path.Equals(root, PathComparison))
            return true;
        var prefix = root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, PathComparison);
    }
}
