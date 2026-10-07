using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;

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
            + (LockText.Length == 0 ? "" : $" - {LockText}");
    public bool ShowLock => IsFile && LocksKnown && Lock is null;
    public bool ShowUnlock => IsFile && LocksKnown && Lock is not null;
    public bool ShowLockActions => ShowLock || ShowUnlock;
    public Func<Task> LockAction { get; init; } = () => Task.CompletedTask;
    public Func<Task> UnlockAction { get; init; } = () => Task.CompletedTask;
    public Func<Task> ForceUnlockAction { get; init; } = () => Task.CompletedTask;
    public Func<Task> HistoryAction { get; init; } = () => Task.CompletedTask;
    public Func<Task> RemoveAction { get; init; } = () => Task.CompletedTask;
    public Func<WorktreeFileMenu?>? FileMenuFactory { get; init; }
    public Func<bool> CanRun { get; init; } = () => false;

    public WorktreeFileMenu? FileMenu => _fileMenu ??= FileMenuFactory?.Invoke();
    public bool ShowOpen => FileMenu is not null;
    public IAsyncRelayCommand LockCommand => _lockCommand ??= new AsyncRelayCommand(LockAction, () => ShowLock && CanRun());
    public IAsyncRelayCommand UnlockCommand => _unlockCommand ??= new AsyncRelayCommand(UnlockAction, () => ShowUnlock && CanRun());
    public IAsyncRelayCommand ForceUnlockCommand => _forceUnlockCommand ??= new AsyncRelayCommand(ForceUnlockAction, () => ShowUnlock && CanRun());
    public IAsyncRelayCommand HistoryCommand => _historyCommand ??= new AsyncRelayCommand(HistoryAction, () => IsFile && CanRun());
    public IAsyncRelayCommand RemoveCommand => _removeCommand ??= new AsyncRelayCommand(RemoveAction, () => IsFile && CanRun());

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
