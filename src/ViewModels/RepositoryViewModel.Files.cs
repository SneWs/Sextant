using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;
using Sextant.Git.Models;
using Sextant.Git.Workspace;

namespace Sextant.ViewModels;

public partial class RepositoryViewModel
{
    private readonly List<RepositoryFileTreeItem> _repositoryFileRoots = [];
    private readonly HashSet<string> _expandedRepositoryDirectories = new(StringComparer.Ordinal);
    private IReadOnlyList<RepositoryFile> _repositoryFiles = [];
    private readonly Dictionary<string, LfsLock> _repositoryLocks = new(StringComparer.Ordinal);
    private bool _repositoryLocksKnown;
    private int _repositoryFilesGeneration;

    public ResetCollection<RepositoryFileTreeItem> RepositoryFileTree { get; } = new();

    [ObservableProperty]
    public partial RepositoryFileTreeItem? SelectedRepositoryFile { get; set; }

    [ObservableProperty]
    public partial bool RepositoryFilesTabOn { get; set; }

    public bool HistoryTabOn => !RepositoryFilesTabOn;
    public bool RepositoryTreeEmpty => RepositoryFileTree.Count == 0 && !IsBusy;
    public string RepositoryTreeEmptyText => RepositoryFilesChangesOnly
        ? "No staged, unstaged, or untracked files."
        : "No repository files.";

    [ObservableProperty]
    public partial bool RepositoryFilesChangesOnly { get; set; }

    partial void OnRepositoryFilesChangesOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(RepositoryTreeEmptyText));
        if (_repositoryFiles.Count == 0 || _session is null)
            return;
        var roots = ComposeRepositoryTree(
            _repositoryFiles, _expandedRepositoryDirectories, _repositoryLocks, _repositoryLocksKnown,
            CurrentStatusFilter());
        InstallRepositoryTree(_repositoryFiles, roots);
    }

    /// <summary>
    /// Re-filters the cached file tree after the status changed. Only runs while the
    /// changes-only toggle is on; the full list needs no re-composition.
    /// </summary>
    private void RefreshRepositoryFilesFilter()
    {
        if (!RepositoryFilesChangesOnly || _repositoryFiles.Count == 0 || _session is null)
            return;
        var roots = ComposeRepositoryTree(
            _repositoryFiles, _expandedRepositoryDirectories, _repositoryLocks, _repositoryLocksKnown,
            CurrentStatusFilter());
        InstallRepositoryTree(_repositoryFiles, roots);
    }

    private IReadOnlyDictionary<string, StatusEntry>? CurrentStatusFilter() =>
        RepositoryFilesChangesOnly && _session is not null ? StatusChanges() : null;

    private Dictionary<string, StatusEntry> StatusChanges()
    {
        var session = _session;
        if (session is null)
            return [];
        var entries = session.Snapshot().Entries;
        var map = new Dictionary<string, StatusEntry>(entries.Count, StringComparer.Ordinal);
        foreach (var entry in entries)
            map[entry.Path] = entry;
        return map;
    }

    private static string StatusTextFor(StatusEntry entry)
    {
        if (entry.Kind == ChangeKind.Unmerged)
            return "U";
        var text = "";
        if (entry.Staged)
            text += entry.IndexStatus;
        if (entry.Unstaged || entry.Kind == ChangeKind.Untracked)
            text = text.Length == 0 ? entry.WorkTreeStatus.ToString() : text + " " + entry.WorkTreeStatus;
        return text;
    }

    partial void OnRepositoryFilesTabOnChanged(bool value)
    {
        OnPropertyChanged(nameof(HistoryTabOn));
        OnPropertyChanged(nameof(ShowHistoryChrome));
    }

    [RelayCommand]
    private void ShowHistoryTab() => RepositoryFilesTabOn = false;

    [RelayCommand]
    private Task ShowRepositoryFilesTab()
    {
        if (RepositoryFilesTabOn)
            return Task.CompletedTask;
        RepositoryFilesTabOn = true;
        return RefreshRepositoryFiles();
    }

    [RelayCommand]
    private Task RefreshRepositoryFiles() =>
        RunAsync("Loading repository files…",
            ct => LoadRepositoryFilesAsync(ct, refreshLocks: true), refreshRepositoryFiles: false);

    private async Task LoadRepositoryFilesAsync(CancellationToken token, bool refreshLocks)
    {
        var session = _session;
        if (session is null)
            return;
        var generation = Interlocked.Increment(ref _repositoryFilesGeneration);
        var files = await session.RepositoryFilesAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (generation != Volatile.Read(ref _repositoryFilesGeneration))
            return;

        var inputs = await ReadTreeInputsAsync(refreshLocks).ConfigureAwait(false);
        if (generation != Volatile.Read(ref _repositoryFilesGeneration))
            return;
        // Directory nodes for the whole work tree are built away from the dispatcher.
        // The UI thread only swaps the visible list.
        var status = CurrentStatusFilter();
        var tree = ComposeRepositoryTree(files, inputs.Expanded, inputs.Locks, inputs.LocksKnown, status);
        await InstallRepositoryTreeAsync(generation, files, tree).ConfigureAwait(false);
        if (!refreshLocks || generation != Volatile.Read(ref _repositoryFilesGeneration))
            return;

        var announced = false;
        try
        {
            // git lfs locks asks the server. A repository that does not use Git LFS has no server,
            // and the command fails with an empty remote address.
            var needsLocks = files.Count > 0 || inputs.Locks.Count > 0;
            var usesLfs = needsLocks && (files.Any(file => file.LfsTracked)
                || await session.UsesLfsAsync(files.Select(file => file.Path).ToArray(), token).ConfigureAwait(false));
            token.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _repositoryFilesGeneration))
                return;
            if (!usesLfs)
            {
                await OnUi(() =>
                {
                    if (generation != _repositoryFilesGeneration)
                        return;
                    _repositoryLocks.Clear();
                    _repositoryLocksKnown = false;
                }).ConfigureAwait(false);
                return;
            }

            await OnUi(() =>
            {
                if (generation != _repositoryFilesGeneration)
                    return;
                BusyText = "Loading LFS locks…";
                announced = true;
            }).ConfigureAwait(false);
            if (!announced)
                return;
            var locks = await session.LfsLocksAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _repositoryFilesGeneration))
                return;
            var locked = new Dictionary<string, LfsLock>(locks.Count, StringComparer.Ordinal);
            foreach (var item in locks)
                locked[item.Path] = item;
            var expanded = await ReadTreeInputsAsync(clearLockKnowledge: false).ConfigureAwait(false);
            var withLocks = ComposeRepositoryTree(files, expanded.Expanded, locked, locksKnown: true, status);
            await OnUi(() =>
            {
                if (generation != _repositoryFilesGeneration)
                    return;
                _repositoryLocks.Clear();
                foreach (var item in locks)
                    _repositoryLocks[item.Path] = item;
                _repositoryLocksKnown = true;
                InstallRepositoryTree(files, withLocks);
            }).ConfigureAwait(false);
        }
        finally
        {
            if (announced)
            {
                await OnUi(() =>
                {
                    if (BusyText == "Loading LFS locks…")
                        BusyText = "";
                }).ConfigureAwait(false);
            }
        }
    }

    private readonly record struct TreeInputs(HashSet<string> Expanded, Dictionary<string, LfsLock> Locks, bool LocksKnown);

    private async Task<TreeInputs> ReadTreeInputsAsync(bool clearLockKnowledge)
    {
        var inputs = default(TreeInputs);
        await OnUi(() =>
        {
            if (clearLockKnowledge)
                _repositoryLocksKnown = false;
            inputs = new TreeInputs(
                new HashSet<string>(_expandedRepositoryDirectories, StringComparer.Ordinal),
                new Dictionary<string, LfsLock>(_repositoryLocks, StringComparer.Ordinal),
                _repositoryLocksKnown);
        }).ConfigureAwait(false);
        return inputs;
    }

    private async Task InstallRepositoryTreeAsync(int generation, IReadOnlyList<RepositoryFile> files, List<RepositoryFileTreeItem> roots)
    {
        await OnUi(() =>
        {
            if (generation != _repositoryFilesGeneration)
                return;
            InstallRepositoryTree(files, roots);
        }).ConfigureAwait(false);
    }

    private void InstallRepositoryTree(IReadOnlyList<RepositoryFile> files, List<RepositoryFileTreeItem> roots)
    {
        _repositoryFiles = files;
        _repositoryFileRoots.Clear();
        _repositoryFileRoots.AddRange(roots);
        PublishRepositoryFileTree();
        OnPropertyChanged(nameof(RepositoryTreeEmpty));
    }

    private List<RepositoryFileTreeItem> ComposeRepositoryTree(
        IReadOnlyList<RepositoryFile> files,
        HashSet<string> expanded,
        IReadOnlyDictionary<string, LfsLock> locks,
        bool locksKnown,
        IReadOnlyDictionary<string, StatusEntry>? status = null)
    {
        var roots = new List<RepositoryFileTreeItem>();
        var directories = new Dictionary<string, RepositoryFileTreeItem>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            StatusEntry? entry = null;
            if (status is not null && !status.TryGetValue(file.Path, out entry))
                continue;
            var parts = file.Path.Split('/');
            var children = roots;
            var prefix = "";
            for (var depth = 0; depth < parts.Length - 1; depth++)
            {
                prefix = prefix.Length == 0 ? parts[depth] : prefix + "/" + parts[depth];
                if (!directories.TryGetValue(prefix, out var directory))
                {
                    directory = new RepositoryFileTreeItem
                    {
                        Path = prefix,
                        Label = parts[depth],
                        Depth = depth,
                        IsDirectory = true,
                        // In changes-only mode every directory is opened so a changed file nested at any depth stays visible. This does not touch the saved expansion set, so turning the toggle off restores it.
                        IsExpanded = RepositoryFilesChangesOnly || expanded.Contains(prefix),
                    };
                    directories.Add(prefix, directory);
                    children.Add(directory);
                }

                children = directory.Children;
            }

            locks.TryGetValue(file.Path, out var fileLock);
            var path = file.Path;
            var tracked = file.LfsTracked;
            children.Add(new RepositoryFileTreeItem
            {
                Path = path,
                Label = parts[^1],
                Depth = parts.Length - 1,
                StatusText = entry is null ? "" : StatusTextFor(entry),
                LfsTracked = tracked,
                LocksKnown = locksKnown,
                Lock = fileLock,
                CanRun = () => !IsBusy && !_lifetime.IsCancellationRequested,
                LockAction = () => ChangeRepositoryLockAsync(path, unlock: false, force: false),
                UnlockAction = () => ChangeRepositoryLockAsync(path, unlock: true, force: false),
                ForceUnlockAction = () => ChangeRepositoryLockAsync(path, unlock: true, force: true),
                HistoryAction = () => ShowFileHistoryAsync(path),
                RemoveAction = () => RemoveRepositoryFileAsync(path),
                FileMenuFactory = () => FileMenuFor(path, tracked),
            });
        }

        SortRepositoryDirectories(roots);
        return roots;
    }

    private static void SortRepositoryDirectories(List<RepositoryFileTreeItem> items)
    {
        items.Sort((left, right) =>
        {
            var directory = right.IsDirectory.CompareTo(left.IsDirectory);
            if (directory != 0)
                return directory;
            var name = StringComparer.OrdinalIgnoreCase.Compare(left.Label, right.Label);
            return name != 0 ? name : StringComparer.Ordinal.Compare(left.Label, right.Label);
        });
        foreach (var item in items)
            SortRepositoryDirectories(item.Children);
    }

    private void PublishRepositoryFileTree()
    {
        var selected = SelectedRepositoryFile;
        var rows = new List<RepositoryFileTreeItem>();
        void AddRows(IEnumerable<RepositoryFileTreeItem> items)
        {
            foreach (var item in items)
            {
                rows.Add(item);
                if (item.IsExpanded)
                    AddRows(item.Children);
            }
        }
        AddRows(_repositoryFileRoots);
        RepositoryFileTree.Reset(rows);
        SelectedRepositoryFile = selected is null ? null
            : rows.FirstOrDefault(item => item.Path == selected.Path && item.IsDirectory == selected.IsDirectory);
    }

    /// <summary>
    /// A click on a file while changes-only is on opens that file's section in the diff and brings the Diff dock to focus unless it already has it. The working-copy all-files diff keeps one expandable section per changed file, so this reveals exactly what was clicked without switching away from the list of changes.
    /// </summary>
    public async Task RevealChangedFileInDiffAsync(string path)
    {
        _host.FocusDock(DockIds.Diff);
        await OpenAllSectionsForPath(path).ConfigureAwait(false);
        JumpToFile?.Invoke(path, null);
    }

    public void ToggleRepositoryDirectory(RepositoryFileTreeItem item)
    {
        if (!item.IsDirectory)
            return;
        item.IsExpanded = !item.IsExpanded;
        if (item.IsExpanded)
            _expandedRepositoryDirectories.Add(item.Path);
        else
            _expandedRepositoryDirectories.Remove(item.Path);
        PublishRepositoryFileTree();
    }

    private Task RemoveRepositoryFileAsync(string path)
    {
        if (_session is null || IsBusy)
            return Task.CompletedTask;
        if (_host.Dialogs is not { } dialogs)
        {
            Fail("Removing a file requires confirmation.");
            return Task.CompletedTask;
        }
        return HoldFocus(async () =>
        {
            var confirmed = await dialogs.ConfirmAsync("Remove file",
                $"Delete {path} from disk and remove it from Git tracking? Its deletion will be staged if it is tracked. Uncommitted changes will be lost; committed history stays unchanged.",
                "Remove");
            if (!confirmed || _session is null)
                return;
            await RunAsync("Removing file…", ct => _session.RemoveRepositoryFileAsync(path, ct));
        });
    }

    private async Task ChangeRepositoryLockAsync(string path, bool unlock, bool force)
    {
        if (_session is null || IsBusy)
            return;
        if (force)
        {
            if (_host.Dialogs is not { } dialogs)
            {
                Fail("Force Unlock requires confirmation before removing another user's LFS lock.");
                return;
            }
            await HoldFocus(async () =>
            {
                _repositoryLocks.TryGetValue(path, out var item);
                var owner = item is { Owner.Length: > 0 } ? $" held by {item.Owner}" : "";
                if (await dialogs.ConfirmAsync("Force Unlock file",
                    $"Remove the LFS lock{owner} on {path}, even if it belongs to another user? They may still be editing this file.",
                    "Force Unlock"))
                    await ChangeRepositoryLockCoreAsync(path, unlock, force);
            });
            return;
        }
        await ChangeRepositoryLockCoreAsync(path, unlock, force);
    }

    private async Task ChangeRepositoryLockCoreAsync(string path, bool unlock, bool force)
    {
        var ok = await RunAsync(unlock ? "Unlocking LFS file…" : "Locking LFS file…", async ct =>
        {
            if (unlock)
                await Session.UnlockLfsFileAsync(path, force, ct);
            else
                await Session.LockLfsFileAsync(path, ct);
            await LoadRepositoryFilesAsync(ct, refreshLocks: true);
        }, refreshRepositoryFiles: false);
        if (!ok && !_lifetime.IsCancellationRequested)
        {
            _repositoryLocksKnown = false;
            var roots = ComposeRepositoryTree(
                _repositoryFiles,
                _expandedRepositoryDirectories,
                _repositoryLocks,
                locksKnown: false,
                CurrentStatusFilter());
            InstallRepositoryTree(_repositoryFiles, roots);
        }
    }

    private void NotifyRepositoryFileCommands()
    {
        OnPropertyChanged(nameof(RepositoryTreeEmpty));
        foreach (var item in RepositoryFileTree)
            item.NotifyCommands();
    }
}
