using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git.Models;

namespace Sextant.ViewModels;

public partial class RepositoryViewModel
{
    private readonly HashSet<string> _checkedLockPaths = new(StringComparer.Ordinal);
    private int _lfsLocksGeneration;

    public ResetCollection<LfsLockRow> LfsLocks { get; } = new();

    [ObservableProperty]
    public partial bool LfsLocksVisible { get; set; }

    [ObservableProperty]
    public partial string LocksMessage { get; set; } = "";

    public bool ShowLocksMessage => LocksMessage.Length > 0;

    partial void OnLocksMessageChanged(string value) => OnPropertyChanged(nameof(ShowLocksMessage));

    [RelayCommand]
    private Task RefreshLfsLocks() =>
        RunAsync("Loading LFS locks…", LoadLfsLocksGuardedAsync, refreshRepositoryFiles: false);

    private async Task LoadLfsLocksGuardedAsync(CancellationToken token)
    {
        try
        {
            await LoadLfsLocksAsync(token);
        }
        catch
        {
            if (!_lifetime.IsCancellationRequested)
                PublishLocks([], "");
            throw;
        }
    }

    private async Task LoadLfsLocksAsync(CancellationToken token)
    {
        if (_session is null)
            return;
        var generation = ++_lfsLocksGeneration;
        var usesLfs = await _session.UsesLfsAsync(null, token);
        token.ThrowIfCancellationRequested();
        if (generation != _lfsLocksGeneration)
            return;
        if (!usesLfs)
        {
            PublishLocks([], "This repository does not use Git LFS.");
            return;
        }

        var listing = await _session.LfsLocksVerifiableAsync(token);
        token.ThrowIfCancellationRequested();
        if (generation != _lfsLocksGeneration)
            return;
        PublishLocks(listing, listing.Mine.Count == 0 && listing.Theirs.Count == 0 ? "No LFS locks." : "");
    }

    internal void ApplyLfsLockListing(LfsLockListing listing, string message = "") =>
        PublishLocks(listing, message);

    private void PublishLocks(LfsLockListing listing, string message)
    {
        var rows = new List<LfsLockRow>();
        AddLockGroup(rows, "My Locks", listing.Mine, mine: true, listing.ForceUnlockSupported);
        AddLockGroup(rows, "Locks", listing.Theirs, mine: false, listing.ForceUnlockSupported);
        var alive = new HashSet<string>(listing.Mine.Select(item => item.Path), StringComparer.Ordinal);
        _checkedLockPaths.RemoveWhere(path => !alive.Contains(path));
        LfsLocks.Reset(rows);
        LocksMessage = message;
        NoteLockChecks();
        NotifyLfsLockCommands();
    }

    private void PublishLocks(IReadOnlyList<LfsLockRow> rows, string message)
    {
        _checkedLockPaths.Clear();
        LfsLocks.Reset(rows);
        LocksMessage = message;
        NotifyLfsLockCommands();
    }

    private void AddLockGroup(List<LfsLockRow> rows, string title, IReadOnlyList<LfsLock> locks, bool mine, bool forceUnlock)
    {
        if (locks.Count == 0)
            return;
        var header = new LfsLockRow
        {
            IsHeader = true,
            IsMine = mine,
            Label = title,
            ShowCheck = mine,
            ToggleChecksCommand = new RelayCommand(ToggleMineChecks, () => mine && !IsBusy && !_lifetime.IsCancellationRequested),
            UnlockAllCommand = new AsyncRelayCommand(UnlockCheckedLocksAsync, () => !IsBusy && !_lifetime.IsCancellationRequested),
        };
        rows.Add(header);
        foreach (var item in locks.OrderBy(lockItem => lockItem.Path, StringComparer.OrdinalIgnoreCase).ThenBy(lockItem => lockItem.Path, StringComparer.Ordinal))
        {
            var row = new LfsLockRow
            {
                IsMine = mine,
                Id = item.Id,
                Path = item.Path,
                Owner = item.Owner,
                Label = item.Path,
                ShowCheck = mine,
                ShowUnlock = mine,
                ShowForceUnlock = !mine && forceUnlock,
                IsChecked = mine && _checkedLockPaths.Contains(item.Path),
                UnlockCommand = new AsyncRelayCommand(
                    () => UnlockListedLockAsync(item.Path, force: !mine),
                    () => !IsBusy && !_lifetime.IsCancellationRequested),
            };
            row.CheckChanged = NoteLockChecks;
            rows.Add(row);
        }
    }

