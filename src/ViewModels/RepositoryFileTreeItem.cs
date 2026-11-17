using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;
using Sextant.Git.Models;

namespace Sextant.ViewModels;

public partial class RepositoryFileTreeItem : ObservableObject
{
    private IAsyncRelayCommand? _lockCommand;
    private IAsyncRelayCommand? _unlockCommand;
    private IAsyncRelayCommand? _forceUnlockCommand;
    private IAsyncRelayCommand? _historyCommand;
    private IAsyncRelayCommand? _removeCommand;
    private WorktreeFileMenu? _fileMenu;

    public required string Path { get; init; }
    public required string Label { get; init; }
    public int Depth { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsFile => !IsDirectory;
    public string StatusText { get; init; } = "";
    public bool HasStatus => StatusText.Length > 0;
    public bool LfsTracked { get; init; }
    public bool LocksKnown { get; init; }
    public LfsLock? Lock { get; init; }
    public List<RepositoryFileTreeItem> Children { get; } = [];
    public Thickness Indent => new(Depth * 14, 0, 0, 0);
    public double ExpandAngle => IsExpanded ? 90 : 0;
    public string StorageText => IsDirectory ? "" : LfsTracked ? "LFS" : "Git";
    public bool HasLockInfo => LocksKnown && Lock is not null;
    public string LockText => !LocksKnown || Lock is null ? ""
        : Lock.Owner.Length == 0 ? "Locked" : $"Locked by {Lock.Owner}";
    public string Tip => IsDirectory ? Path
        : $"{Path} - {(LfsTracked ? "tracked with Git LFS" : "not tracked with Git LFS")}"
            + (StatusText.Length == 0 ? "" : $" - {StatusText}")
            + (LockText.Length == 0 ? "" : $" - {LockText}");
    public bool ShowLock => IsFile && LocksKnown && Lock is null;
    public bool ShowUnlock => IsFile && LocksKnown && Lock is not null;
    public bool ShowLockActions => ShowLock || ShowUnlock;
    public RepositoryViewModel? Owner { get; init; }
    public WorktreeFileMenu? FileMenuOverride { get; init; }

    public WorktreeFileMenu? FileMenu => _fileMenu ??= FileMenuOverride ?? Owner?.FileMenuForRepositoryFile(Path, LfsTracked);
    public bool ShowOpen => FileMenu is not null;
    public IAsyncRelayCommand LockCommand => _lockCommand ??= new AsyncRelayCommand(LockAsync, CanLock);
    public IAsyncRelayCommand UnlockCommand => _unlockCommand ??= new AsyncRelayCommand(UnlockAsync, CanUnlock);
    public IAsyncRelayCommand ForceUnlockCommand => _forceUnlockCommand ??= new AsyncRelayCommand(ForceUnlockAsync, CanUnlock);
    public IAsyncRelayCommand HistoryCommand => _historyCommand ??= new AsyncRelayCommand(ShowHistoryAsync, CanRunForFile);
    public IAsyncRelayCommand RemoveCommand => _removeCommand ??= new AsyncRelayCommand(RemoveAsync, CanRunForFile);

    private bool CanRunCommands() => Owner?.CanRunRepositoryFileActions ?? false;

    private bool CanLock() => ShowLock && CanRunCommands();

    private bool CanUnlock() => ShowUnlock && CanRunCommands();

    private bool CanRunForFile() => IsFile && CanRunCommands();

    private Task LockAsync() => Owner is null ? Task.CompletedTask
        : Owner.ChangeRepositoryFileLockAsync(Path, unlock: false, force: false);

    private Task UnlockAsync() => Owner is null ? Task.CompletedTask
        : Owner.ChangeRepositoryFileLockAsync(Path, unlock: true, force: false);

    private Task ForceUnlockAsync() => Owner is null ? Task.CompletedTask
        : Owner.ChangeRepositoryFileLockAsync(Path, unlock: true, force: true);

    private Task ShowHistoryAsync() => Owner is null ? Task.CompletedTask : Owner.ShowRepositoryFileHistoryAsync(Path);

    private Task RemoveAsync() => Owner is null ? Task.CompletedTask : Owner.RemoveRepositoryFileAsync(Path);

    public void NotifyCommands()
    {
        _lockCommand?.NotifyCanExecuteChanged();
        _unlockCommand?.NotifyCanExecuteChanged();
        _forceUnlockCommand?.NotifyCanExecuteChanged();
        _historyCommand?.NotifyCanExecuteChanged();
        _removeCommand?.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandAngle));
}
