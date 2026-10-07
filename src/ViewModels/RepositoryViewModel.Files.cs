using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;

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
    public bool RepositoryTreeEmpty => _repositoryFiles.Count == 0 && !IsBusy;

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
        if (_session is null)
            return;
        var generation = ++_repositoryFilesGeneration;
        var files = await _session.RepositoryFilesAsync(token);
        token.ThrowIfCancellationRequested();
        if (generation != _repositoryFilesGeneration)
            return;
        _repositoryFiles = files;
        var needsLocks = _repositoryFiles.Count > 0 || _repositoryLocks.Count > 0;
        if (refreshLocks)
            _repositoryLocksKnown = false;
        BuildRepositoryFileTree();
        if (!refreshLocks)
            return;
        try
        {
            if (needsLocks)
                BusyText = "Loading LFS locks…";
            var locks = needsLocks
                ? await _session.LfsLocksAsync(token)
                : [];
            token.ThrowIfCancellationRequested();
            if (generation != _repositoryFilesGeneration)
                return;
            _repositoryLocks.Clear();
            foreach (var item in locks)
                _repositoryLocks[item.Path] = item;
            _repositoryLocksKnown = needsLocks;
        }
        finally
        {
            if (generation == _repositoryFilesGeneration)
                BuildRepositoryFileTree();
        }
    }

    private void BuildRepositoryFileTree()
    {
        _repositoryFileRoots.Clear();
        var directories = new Dictionary<string, RepositoryFileTreeItem>(StringComparer.Ordinal);
        foreach (var file in _repositoryFiles)
        {
            var parts = file.Path.Split('/');
            var children = _repositoryFileRoots;
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
                        IsExpanded = _expandedRepositoryDirectories.Contains(prefix),
                    };
                    directories.Add(prefix, directory);
                    children.Add(directory);
                }
                children = directory.Children;
            }

            _repositoryLocks.TryGetValue(file.Path, out var fileLock);
            children.Add(new RepositoryFileTreeItem
            {
                Path = file.Path,
                Label = parts[^1],
                Depth = parts.Length - 1,
                LfsTracked = file.LfsTracked,
                LocksKnown = _repositoryLocksKnown,
                Lock = fileLock,
                CanRun = () => !IsBusy && !_lifetime.IsCancellationRequested,
                LockAction = () => ChangeRepositoryLockAsync(file.Path, unlock: false, force: false),
                UnlockAction = () => ChangeRepositoryLockAsync(file.Path, unlock: true, force: false),
                ForceUnlockAction = () => ChangeRepositoryLockAsync(file.Path, unlock: true, force: true),
                HistoryAction = () => ShowFileHistoryAsync(file.Path),
            });
        }

        SortRepositoryDirectories(_repositoryFileRoots);
        PublishRepositoryFileTree();
        OnPropertyChanged(nameof(RepositoryTreeEmpty));
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
            BuildRepositoryFileTree();
        }
    }

    private void NotifyRepositoryFileCommands()
    {
        OnPropertyChanged(nameof(RepositoryTreeEmpty));
        foreach (var item in RepositoryFileTree)
            item.NotifyCommands();
    }
}