    private void ToggleMineChecks()
    {
        var mine = LfsLocks.Where(row => row.IsLock && row.IsMine).ToList();
        if (mine.Count == 0)
            return;
        var check = mine.Any(row => !row.IsChecked);
        foreach (var row in mine)
            row.IsChecked = check;
        NoteLockChecks();
    }

    private void NoteLockChecks()
    {
        _checkedLockPaths.Clear();
        foreach (var row in LfsLocks)
        {
            if (row.IsLock && row.IsMine && row.IsChecked)
                _checkedLockPaths.Add(row.Path);
        }

        var mine = LfsLocks.Where(row => row.IsLock && row.IsMine).ToList();
        bool? header = mine.Count == 0 ? false
            : mine.All(row => row.IsChecked) ? true
            : mine.All(row => !row.IsChecked) ? false
            : null;
        var showAll = mine.Any(row => row.IsChecked);
        foreach (var row in LfsLocks)
        {
            if (!row.IsHeader || !row.IsMine)
                continue;
            row.HeaderChecked = header;
            row.ShowUnlockAll = showAll;
        }
    }

    private Task UnlockListedLockAsync(string path, bool force)
    {
        if (_session is null || IsBusy)
            return Task.CompletedTask;
        if (!force)
            return UnlockListedCoreAsync([path], force: false);
        if (_host.Dialogs is not { } dialogs)
        {
            Fail("Force unlock requires confirmation before removing another user's LFS lock.");
            return Task.CompletedTask;
        }

        return HoldFocus(async () =>
        {
            var owner = LfsLocks.FirstOrDefault(row => row.IsLock && row.Path == path)?.Owner;
            var held = owner is { Length: > 0 } ? $" held by {owner}" : "";
            if (await dialogs.ConfirmAsync("Force Unlock file",
                $"Remove the LFS lock{held} on {path}, even if it belongs to another user? They may still be editing this file.",
                "Force Unlock"))
                await UnlockListedCoreAsync([path], force: true);
        });
    }

    private Task UnlockCheckedLocksAsync()
    {
        if (_session is null || IsBusy)
            return Task.CompletedTask;
        var paths = LfsLocks.Where(row => row.IsLock && row.IsMine && row.IsChecked).Select(row => row.Path).ToList();
        if (paths.Count == 0)
            return Task.CompletedTask;
        if (_host.Dialogs is not { } dialogs)
        {
            Fail("Unlock all requires confirmation.");
            return Task.CompletedTask;
        }

        var noun = paths.Count == 1 ? "lock" : "locks";
        return HoldFocus(async () =>
        {
            if (await dialogs.ConfirmAsync("Unlock all",
                $"Unlock {paths.Count} selected LFS {noun}?",
                "Unlock all"))
                await UnlockListedCoreAsync(paths, force: false);
        });
    }

    private async Task UnlockListedCoreAsync(IReadOnlyList<string> paths, bool force)
    {
        await RunAsync(paths.Count == 1 ? "Unlocking LFS file…" : "Unlocking LFS files…", async ct =>
        {
            foreach (var path in paths)
                await Session.UnlockLfsFileAsync(path, force, ct);
            if (RepositoryFilesTabOn)
                await LoadRepositoryFilesAsync(ct, refreshLocks: true);
            await LoadLfsLocksAsync(ct);
        }, refreshRepositoryFiles: false);
    }

    private void NotifyLfsLockCommands()
    {
        foreach (var row in LfsLocks)
        {
            row.ToggleChecksCommand?.NotifyCanExecuteChanged();
            row.UnlockCommand?.NotifyCanExecuteChanged();
            row.UnlockAllCommand?.NotifyCanExecuteChanged();
        }
    }
}
