using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant;
using Sextant.Git;
using Sextant.Git.Parsing;
using Sextant.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;

namespace Sextant.ViewModels;

public partial class RepositoryViewModel : ViewModelBase
{
    private readonly IWorkspaceHost _host;
    private readonly CancellationTokenSource _lifetime = new();
    private RepositorySession? _session;
    private GitDirectoryWatcher? _watcher;
    private CancellationTokenSource? _operation;
    private CancellationTokenSource? _details;
    private Task? _load;
    private bool _applying;
    private bool _askedPerformance;
    private int _holdFocusRefresh;
    private bool _watcherFailed;
    private bool _loadingMore;
    private int _historyGeneration;
    private int _seenCommits;
    private string _entrySignature = "";
    private bool _inAppMerge = true;
    private readonly List<FileRowViewModel> _workingFiles = [];
    private readonly HashSet<string> _worktreeLfs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _viewLfs = new(StringComparer.Ordinal);
    private bool _showingCommitFiles;
    private string _refSignature = "";
    private string _commandSignature = "";
    private string? _rawPatch;
    private bool _viewingStaged;
    private FileRowViewModel? _keptFile;
    private bool _diffReady;
    private bool _allFilesShown;
    private bool _armJump;
    private string? _jumpPath;
    private string? _jumpOriginal;
    private readonly List<DiffSection> _sections = [];
    private readonly Queue<DiffSection> _blameQueue = [];
    private bool _blamePump;
    private int _blameGeneration;
    private string? _blameRevision;
    // A loaded changelist starts collapsed. Exceptions are the files the user opened, or closed after Expand all.
    private readonly HashSet<string> _foldExceptions = new(StringComparer.Ordinal);
    private bool _foldsOpen;
    private string? _foldScope;
    private List<DiffRow>? _rowSink;

    public bool ShowSectionFolds => AllFiles && ShowingDiff && _sections.Count > 0;

    private void NoteSectionFolds()
    {
        OnPropertyChanged(nameof(ShowSectionFolds));
        ExpandAllSectionsCommand.NotifyCanExecuteChanged();
        CollapseAllSectionsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The all-files diff should move this file's header to the top of the diff.</summary>
    public event Action<string, string?>? JumpToFile;
    private bool _wasMerge;
    private string? _shownSha;

    public RepositoryViewModel(IWorkspaceHost host, string requestedPath)
    {
        _host = host;
        RequestedPath = requestedPath;
        Title = System.IO.Path.GetFileName(requestedPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(Title))
            Title = requestedPath;
    }

    public string RequestedPath { get; }

    public void UseHiddenBranches(IEnumerable<string>? names)
    {
        _hiddenBranches.Clear();
        if (names is null)
            return;
        foreach (var name in names)
        {
            if (BranchVisibility.IsRemembered(name))
                _hiddenBranches.Add(name);
        }
    }

    public IReadOnlyList<string> HiddenBranchNames =>
        _hiddenBranches.OrderBy(name => name, StringComparer.Ordinal).ToArray();

    private double _locationsWidth = 220;

    private double _graphWidth = 520;

    private double _filesHeight = 180;

    public double LocationsWidth
    {
        get => _locationsWidth;
        set
        {
            if (_locationsWidth == value)
                return;
            _locationsWidth = value;
            OnPropertyChanged();
        }
    }

    public double GraphWidth
    {
        get => _graphWidth;
        set
        {
            if (_graphWidth == value)
                return;
            _graphWidth = value;
            OnPropertyChanged();
        }
    }

    public double FilesHeight
    {
        get => _filesHeight;
        set
        {
            if (_filesHeight == value)
                return;
            _filesHeight = value;
            OnPropertyChanged();
        }
    }

    public bool PanesEdited { get; private set; }

    public void NotePaneEdit() => PanesEdited = true;

    /// <summary>The locations column: branches, remotes, tags, and stashes.</summary>
    [ObservableProperty]
    public partial bool ShowLocations { get; set; } = true;

    [ObservableProperty]
    public partial bool LocationFilterOpen { get; set; }

    [ObservableProperty]
    public partial string LocationFilter { get; set; } = "";

    [RelayCommand]
    private void ToggleLocations()
    {
        ShowLocations = !ShowLocations;
        NotePaneEdit();
        _host.Save();
    }

    [RelayCommand]
    private void ToggleLocationFilter()
    {
        if (LocationFilterOpen)
        {
            LocationFilterOpen = false;
            if (LocationFilter.Length > 0)
                LocationFilter = "";
        }
        else
        {
            LocationFilterOpen = true;
        }
    }

    partial void OnLocationFilterChanged(string value) => PublishLocations(SelectedLocation?.Key);

    public ObservableCollection<GraphRowViewModel> Rows { get; } = [];

    public ResetCollection<LocationItem> Locations { get; } = [];

    private readonly List<LocationItem> _locationRoots = [];

    private readonly HashSet<string> _collapsedLocations = new(StringComparer.Ordinal);

    private readonly HashSet<string> _hiddenBranches = new(StringComparer.Ordinal);

    public ResetCollection<FileRowViewModel> Files { get; } = [];

    public ObservableCollection<DiffRow> DiffRows { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string? ShortcutHint { get; set; }

    [ObservableProperty]
    public partial string? Toplevel { get; set; }

    [ObservableProperty]
    public partial string BranchText { get; set; } = "";

    [ObservableProperty]
    public partial string AheadBehindText { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowAheadBehind { get; set; }

    public bool ShowBranchStatus => ShowAheadBehind || IsBusy;

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial bool IsConflicted { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool CanCancel { get; set; }

    [ObservableProperty]
    public partial string BusyText { get; set; } = "";

    [ObservableProperty]
    public partial string Banner { get; set; } = "";

    [ObservableProperty]
    public partial bool HasBanner { get; set; }

    [ObservableProperty]
    public partial string CommitMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowingWorkingCopy { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowingCommit { get; set; }

    /// <summary>Full message of the selected commit, or the two subjects of a range.</summary>
    [ObservableProperty]
    public partial string CommitMessageText { get; set; } = "";

    /// <summary>Full sha of the selected commit, or <c>older..newer</c> for a range.</summary>
    [ObservableProperty]
    public partial string CommitShaText { get; set; } = "";

    [ObservableProperty]
    public partial string CommitAuthorText { get; set; } = "";

    [ObservableProperty]
    public partial string CommitDateText { get; set; } = "";

    /// <summary>Author and date apply to one commit. A range only has the message and the two hashes.</summary>
    [ObservableProperty]
    public partial bool ShowCommitIdentity { get; set; }

    [ObservableProperty]
    public partial bool ShowCommitStats { get; set; }

    [ObservableProperty]
    public partial string CommitStatsFiles { get; set; } = "";

    [ObservableProperty]
    public partial string CommitStatsRemoved { get; set; } = "";

    [ObservableProperty]
    public partial string CommitStatsAdded { get; set; } = "";

    [ObservableProperty]
    public partial bool NothingStaged { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowLoadMore { get; set; }

    [ObservableProperty]
    public partial string LoadMoreText { get; set; } = "Load more";

    [ObservableProperty]
    public partial bool ShowLoadDiff { get; set; }

    [ObservableProperty]
    public partial string DiffNotice { get; set; } = "";

    [ObservableProperty]
    public partial bool HasDiffNotice { get; set; }

    [ObservableProperty]
    public partial bool CommandsOpen { get; set; }

    [ObservableProperty]
    public partial string CommandLog { get; private set; } = "";

    [ObservableProperty]
    public partial GraphRowViewModel? SelectedGraphRow { get; set; }

    [ObservableProperty]
    public partial FileRowViewModel? SelectedFile { get; set; }

    [ObservableProperty]
    public partial LocationItem? SelectedLocation { get; set; }

    public bool IsReady => _session is not null;

    public bool CanCommit => !IsBusy && ShowingWorkingCopy && !string.IsNullOrWhiteSpace(CommitMessage);

    public bool CanStageAll => !IsBusy && ShowingWorkingCopy && _hasUnstagedWork;

    public bool CanUnstageAll => !IsBusy && ShowingWorkingCopy && _hasStagedWork;

    public bool CanDiscardAll => !IsBusy && ShowingWorkingCopy && (_hasUnstagedWork || _hasStagedWork);

    private bool _hasUnstagedWork;

    private bool _hasStagedWork;

    public bool CanRunCommands => !IsBusy;

    public bool ShowDirtyDot => IsDirty && !IsConflicted;

    public bool ShowCleanDot => !IsDirty && !IsConflicted;

    public Task EnsureLoadedAsync() => _load ??= LoadCoreAsync();

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _operation?.Cancel();
        _details?.Cancel();
        _watcher?.Dispose();
        _watcher = null;
        ClearPreview();
        if (_session is not null)
            await _session.DisposeAsync();
    }

    public void ActivateLocation(LocationItem item)
    {
        if (item.ShowCheckout)
            item.CheckoutCommand.Execute(null);
        else if (item.ShowOpen)
            item.OpenCommand.Execute(null);
        else if (item.ShowReveal || item.ShowTag)
            item.RevealCommand.Execute(null);
    }

    [RelayCommand]
    public Task Refresh() => RunAsync("Refreshing…", ct => Session.RefreshRefsAndStatusAsync(ct));

    public Task RefreshFromFocusAsync()
    {
        // A dialog closes by activating this window. HoldFocus keeps that
        // activation from starting a refresh that takes IsBusy and drops the
        // command the dialog just confirmed.
        if (_session is null || IsBusy || _holdFocusRefresh > 0)
            return Task.CompletedTask;
        return Refresh();
    }

    private async Task HoldFocus(Func<Task> action)
    {
        _holdFocusRefresh++;
        try
        {
            await action();
        }
        finally
        {
            _holdFocusRefresh--;
        }
    }

    [RelayCommand]
    public Task Fetch()
    {
        var progress = Progress();
        return RunAsync("Fetching…", ct => Session.FetchAsync(progress, ct));
    }

    [RelayCommand]
    public Task FetchAll()
    {
        var progress = Progress();
        return RunAsync("Fetching all…", ct => Session.FetchAllAsync(progress, ct));
    }

    [RelayCommand]
    public Task FetchAllAndCleanUp()
    {
        var progress = Progress();
        return RunAsync("Fetching all and cleaning up…", ct => Session.FetchAllPruneAsync(progress, ct));
    }

    [RelayCommand]
    public Task Pull()
    {
        var progress = Progress();
        return RunAsync("Pulling…", ct => Session.PullAsync(progress, ct));
    }

    [RelayCommand]
    public Task Push() => PushCoreAsync(noVerify: false);

    [RelayCommand]
    private Task PushIgnoringLocalChecks() => PushCoreAsync(noVerify: true);

    [RelayCommand]
    public Task Commit() => CommitCoreAsync(noVerify: false);

    [RelayCommand]
    private Task CommitWithoutHooks() => CommitCoreAsync(noVerify: true);

    private async Task CommitCoreAsync(bool noVerify)
    {
        if (_session is null || IsBusy || !ShowingWorkingCopy)
            return;
        if (string.IsNullOrWhiteSpace(CommitMessage))
        {
            Fail("Enter a commit message. Amend, in the commit menu, can keep the current message.");
            return;
        }
        var message = CommitMessage;
        var label = noVerify ? "Committing without hooks…" : "Committing…";
        var ok = await RunAsync(label, ct => _session.CommitAsync(message, ct, noVerify));
        if (ok)
            CommitMessage = "";
    }

    [RelayCommand]
    private Task StageAll() => RunAsync("Staging all…", ct => Session.StageAllAsync(ct));

    [RelayCommand]
    private Task UnstageAll() => RunAsync("Unstaging all…", ct => Session.UnstageAllAsync(ct));

    [RelayCommand]
    private Task DiscardAll()
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy || !CanDiscardAll)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var conflicts = _session.Snapshot().Entries.Any(entry => entry.Kind == ChangeKind.Unmerged);
            var message = conflicts
                ? "Discard every change except conflicted files? Staged and unstaged edits are restored, and untracked files are removed. This cannot be undone."
                : "Discard all changes? Staged and unstaged edits are restored, and untracked files are removed. This cannot be undone.";
            var ok = await dialogs.ConfirmAsync("Discard all", message, "Discard all");
            if (!ok || _session is null)
                return;
            await RunAsync("Discarding all…", ct => _session.DiscardAllAsync(ct));
        });
    }

    [RelayCommand]
    private void Cancel() => _operation?.Cancel();

    [RelayCommand]
    public Task CreateBranch()
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var name = await dialogs.PromptAsync("Create branch", "Branch name");
            if (string.IsNullOrWhiteSpace(name) || _session is null)
                return;
            await RunAsync("Creating branch…", ct => _session.CreateBranchAsync(name, ct));
        });
    }

    [RelayCommand]
    public Task AbortMerge()
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        var kind = _session.Snapshot().Sequencer;
        var noun = kind switch
        {
            SequencerKind.Rebase => "rebase",
            SequencerKind.CherryPick => "cherry-pick",
            SequencerKind.Revert => "revert",
            _ => "merge",
        };
        return HoldFocus(async () =>
        {
            var ok = await dialogs.ConfirmAsync("Abort", $"Abort the current {noun} and return to HEAD?", "Abort");
            if (!ok || _session is null)
                return;
            await RunAsync("Aborting…", ct => _session.AbortSequencerAsync(ct));
        });
    }

    [RelayCommand]
    public async Task ContinueSequencer()
    {
        if (_session is null || IsBusy)
            return;
        await RunAsync("Continuing…", ct => _session.ContinueSequencerAsync(ct));
    }

    [RelayCommand]
    public Task CheckoutFromPalette()
    {
        if (_host.Dialogs is null || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var pick = await PickRefAsync("Checkout", "Checkout a branch.");
            if (pick is null || _session is null)
                return;
            if (pick.StartsWith("refs/heads/", StringComparison.Ordinal))
                await RunAsync("Checking out…", ct => _session.SwitchAsync(ShortHead(pick), ct));
            else if (pick.StartsWith("refs/remotes/", StringComparison.Ordinal))
                await RunAsync("Checking out…", ct => _session.SwitchTrackAsync(ShortRemote(pick), ct));
        });
    }

    [RelayCommand]
    public Task MergeFromPalette()
    {
        if (_host.Dialogs is null || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var pick = await PickRefAsync("Merge", "Merge a branch into HEAD.");
            if (pick is null)
                return;
            var name = pick.StartsWith("refs/heads/", StringComparison.Ordinal) ? ShortHead(pick) : ShortRemote(pick);
            await MergeNamedAsync(name);
        });
    }

    [RelayCommand]
    private void ToggleCommands() => CommandsOpen = !CommandsOpen;

    [RelayCommand]
    private void ActivateTab() => _host.Activate(this);

    [RelayCommand]
    private void CloseTab() => _host.Close(this);

    [RelayCommand]
    private Task LoadMore()
    {
        var past = _session?.Snapshot().HistoryCapped == true;
        return LoadMoreAsync(past);
    }

    public Task LoadMoreFromScrollAsync()
    {
        if (_session is null)
            return Task.CompletedTask;
        var state = _session.Snapshot();
        if (state.HistoryEnded || state.HistoryCapped)
            return Task.CompletedTask;
        return LoadMoreAsync(false);
    }

    [RelayCommand]
    private Task LoadLargeDiff()
    {
        _allowLarge = true;
        return LoadDiffAsync();
    }

    [RelayCommand]
    private void DismissBanner() => HasBanner = false;

    [RelayCommand]
    private async Task CopyBanner()
    {
        if (_host.Dialogs is not null)
            await _host.Dialogs.CopyAsync(Banner);
    }

    private bool _allowLarge;

    private RepositorySession Session => _session ?? throw new InvalidOperationException("Repository is not open.");

    partial void OnCommitMessageChanged(string value)
    {
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(CanCommitOrAmend));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(CanAmend));
        OnPropertyChanged(nameof(CanCommitOrAmend));
        OnPropertyChanged(nameof(CanRunCommands));
        OnPropertyChanged(nameof(ShowBranchStatus));
        NotifyBulkStage();
    }

    partial void OnShowAheadBehindChanged(bool value) => OnPropertyChanged(nameof(ShowBranchStatus));

    partial void OnShowingWorkingCopyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(CanAmend));
        OnPropertyChanged(nameof(CanCommitOrAmend));
        NotifyBulkStage();
    }

    partial void OnNothingStagedChanged(bool value) => OnPropertyChanged(nameof(CanCommit));

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDirtyDot));
        OnPropertyChanged(nameof(ShowCleanDot));
    }

    partial void OnIsConflictedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDirtyDot));
        OnPropertyChanged(nameof(ShowCleanDot));
    }

    partial void OnSelectedGraphRowChanged(GraphRowViewModel? value)
    {
        if (_applying || _rangeOlder is not null)
            return;
        // The graph SelectionChanged handler records a multi-select range in this same turn.
        Dispatcher.UIThread.Post(() =>
        {
            if (_applying || _rangeOlder is not null || !ReferenceEquals(SelectedGraphRow, value))
                return;
            _ = LoadDetailsAsync();
        }, DispatcherPriority.Background);
    }

    partial void OnSelectedFileChanged(FileRowViewModel? value)
    {
        if (value is { IsHeader: false })
            _keptFile = value;
        if (_applying)
            return;
        // A section title is not a file. Leave the open file selected so the bar does not take the row highlight.
        if (value is { IsHeader: true })
        {
            if (_keptFile is not null && Files.Contains(_keptFile))
            {
                _applying = true;
                try
                {
                    SelectedFile = _keptFile;
                }
                finally
                {
                    _applying = false;
                }
            }

            return;
        }
        if (TryRevealOpenFile(value))
            return;
        _allowLarge = false;
        if (AllFiles && value is not null)
        {
            _armJump = true;
            _jumpPath = value.Path;
            _jumpOriginal = value.OriginalPath;
        }
        else
        {
            _armJump = false;
        }

        _ = LoadDiffAsync();
    }

    /// <summary>A click on the file that is already selected. The selection does not change, so scroll from here.</summary>
    public void RevealSelectedFile() => TryRevealOpenFile(SelectedFile);

    private bool TryRevealOpenFile(FileRowViewModel? value)
    {
        if (!AllFiles || !_diffReady || !_allFilesShown || value is not { IsHeader: false } file)
            return false;
        if (!SameOpenDiff(file))
            return false;
        if (!DiffHasFile(file) && !ImageHasFile(file))
            return false;
        OpenForJump(file.Path, file.OriginalPath);
        JumpToFile?.Invoke(file.Path, file.OriginalPath);
        return true;
    }

    private bool SameOpenDiff(FileRowViewModel file)
    {
        if (ShowingBlame)
            return true;
        var range = _rangeOlder is not null && _rangeNewer is not null;
        var workingCopy = !range && (SelectedGraphRow is null || SelectedGraphRow.IsWorkingCopy);
        return !workingCopy || file.FromStagedList == _viewingStaged;
    }

    private bool DiffHasFile(FileRowViewModel file)
    {
        foreach (var row in DiffRows)
        {
            if (row is DiffFileRow header && HeaderMatches(header, file))
                return true;
        }

        return false;
    }

    private bool ImageHasFile(FileRowViewModel file)
    {
        foreach (var row in ImageCompares)
        {
            if (HeaderMatches(row.Path, file))
                return true;
        }

        return false;
    }

    private static bool HeaderMatches(DiffFileRow header, FileRowViewModel file) =>
        HeaderMatches(header.Path.Length > 0 ? header.Path : header.Label, file);

    private static bool HeaderMatches(string header, FileRowViewModel file) =>
        DiffParser.SameFile(header, file.Path)
        || (file.OriginalPath is { Length: > 0 } original && DiffParser.SameFile(header, original));

    private async Task LoadCoreAsync()
    {
        if (!_host.GitReady || _host.GitExecutable is null)
        {
            Fail("Git is not ready.");
            _load = null;
            return;
        }

        BusyText = "Opening…";
        IsBusy = true;
        try
        {
            _session = await OpenSessionAsync(RequestedPath);
            if (_session is null || _lifetime.IsCancellationRequested)
                return;
            Toplevel = _session.Toplevel;
            Title = _session.DisplayName;
            Apply(_session.Snapshot());
            _host.NoteLoaded(this);
            StartWatcher();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }

        if (_session is not null && !_lifetime.IsCancellationRequested)
        {
            await LoadDetailsAsync();
            await MaybeSuggestAsync();
        }
    }

    private async Task<RepositorySession?> OpenSessionAsync(string path)
    {
        try
        {
            return await RepositorySession.OpenAsync(_host.Runner, _host.GitExecutable!, path, _lifetime.Token, _hiddenBranches);
        }
        catch (GitCommandFailedException exception) when (exception.IsDubiousOwnership)
        {
            var dialogs = _host.Dialogs;
            var trust = dialogs is not null && await dialogs.ConfirmAsync(
                "Trust this repository?",
                exception.Message + Environment.NewLine + Environment.NewLine
                    + "Trusting adds this path to the global safe.directory list.",
                "Trust");
            if (!trust)
            {
                Fail(exception.Message);
                _load = null;
                return null;
            }

            try
            {
                await RepositoryAdmin.AddSafeDirectoryAsync(_host.Runner, _host.GitExecutable!, path, _lifetime.Token);
                return await RepositorySession.OpenAsync(_host.Runner, _host.GitExecutable!, path, _lifetime.Token, _hiddenBranches);
            }
            catch (GitCommandFailedException again)
            {
                Fail(again.Message);
                _load = null;
                return null;
            }
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
            _load = null;
            return null;
        }
    }

    private void StartWatcher()
    {
        if (_session is null)
            return;
        _watcher?.Dispose();
        _watcher = new GitDirectoryWatcher(_session.GitDirectory, () => Dispatcher.UIThread.Post(() => _ = RefreshFromWatcherAsync()));
        _watcher.Failed += () => Dispatcher.UIThread.Post(OnWatcherFailed);
    }

    private async Task RefreshFromWatcherAsync()
    {
        if (_session is null || IsBusy || _lifetime.IsCancellationRequested)
            return;
        try
        {
            await _session.RefreshStatusAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested)
                return;
            Apply(_session.Snapshot());
            await LoadDetailsAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
            if (_session is not null)
                Apply(_session.Snapshot());
        }
    }

    private void OnWatcherFailed()
    {
        if (_watcherFailed)
            return;
        _watcherFailed = true;
        _watcher?.Dispose();
        _watcher = null;
        Fail("The repository watcher stopped. Refresh with F5. Sextant will still offer local performance settings when status is slow.");
    }

    private async Task MaybeSuggestAsync()
    {
        if (_askedPerformance || _session is null || _host.Dialogs is null)
            return;
        var suggestion = _session.Snapshot().Suggestion;
        if (suggestion is null)
            return;
        _askedPerformance = true;
        _holdFocusRefresh++;
        try
        {
            var choice = await _host.Dialogs.ConfirmPerformanceAsync(suggestion.Value);
            if (choice is null || _session is null)
                return;
            if (!choice.ManyFiles && !choice.FileSystemMonitor)
                return;
            var settings = new List<(string Key, string Value)>(2);
            if (choice.ManyFiles)
                settings.Add(("feature.manyFiles", "true"));
            if (choice.FileSystemMonitor)
                settings.Add(("core.fsmonitor", "true"));
            await ApplyPerformanceAsync(settings);
        }
        finally
        {
            _holdFocusRefresh--;
        }
    }

    private async Task ApplyPerformanceAsync(List<(string Key, string Value)> settings)
    {
        if (_session is null)
            return;

        // RunAsync refuses to start while a refresh owns IsBusy. The accepted
        // keys still have to reach this repository's local config.
        if (!IsBusy)
        {
            await RunAsync("Writing config…", ct => _session.SetLocalConfigsAsync(settings, ct));
            return;
        }

        try
        {
            await _session.SetLocalConfigsAsync(settings, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
        }

        if (_session is null || _lifetime.IsCancellationRequested || IsBusy)
            return;
        Apply(_session.Snapshot());
        await LoadDetailsAsync();
    }

    private async Task<bool> RunAsync(string label, Func<CancellationToken, Task> action)
    {
        if (_session is null || IsBusy)
            return false;
        _operation?.Dispose();
        _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        IsBusy = true;
        CanCancel = true;
        BusyText = label;
        var ok = false;
        try
        {
            await action(_operation.Token);
            HasBanner = false;
            Banner = "";
            ok = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
        }
        catch (RepositoryActionException exception)
        {
            Fail(exception.Message);
        }
        finally
        {
            IsBusy = false;
            CanCancel = false;
            BusyText = "";
        }

        if (_session is not null && !_lifetime.IsCancellationRequested)
        {
            Apply(_session.Snapshot());
            await LoadDetailsAsync();
        }

        return ok;
    }

    private async Task PushCoreAsync(bool noVerify)
    {
        if (_session is null || IsBusy)
            return;
        var label = noVerify ? "Pushing without local checks…" : "Pushing…";
        var state = _session.Snapshot();
        if (state.Branch.Upstream is null && !state.Branch.Detached && state.Branch.HeadName is { } branch)
        {
            if (_host.Dialogs is null)
                return;
            if (state.Remotes.Count == 0)
            {
                Fail("This branch has no upstream, and this repository has no remotes. Add a remote, then push.");
                return;
            }

            var dialogs = _host.Dialogs;
            await HoldFocus(async () =>
            {
                var remote = await dialogs.PickAsync("Push", $"Push {branch} and set its upstream.", state.Remotes);
                if (remote is null || _session is null)
                    return;
                var progress = Progress();
                await RunAsync(label, ct => _session.PushUpstreamAsync(remote, branch, progress, ct, noVerify));
            });
            return;
        }

        var pushProgress = Progress();
        await RunAsync(label, ct => _session.PushAsync(pushProgress, ct, noVerify));
    }

    private IProgress<string> Progress() => new Progress<string>(text =>
    {
        if (!string.IsNullOrWhiteSpace(text))
            BusyText = text;
    });

    private void NotifyBulkStage()
    {
        OnPropertyChanged(nameof(CanStageAll));
        OnPropertyChanged(nameof(CanUnstageAll));
        OnPropertyChanged(nameof(CanDiscardAll));
    }

    private void Fail(string message)
    {
        Banner = message;
        HasBanner = true;
    }

    private void Apply(SessionState state)
    {
        _rangeOlder = null;
        _rangeNewer = null;
        var wantWork = SelectedGraphRow is null || SelectedGraphRow.IsWorkingCopy;
        var enteringOperation = state.Sequencer != SequencerKind.None && !_wasMerge;
        if (enteringOperation)
            wantWork = true;
        var wantSha = SelectedGraphRow?.Sha;
        var wantPath = SelectedFile is { IsHeader: false } file ? file.Path : null;
        var wantStaged = SelectedFile?.FromStagedList ?? false;

        _applying = true;
        try
        {
            Toplevel = _session?.Toplevel ?? Toplevel;
            if (_session is not null && !string.IsNullOrEmpty(_session.DisplayName))
                Title = _session.DisplayName;
            BranchText = DescribeBranch(state.Branch);
            ShowAheadBehind = state.Branch.Ahead != 0 || state.Branch.Behind != 0;
            AheadBehindText = ShowAheadBehind ? $"↑{state.Branch.Ahead}  ↓{state.Branch.Behind}" : "";
            IsDirty = state.Entries.Count > 0;
            SparseCheckout = state.SparseCheckout;
            IsConflicted = state.Sequencer != SequencerKind.None;
            var unmerged = state.Entries.Any(entry => entry.Kind == ChangeKind.Unmerged);
            _amendAllowed = !state.Branch.Unborn
                && (state.Sequencer == SequencerKind.None || (state.Sequencer == SequencerKind.Rebase && !unmerged));
            ConflictText = state.Sequencer switch
            {
                SequencerKind.Rebase when !unmerged && RebaseStoppedToEdit(_session?.GitDirectory) =>
                    "Rebase stopped to edit this commit. Change the files, stage them, and continue to keep the new contents. Amend first if you also want a new message. Abort returns to where the rebase started.",
                SequencerKind.Rebase => "Rebase in progress. Resolve each file in the editor, save and stage, then continue, or abort the rebase.",
                SequencerKind.CherryPick => "Cherry-pick in progress. Resolve each file in the editor, save and stage, then continue, or abort the cherry-pick.",
                SequencerKind.Revert => "Revert in progress. Resolve each file in the editor, save and stage, then continue, or abort the revert.",
                _ => "Merge in progress. Resolve each file in the editor, save and stage, then continue, or abort the merge.",
            };
            HistoryCaption = state.HistoryLabel ?? "";
            HasHistoryFilter = state.HistoryLabel is { Length: > 0 };
            HasHistoryQuery = state.HasHistoryQuery;
            if (state.Sequencer != SequencerKind.None && !_wasMerge && string.IsNullOrWhiteSpace(CommitMessage) && !string.IsNullOrWhiteSpace(state.MergeMessage))
                CommitMessage = state.MergeMessage.Trim();
            _wasMerge = state.Sequencer != SequencerKind.None;
            NothingStaged = !state.Entries.Any(entry => entry.Staged);
            var hasUnstaged = state.Entries.Any(entry => entry.Kind != ChangeKind.Unmerged && (entry.Unstaged || entry.Kind == ChangeKind.Untracked));
            var hasStaged = state.Entries.Any(entry => entry.Staged && entry.Kind != ChangeKind.Unmerged);
            if (hasUnstaged != _hasUnstagedWork || hasStaged != _hasStagedWork)
            {
                _hasUnstagedWork = hasUnstaged;
                _hasStagedWork = hasStaged;
                NotifyBulkStage();
            }
            ShowLoadMore = !state.HistoryEnded;
            LoadMoreText = state.HistoryCapped ? "Load more (past 50,000)" : "Load more";

            var entrySignature = EntrySignature(state.Entries);
            var inApp = MergeToolCommand.UseInAppEditor(_host.MergeTool, state.Config);
            if (entrySignature != _entrySignature || inApp != _inAppMerge)
            {
                _entrySignature = entrySignature;
                _inAppMerge = inApp;
                RebuildFiles(state);
            }

            var refSignature = RefSignature(state);
            if (refSignature != _refSignature)
            {
                _refSignature = refSignature;
                RebuildLocations(state);
            }

            if (state.HistoryGeneration != _historyGeneration)
            {
                _historyGeneration = state.HistoryGeneration;
                _seenCommits = state.Commits.Count;
                RebuildGraph(state);
            }
            else if (state.Commits.Count > _seenCommits)
            {
                AppendGraph(state);
                _seenCommits = state.Commits.Count;
            }
            else if (Rows.Count > 0 && Rows[0].IsWorkingCopy)
            {
                Rows[0].Subject = "Working copy";
                Rows[0].Detail = WorkingDetail(state);
            }

            UpdateCommands(state);
            if (enteringOperation && unmerged && _inAppMerge)
            {
                wantPath = state.Entries.First(entry => entry.Kind == ChangeKind.Unmerged).Path;
                wantStaged = false;
            }

            RememberSelection(wantWork, wantSha, wantPath, wantStaged);
        }
        finally
        {
            _applying = false;
        }

        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(CanAmend));
        OnPropertyChanged(nameof(CanCommitOrAmend));
    }

    private void RememberSelection(bool wantWork, string? wantSha, string? wantPath, bool wantStaged)
    {
        GraphRowViewModel? row = wantWork
            ? Rows.FirstOrDefault(candidate => candidate.IsWorkingCopy)
            : Rows.FirstOrDefault(candidate => string.Equals(candidate.Sha, wantSha, StringComparison.OrdinalIgnoreCase))
                ?? Rows.FirstOrDefault(candidate => candidate.IsWorkingCopy);
        SelectedGraphRow = row;
        if (row is null || row.IsWorkingCopy)
        {
            SelectedFile = Files.FirstOrDefault(candidate => !candidate.IsHeader && candidate.Path == wantPath && candidate.FromStagedList == wantStaged)
                ?? Files.FirstOrDefault(candidate => !candidate.IsHeader && candidate.Path == wantPath)
                ?? Files.FirstOrDefault(candidate => !candidate.IsHeader);
        }
    }

    private void RebuildGraph(SessionState state)
    {
        Rows.Clear();
        Rows.Add(WorkingRow(state));
        foreach (var commit in state.Commits)
            Rows.Add(CommitRow(commit, state));
    }

    private void AppendGraph(SessionState state)
    {
        if (Rows.Count == 0)
        {
            RebuildGraph(state);
            return;
        }

        for (var i = _seenCommits; i < state.Commits.Count; i++)
            Rows.Add(CommitRow(state.Commits[i], state));
    }

    private GraphRowViewModel WorkingRow(SessionState state) => new()
    {
        IsWorkingCopy = true,
        ShowLanes = false,
        Subject = "Working copy",
        Detail = WorkingDetail(state),
        CreateBranchCommand = CreateBranchCommand,
    };

    private GraphRowViewModel CommitRow(GraphCommit commit, SessionState state)
    {
        var locals = state.Refs.Where(reference =>
            reference.Name.StartsWith("refs/heads/", StringComparison.Ordinal)
            && string.Equals(reference.Oid, commit.Commit.Sha, StringComparison.OrdinalIgnoreCase)).ToList();
        var checkout = locals.Count == 1 && !locals[0].IsHead;
        var name = checkout ? ShortHead(locals[0].Name) : "";
        var head = !string.IsNullOrEmpty(state.Branch.Oid)
            && string.Equals(state.Branch.Oid, commit.Commit.Sha, StringComparison.OrdinalIgnoreCase);
        return new GraphRowViewModel
        {
            Sha = commit.Commit.Sha,
            Commit = commit.Commit,
            Lanes = commit.Lanes,
            ShowLanes = true,
            DrawLanes = !state.FlatHistory,
            Subject = commit.Commit.Subject,
            Author = commit.Commit.AuthorName,
            When = Relative(commit.Commit.AuthorUnixSeconds),
            IsHead = head,
            Detail = CommitDetail(commit.Commit, RefLabel(commit.Commit.Sha, state.Refs, state.HiddenBranches), head),
            Tags = TagNames(commit.Commit.Sha, state.Refs, state.HiddenBranches),
            ShowCheckout = checkout,
            ShowRewrite = true,
            CheckoutCommand = checkout
                ? new AsyncRelayCommand(() => RunAsync("Checking out…", ct => Session.SwitchAsync(name, ct)))
                : UiCommands.Disabled,
            CreateBranchAtCommand = new AsyncRelayCommand(() => CreateBranchAtAsync(commit.Commit)),
            CopyShaCommand = new AsyncRelayCommand(() => CopyText(commit.Commit.Sha)),
            PatchCommand = new AsyncRelayCommand(() => SavePatchAsync(commit.Commit)),
            ResetSoftCommand = new AsyncRelayCommand(() => ResetAsync(commit.Commit, "--soft")),
            ResetMixedCommand = new AsyncRelayCommand(() => ResetAsync(commit.Commit, "--mixed")),
            ResetHardCommand = new AsyncRelayCommand(() => ResetAsync(commit.Commit, "--hard")),
            CherryPickCommand = new AsyncRelayCommand(() => CherryPickAsync(commit.Commit)),
            RevertCommand = new AsyncRelayCommand(() => RevertAsync(commit.Commit)),
            RebaseCommand = new AsyncRelayCommand(() => RebaseFromRowAsync(commit.Commit.Sha, reword: false)),
            RewordCommand = new AsyncRelayCommand(() => RebaseFromRowAsync(commit.Commit.Sha, reword: true)),
            TagCommand = new AsyncRelayCommand(() => TagAsync(commit.Commit)),
        };
    }

    private void RebuildFiles(SessionState state)
    {
        _worktreeLfs.Clear();
        foreach (var path in state.LfsPaths)
            _worktreeLfs.Add(path);
        if (!_showingCommitFiles)
            UseViewLfs(_worktreeLfs);
        _workingFiles.Clear();
        var conflicts = state.Entries.Where(entry => entry.Kind == ChangeKind.Unmerged).ToList();
        var staged = state.Entries.Where(entry => entry.Staged && entry.Kind != ChangeKind.Unmerged).ToList();
        var unstaged = state.Entries.Where(entry => (entry.Unstaged || entry.Kind == ChangeKind.Untracked) && entry.Kind != ChangeKind.Unmerged).ToList();
        AddFileSection("Conflicts", conflicts, stagedList: false, conflict: true);
        AddFileSection("Staged", staged, stagedList: true, conflict: false);
        AddFileSection("Unstaged", unstaged, stagedList: false, conflict: false);
        if (!_showingCommitFiles)
            CopyFiles(_workingFiles);
    }

    private void AddFileSection(string title, List<StatusEntry> entries, bool stagedList, bool conflict)
    {
        if (entries.Count == 0)
            return;
        _workingFiles.Add(new FileRowViewModel { IsHeader = true, Label = title });
        foreach (var entry in entries)
            _workingFiles.Add(FileRow(entry, stagedList, conflict));
    }

    private FileRowViewModel FileRow(StatusEntry entry, bool stagedList, bool conflict)
    {
        var path = entry.Path;
        var untracked = entry.Kind == ChangeKind.Untracked;
        var lfs = CheckAttrParser.IsTracked(_worktreeLfs, path, entry.OriginalPath);
        return new FileRowViewModel
        {
            Path = path,
            OriginalPath = entry.OriginalPath,
            Label = entry.OriginalPath is { Length: > 0 } original ? original + " → " + path : path,
            StatusText = conflict ? "U" : untracked ? "?" : (stagedList ? entry.IndexStatus : entry.WorkTreeStatus).ToString(),
            Kind = entry.Kind,
            FromStagedList = stagedList,
            Untracked = untracked,
            LfsTracked = lfs,
            ShowStage = conflict || !stagedList,
            ShowUnstage = stagedList && !conflict,
            ShowDiscard = !conflict,
            ShowMergetool = conflict,
            MergetoolLabel = _inAppMerge ? "Resolve" : "Merge tool",
            MergetoolMenu = _inAppMerge ? "Resolve in editor" : "Open in merge tool",
            ShowHistory = true,
            ShowLfsTrack = !conflict && !lfs,
            ShowLfsUntrack = !conflict && lfs,
            ShowLfsDownload = !conflict && lfs && !untracked,
            StageCommand = new AsyncRelayCommand(() => RunAsync(conflict ? "Staging resolution…" : "Staging…", ct => Session.StageFileAsync(path, ct))),
            UnstageCommand = new AsyncRelayCommand(() => RunAsync("Unstaging…", ct => Session.UnstageFileAsync(path, ct))),
            DiscardCommand = new AsyncRelayCommand(() => DiscardAsync(path, untracked)),
            MergetoolCommand = new AsyncRelayCommand(() => OpenMergeToolAsync(path)),
            HistoryCommand = new AsyncRelayCommand(() => ShowFileHistoryAsync(path)),
            LfsTrackCommand = new AsyncRelayCommand(() => TrackWithLfs(path)),
            LfsUntrackCommand = new AsyncRelayCommand(() => UntrackLfs(path)),
            LfsDownloadCommand = new AsyncRelayCommand(() => DownloadLfs(path)),
        };
    }

    private bool UseInAppMerge() =>
        MergeToolCommand.UseInAppEditor(_host.MergeTool, _session?.Snapshot().Config);

    private Task OpenMergeToolAsync(string path)
    {
        if (_session is null)
            return Task.CompletedTask;
        if (UseInAppMerge())
        {
            OpenInAppMerge(path);
            return Task.CompletedTask;
        }

        return RunAsync("Opening merge tool…", ct => _session.MergetoolAsync(path, _host.MergeTool, ct));
    }

    private void OpenInAppMerge(string path)
    {
        var working = Rows.FirstOrDefault(row => row.IsWorkingCopy);
        if (working is not null && !ReferenceEquals(SelectedGraphRow, working))
            SelectedGraphRow = working;
        var file = Files.FirstOrDefault(candidate =>
            !candidate.IsHeader && candidate.Kind == ChangeKind.Unmerged && candidate.Path == path);
        if (file is null)
            return;
        if (ReferenceEquals(SelectedFile, file))
        {
            _allowLarge = false;
            _ = LoadDiffAsync();
            return;
        }

        SelectedFile = file;
    }

    public void ApplyMergePreference()
    {
        if (_session is null)
            return;
        var state = _session.Snapshot();
        var inApp = MergeToolCommand.UseInAppEditor(_host.MergeTool, state.Config);
        if (inApp == _inAppMerge)
            return;

        _inAppMerge = inApp;
        var path = SelectedFile is { IsHeader: false } selected ? selected.Path : null;
        var staged = SelectedFile?.FromStagedList ?? false;
        RebuildFiles(state);
        if (_showingCommitFiles)
            return;

        _applying = true;
        try
        {
            SelectedFile = Files.FirstOrDefault(candidate => !candidate.IsHeader && candidate.Path == path && candidate.FromStagedList == staged)
                ?? Files.FirstOrDefault(candidate => !candidate.IsHeader && candidate.Path == path)
                ?? Files.FirstOrDefault(candidate => !candidate.IsHeader);
        }
        finally
        {
            _applying = false;
        }

        if (ShowingWorkingCopy)
            _ = LoadDiffAsync();
    }

    private Task DiscardAsync(string path, bool untracked)
    {
        if (_host.Dialogs is not { } dialogs || _session is null)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var ok = await dialogs.ConfirmAsync(
                "Discard",
                $"Discard changes to {path}? This cannot be undone.",
                "Discard");
            if (!ok || _session is null)
                return;
            if (untracked)
                await RunAsync("Discarding…", ct => _session.DiscardUntrackedAsync(path, ct));
            else
                await RunAsync("Discarding…", ct => _session.DiscardTrackedAsync(path, ct));
        });
    }

    private void RebuildLocations(SessionState state)
    {
        var selected = SelectedLocation?.Key;
        RememberCollapsed();
        _locationRoots.Clear();

        var head = HeadLabel(state.Branch);
        var anyBranchHidden = state.HiddenBranches.Any(BranchVisibility.IsGraphBranch);
        var branches = new List<LocationItem>();
        foreach (var branch in state.Refs.Where(reference => reference.Name.StartsWith("refs/heads/", StringComparison.Ordinal))
                     .OrderBy(reference => reference.Name, StringComparer.Ordinal))
        {
            var name = ShortHead(branch.Name);
            var current = branch.IsHead;
            var hidden = state.HiddenBranches.Contains(branch.Name);
            branches.Add(new LocationItem
            {
                Key = "b:" + name,
                Label = name + UpstreamTrackParser.Suffix(branch.Ahead, branch.Behind),
                SearchText = name,
                IsCurrent = current,
                Oid = branch.Oid,
                ShowCheckout = !current,
                ShowMerge = !current,
                ShowRebase = !current,
                MergeLabel = $"Merge {name} into {head}",
                RebaseLabel = $"Rebase {head} onto {name}",
                ShowDelete = !current,
                ShowSetUpstream = true,
                ShowReveal = true,
                ShowHide = true,
                ShowHideOthers = true,
                ShowEye = true,
                EyeHidden = hidden,
                EyeOpen = !hidden,
                HideLabel = hidden ? "Show branch" : "Hide branch",
                ShowAllBranches = anyBranchHidden,
                LabelOpacity = hidden ? 0.45 : 1,
                CheckoutCommand = new AsyncRelayCommand(() => RunAsync("Checking out…", ct => Session.SwitchAsync(name, ct))),
                MergeCommand = new AsyncRelayCommand(() => MergeNamedAsync(name)),
                RebaseCommand = new AsyncRelayCommand(() => RebaseOntoAsync(name)),
                DeleteCommand = new AsyncRelayCommand(() => DeleteNamedAsync(name)),
                SetUpstreamCommand = new AsyncRelayCommand(() => SetUpstreamNamedAsync(name)),
                RevealCommand = new AsyncRelayCommand(() => RevealAsync(branch.Oid)),
                HideCommand = new AsyncRelayCommand(() => ToggleHiddenAsync(branch.Name)),
                HideOthersCommand = new AsyncRelayCommand(() => HideOthersAsync(branch.Name)),
                ShowAllBranchesCommand = new AsyncRelayCommand(ShowAllBranchesAsync),
            });
        }

        _locationRoots.Add(Section("h:branches", "Branches", GroupLocations(branches, "b"), branches.Count));

        var trackedRefs = state.Refs.Where(reference =>
            reference.Name.StartsWith("refs/remotes/", StringComparison.Ordinal)
            && !reference.Name.EndsWith("/HEAD", StringComparison.Ordinal)).ToList();
        foreach (var group in trackedRefs.GroupBy(reference => RemoteGroup(reference.Name)).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var tracked = new List<LocationItem>();
            var remoteName = group.Key;
            foreach (var remote in group.OrderBy(reference => reference.Name, StringComparer.Ordinal))
            {
                var name = ShortRemote(remote.Name);
                var branchName = name.Length > remoteName.Length + 1 ? name[(remoteName.Length + 1)..] : name;
                var hidden = state.HiddenBranches.Contains(remote.Name);
                tracked.Add(new LocationItem
                {
                    Key = "r:" + name,
                    Label = branchName,
                    SearchText = name,
                    Oid = remote.Oid,
                    ShowCheckout = true,
                    ShowDelete = true,
                    ShowReveal = true,
                    ShowHide = true,
                    ShowHideOthers = true,
                    ShowEye = true,
                    EyeHidden = hidden,
                    EyeOpen = !hidden,
                    HideLabel = hidden ? "Show branch" : "Hide branch",
                    ShowAllBranches = anyBranchHidden,
                    LabelOpacity = hidden ? 0.45 : 1,
                    CheckoutCommand = new AsyncRelayCommand(() => RunAsync("Checking out…", ct => Session.SwitchTrackAsync(name, ct))),
                    DeleteCommand = new AsyncRelayCommand(() => DeleteRemoteBranchAsync(remoteName, branchName)),
                    RevealCommand = new AsyncRelayCommand(() => RevealAsync(remote.Oid)),
                    HideCommand = new AsyncRelayCommand(() => ToggleHiddenAsync(remote.Name)),
                    HideOthersCommand = new AsyncRelayCommand(() => HideOthersAsync(remote.Name)),
                    ShowAllBranchesCommand = new AsyncRelayCommand(ShowAllBranchesAsync),
                });
            }

            _locationRoots.Add(Section("h:remote:" + group.Key, group.Key, GroupLocations(tracked, "r:" + group.Key), tracked.Count));
        }

        var tags = new List<LocationItem>();
        foreach (var tag in state.Refs.Where(reference => reference.Name.StartsWith("refs/tags/", StringComparison.Ordinal))
                     .OrderBy(reference => reference.Name, StringComparer.Ordinal))
        {
            var name = tag.Name["refs/tags/".Length..];
            tags.Add(new LocationItem
            {
                Key = "t:" + name,
                Label = name,
                SearchText = name,
                Oid = tag.Oid,
                ShowTag = true,
                RevealCommand = new AsyncRelayCommand(() => RevealAsync(tag.Oid)),
                CreateBranchFromTagCommand = new AsyncRelayCommand(() => CreateBranchFromTagAsync(name)),
                CheckoutTagCommand = new AsyncRelayCommand(() => CheckoutTagAsync(name)),
                PushTagCommand = new AsyncRelayCommand(() => PushTagAsync(name)),
                DeleteCommand = new AsyncRelayCommand(() => DeleteTagAsync(name)),
                DeleteRemoteTagCommand = new AsyncRelayCommand(() => DeleteRemoteTagAsync(name)),
            });
        }

        _locationRoots.Add(Section("h:tags", "Tags", GroupLocations(tags, "t"), tags.Count));

        var remotes = new List<LocationItem>();
        foreach (var remote in state.Remotes.OrderBy(name => name, StringComparer.Ordinal))
        {
            remotes.Add(new LocationItem
            {
                Key = "m:" + remote,
                Label = remote,
                SearchText = remote,
                ShowDelete = true,
                ShowRename = true,
                DeleteCommand = new AsyncRelayCommand(() => RemoveRemoteAsync(remote)),
                RenameCommand = new AsyncRelayCommand(() => RenameRemoteAsync(remote)),
            });
        }

        _locationRoots.Add(Section("h:remotes", "Remotes", remotes, remotes.Count));

        var stashes = new List<LocationItem>();
        foreach (var stash in state.Stashes)
        {
            var hidden = state.HiddenBranches.Contains(BranchVisibility.StashToken(stash.Sha));
            var label = stash.Ref + "  " + stash.Subject;
            stashes.Add(new LocationItem
            {
                Key = "s:" + stash.Ref,
                Label = label,
                SearchText = label,
                ShowPop = true,
                ShowApply = true,
                ShowDrop = true,
                ShowEye = true,
                EyeHidden = hidden,
                EyeOpen = !hidden,
                HideLabel = hidden ? "Show stash" : "Hide stash",
                LabelOpacity = hidden ? 0.45 : 1,
                PopCommand = new AsyncRelayCommand(() => PopStashAsync(stash)),
                ApplyCommand = new AsyncRelayCommand(() => ApplyStashAsync(stash)),
                DropCommand = new AsyncRelayCommand(() => DropStashAsync(stash)),
                HideCommand = new AsyncRelayCommand(() => ToggleHiddenAsync(BranchVisibility.StashToken(stash.Sha))),
            });
        }

        _locationRoots.Add(Section("h:stashes", "Stashes", stashes, stashes.Count));

        var modules = new List<LocationItem>();
        foreach (var module in state.Submodules)
        {
            var suffix = module.State switch
            {
                SubmoduleState.Uninitialized => "not checked out",
                SubmoduleState.Modified => "modified",
                SubmoduleState.Conflict => "conflict",
                _ => string.IsNullOrEmpty(module.Describe) ? Short(module.Sha) : module.Describe,
            };
            modules.Add(new LocationItem
            {
                Key = "u:" + module.Path,
                Label = module.Path + "  " + suffix,
                SearchText = module.Path,
                ShowOpen = module.State != SubmoduleState.Uninitialized,
                OpenCommand = new AsyncRelayCommand(() => OpenSubmoduleAsync(module)),
            });
        }

        _locationRoots.Add(Section("h:submodules", "Submodules", modules, modules.Count));

        if (state.Worktrees.Count > 0)
        {
            var trees = new List<LocationItem>();
            foreach (var tree in state.Worktrees.OrderBy(tree => tree.Path, StringComparer.Ordinal))
            {
                var current = Toplevel is not null && RepoPath.Same(tree.Path, Toplevel);
                var tracked = tree.Branch is null
                    ? null
                    : state.Refs.FirstOrDefault(reference => reference.Name == tree.Branch);
                var name = tree.Bare
                    ? "bare"
                    : tree.Detached ? "detached " + Short(tree.Head) : ShortHead(tree.Branch ?? "");
                var pending = tracked is null ? "" : UpstreamTrackParser.Suffix(tracked.Ahead, tracked.Behind);
                trees.Add(new LocationItem
                {
                    Key = "w:" + tree.Path,
                    Label = name + pending + "  " + tree.Path,
                    SearchText = tree.Path,
                    IsCurrent = current,
                    ShowOpen = !current,
                    OpenCommand = new AsyncRelayCommand(() => _host.OpenRepositoryAsync(tree.Path)),
                });
            }

            _locationRoots.Add(Section("h:worktrees", "Worktrees", trees, trees.Count));
        }

        PublishLocations(selected);
    }

    public void ToggleLocation(LocationItem item)
    {
        if (item.Children.Count == 0)
            return;
        item.IsExpanded = !item.IsExpanded;
        if (item.CollapseKey.Length > 0)
        {
            if (item.IsExpanded)
                _collapsedLocations.Remove(item.CollapseKey);
            else
                _collapsedLocations.Add(item.CollapseKey);
        }

        PublishLocations(SelectedLocation?.Key);
    }

    private void PublishLocations(string? selectedKey)
    {
        var flat = new List<LocationItem>();
        var filter = LocationFilter.Trim();
        foreach (var root in _locationRoots)
        {
            if (filter.Length == 0)
                AppendVisible(root, 0, flat);
            else
                AppendFiltered(root, 0, filter, flat);
        }

        Locations.Reset(flat);
        SelectedLocation = selectedKey is null ? null : flat.FirstOrDefault(item => item.Key == selectedKey);
    }

    private static bool AppendFiltered(LocationItem item, int depth, string filter, List<LocationItem> flat)
    {
        if (Matches(item, filter))
        {
            AppendAll(item, depth, flat);
            return true;
        }

        if (item.Children.Count == 0)
            return false;

        var start = flat.Count;
        item.Depth = depth;
        flat.Add(item);
        var any = false;
        foreach (var child in item.Children)
        {
            if (AppendFiltered(child, depth + 1, filter, flat))
                any = true;
        }

        if (any)
            return true;
        flat.RemoveRange(start, flat.Count - start);
        return false;
    }

    private static void AppendAll(LocationItem item, int depth, List<LocationItem> flat)
    {
        item.Depth = depth;
        flat.Add(item);
        foreach (var child in item.Children)
            AppendAll(child, depth + 1, flat);
    }

    private static bool Matches(LocationItem item, string filter)
    {
        if (item.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase))
            return true;
        return !item.IsHeader && item.Label.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendVisible(LocationItem item, int depth, List<LocationItem> flat)
    {
        item.Depth = depth;
        flat.Add(item);
        if (!item.IsExpanded)
            return;
        foreach (var child in item.Children)
            AppendVisible(child, depth + 1, flat);
    }

    private void RememberCollapsed() => RememberCollapsed(_locationRoots);

    private void RememberCollapsed(IEnumerable<LocationItem> items)
    {
        foreach (var item in items)
        {
            if (item.Children.Count > 0 && item.CollapseKey.Length > 0)
            {
                if (item.IsExpanded)
                    _collapsedLocations.Remove(item.CollapseKey);
                else
                    _collapsedLocations.Add(item.CollapseKey);
            }

            RememberCollapsed(item.Children);
        }
    }

    private List<LocationItem> GroupLocations(IReadOnlyList<LocationItem> leaves, string scope)
    {
        var byPath = new Dictionary<string, LocationItem>(StringComparer.Ordinal);
        foreach (var leaf in leaves)
            byPath[leaf.Label] = leaf;
        return MapNodes(PathGrouping.Group(byPath.Keys), byPath, scope);
    }

    private List<LocationItem> MapNodes(
        IReadOnlyList<PathGrouping.Node> nodes,
        Dictionary<string, LocationItem> byPath,
        string scope)
    {
        var list = new List<LocationItem>();
        foreach (var node in nodes)
        {
            var children = MapNodes(node.Children, byPath, scope);
            var refs = (node.RefPath is null ? 0 : 1) + children.Sum(RefCount);
            var label = node.Children.Count == 0 ? node.Label : $"{node.Label} ({refs})";
            if (node.RefPath is { } path && byPath.TryGetValue(path, out var leaf))
            {
                leaf.Label = label;
                foreach (var child in children)
                    leaf.Children.Add(child);
                if (children.Count > 0)
                    ApplyFold(leaf, scope + ":" + node.FullName);
                list.Add(leaf);
                continue;
            }

            var folder = new LocationItem
            {
                Key = "g:" + scope + ":" + node.FullName,
                Label = label,
            };
            ApplyFold(folder, scope + ":" + node.FullName);
            foreach (var child in children)
                folder.Children.Add(child);
            list.Add(folder);
        }

        return list;
    }

    private void ApplyFold(LocationItem item, string collapseKey)
    {
        item.CollapseKey = collapseKey;
        item.IsExpanded = !_collapsedLocations.Contains(collapseKey);
    }

    private static int RefCount(LocationItem item) =>
        (item.Key.StartsWith("g:", StringComparison.Ordinal) ? 0 : 1) + item.Children.Sum(RefCount);

    private LocationItem Section(string key, string title, IReadOnlyList<LocationItem> children, int count)
    {
        var section = new LocationItem
        {
            IsHeader = true,
            Key = key,
            Label = $"{title} ({count})",
            SearchText = title,
            IsExpanded = !_collapsedLocations.Contains(key),
            CollapseKey = key,
        };
        foreach (var child in children)
            section.Children.Add(child);
        return section;
    }

    private Task MergeNamedAsync(string name)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        var current = HeadLabel(_session.Snapshot().Branch);
        return HoldFocus(async () =>
        {
            var ok = await dialogs.ConfirmAsync("Merge", $"Merge {name} into {current}?", "Merge");
            if (!ok || _session is null)
                return;
            await RunAsync("Merging…", ct => _session.MergeAsync(name, ct));
        });
    }

    private Task RebaseOntoAsync(string name)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        var current = HeadLabel(_session.Snapshot().Branch);
        return HoldFocus(async () =>
        {
            var ok = await dialogs.ConfirmAsync("Rebase", $"Rebase {current} onto {name}?", "Rebase");
            if (!ok || _session is null)
                return;
            await RunAsync("Rebasing…", ct => _session.RebaseAsync(name, ct));
        });
    }

    private Task DeleteNamedAsync(string name)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var ok = await dialogs.ConfirmAsync("Delete branch", $"Delete {name}?", "Delete");
            if (!ok || _session is null)
                return;
            var unmerged = false;
            await RunAsync("Deleting branch…", async ct =>
            {
                try
                {
                    await _session.DeleteBranchAsync(name, ct);
                }
                catch (GitCommandFailedException exception) when (IsNotFullyMerged(exception))
                {
                    unmerged = true;
                }
            });
            if (!unmerged || _session is null)
                return;
            var force = await dialogs.ConfirmAsync(
                "Force delete branch",
                $"{name} is not fully merged. Force delete removes it anyway.",
                "Force delete");
            if (!force || _session is null)
                return;
            await RunAsync("Deleting branch…", ct => _session.ForceDeleteBranchAsync(name, ct));
        });
    }

    private Task DeleteRemoteBranchAsync(string remote, string branch)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        var shown = remote + "/" + branch;
        return HoldFocus(async () =>
        {
            var ok = await dialogs.ConfirmAsync("Delete branch", $"Delete {shown} from {remote}?", "Delete");
            if (!ok || _session is null)
                return;
            bool merged;
            try
            {
                merged = await _session.IsMergedIntoHeadAsync(shown, _lifetime.Token);
            }
            catch (GitCommandFailedException exception)
            {
                Fail(exception.Message);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!merged)
            {
                var force = await dialogs.ConfirmAsync(
                    "Force delete branch",
                    $"{shown} is not fully merged. Force delete removes it from {remote} anyway.",
                    "Force delete");
                if (!force || _session is null)
                    return;
            }

            var progress = Progress();
            await RunAsync("Deleting branch…", ct => _session.DeleteRemoteBranchAsync(remote, branch, progress, ct));
        });
    }

    private static bool IsNotFullyMerged(GitCommandFailedException exception) =>
        exception.StandardError.Contains("not fully merged", StringComparison.OrdinalIgnoreCase);

    private Task SetUpstreamNamedAsync(string branch)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        var options = _session.Snapshot().Refs
            .Where(reference => reference.Name.StartsWith("refs/remotes/", StringComparison.Ordinal)
                && !reference.Name.EndsWith("/HEAD", StringComparison.Ordinal))
            .Select(reference => ShortRemote(reference.Name))
            .ToList();
        if (options.Count == 0)
        {
            Fail("There is no remote-tracking branch to use as upstream.");
            return Task.CompletedTask;
        }

        return HoldFocus(async () =>
        {
            var pick = await dialogs.PickAsync("Set upstream", $"Upstream for {branch}", options);
            if (pick is null || _session is null)
                return;
            await RunAsync("Setting upstream…", ct => _session.SetUpstreamAsync(branch, pick, ct));
        });
    }

    private async Task RevealAsync(string oid)
    {
        if (_session is null)
            return;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var found = Rows.FirstOrDefault(row => string.Equals(row.Sha, oid, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
            {
                SelectedGraphRow = found;
                return;
            }

            var state = _session.Snapshot();
            if (state.HistoryEnded)
                break;
            var pastCap = state.HistoryCapped;
            var loaded = await _session.LoadMoreHistoryAsync(pastCap, _lifetime.Token);
            Apply(_session.Snapshot());
            if (!loaded)
            {
                if (!pastCap && _session.Snapshot().HistoryCapped)
                    continue;
                break;
            }
        }

        var row = Rows.FirstOrDefault(candidate => string.Equals(candidate.Sha, oid, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            Fail("That commit is not in the loaded history.");
        else
            SelectedGraphRow = row;
    }

    private async Task<string?> PickRefAsync(string title, string message)
    {
        if (_host.Dialogs is null || _session is null)
            return null;
        var options = _session.Snapshot().Refs
            .Where(reference => reference.Name.StartsWith("refs/heads/", StringComparison.Ordinal)
                || (reference.Name.StartsWith("refs/remotes/", StringComparison.Ordinal)
                    && !reference.Name.EndsWith("/HEAD", StringComparison.Ordinal)))
            .Select(reference => reference.Name.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? ShortHead(reference.Name)
                : ShortRemote(reference.Name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var pick = await _host.Dialogs.PickAsync(title, message, options);
        if (pick is null)
            return null;
        var match = _session.Snapshot().Refs.FirstOrDefault(reference =>
            ShortHead(reference.Name) == pick || ShortRemote(reference.Name) == pick);
        return match?.Name;
    }

    private async Task LoadMoreAsync(bool pastCap)
    {
        if (_session is null || _loadingMore || IsBusy)
            return;
        _loadingMore = true;
        try
        {
            await _session.LoadMoreHistoryAsync(pastCap, _lifetime.Token);
            Apply(_session.Snapshot());
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
        }
        finally
        {
            _loadingMore = false;
        }
    }

    private async Task LoadDetailsAsync()
    {
        if (_applying || _lifetime.IsCancellationRequested)
            return;
        var row = SelectedGraphRow;
        if (row is null || row.IsWorkingCopy)
        {
            ShowingWorkingCopy = true;
            ShowingCommit = false;
            ClearCommitFields();
            _shownSha = null;
            if (_showingCommitFiles)
                RestoreWorkingFiles();
            await LoadDiffAsync();
            return;
        }

        ShowingWorkingCopy = false;
        ShowingCommit = true;
        var sha = row.Sha ?? "";
        var same = _shownSha == sha && _showingCommitFiles;
        CommitShaText = sha;
        CommitAuthorText = AuthorLine(row.Commit);
        CommitDateText = row.Commit is null ? "" : CommitStamp(row.Commit.AuthorUnixSeconds);
        ShowCommitIdentity = CommitAuthorText.Length > 0 || CommitDateText.Length > 0;
        // The body arrives after the subject. Selecting this commit again must not drop it.
        if (!same)
        {
            CommitMessageText = row.Subject;
            ShowCommitStats = false;
        }

        if (_session is null || sha.Length == 0 || same)
            return;
        ReplaceDetails();
        var token = _details!.Token;
        try
        {
            var parent = row.Commit?.Parents.Count > 0 ? row.Commit.Parents[0] : null;
            var files = await _session.CommitFilesAsync(sha, parent, token);
            if (files is null || token.IsCancellationRequested)
                return;
            var tracked = await LfsMarksAsync(files, sha, token);
            if (token.IsCancellationRequested)
                return;
            ShowCommitFiles(files, tracked);
            _shownSha = sha;
            _diffParent = parent;
            await ApplyCommitStatsAsync(parent, sha, token);
            try
            {
                var message = await _session.CommitMessageAsync(sha, token);
                if (!token.IsCancellationRequested && message.Length > 0)
                    CommitMessageText = message;
            }
            catch (GitCommandFailedException)
            {
            }

            await LoadDiffAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
        }
    }

    private string? _diffParent;

    private void RestoreWorkingFiles()
    {
        var previous = SelectedFile;
        _showingCommitFiles = false;
        UseViewLfs(_worktreeLfs);
        _applying = true;
        try
        {
            CopyFiles(_workingFiles);
            SelectedFile = Files.FirstOrDefault(candidate => !candidate.IsHeader && candidate.Path == previous?.Path && candidate.FromStagedList == (previous?.FromStagedList ?? false))
                ?? Files.FirstOrDefault(candidate => !candidate.IsHeader);
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>
    /// The file list view runs the replacement while holding the working-copy scroll position.
    /// </summary>
    public event Action<Action>? PreserveFileScroll;

    private void CopyFiles(IReadOnlyList<FileRowViewModel> rows)
    {
        void Apply()
        {
            Files.Clear();
            foreach (var row in rows)
                Files.Add(row);
        }

        var preserve = PreserveFileScroll;
        if (preserve is null)
            Apply();
        else
            preserve(Apply);
    }

    private void ShowCommitFiles(IReadOnlyList<CommitFileChange> files, IReadOnlySet<string> tracked)
    {
        var previous = SelectedFile?.Path;
        _showingCommitFiles = true;
        UseViewLfs(tracked);
        _applying = true;
        try
        {
            var rows = new List<FileRowViewModel>(files.Count + 1);
            if (files.Count > 0)
                rows.Add(new FileRowViewModel { IsHeader = true, Label = $"Changes ({files.Count})" });
            foreach (var change in files)
            {
                rows.Add(new FileRowViewModel
                {
                    Path = change.Path,
                    OriginalPath = change.OriginalPath,
                    Label = change.OriginalPath is { Length: > 0 } original ? original + " → " + change.Path : change.Path,
                    StatusText = Letter(change.Kind),
                    Kind = change.Kind,
                    LfsTracked = CheckAttrParser.IsTracked(tracked, change.Path, change.OriginalPath),
                    ShowHistory = true,
                    HistoryCommand = new AsyncRelayCommand(() => ShowFileHistoryAsync(change.Path)),
                });
            }

            Files.Reset(rows);
            SelectedFile = rows.FirstOrDefault(candidate => !candidate.IsHeader && candidate.Path == previous)
                ?? rows.FirstOrDefault(candidate => !candidate.IsHeader);
        }
        finally
        {
            _applying = false;
        }
    }

    private async Task LoadDiffAsync()
    {
        if (_lifetime.IsCancellationRequested)
            return;
        var armJump = _armJump;
        var jumpPath = _jumpPath;
        var jumpOriginal = _jumpOriginal;
        _armJump = false;
        if (ShowingBlame)
        {
            await LoadBlameAsync(armJump, jumpPath, jumpOriginal);
            return;
        }

        var file = SelectedFile is { IsHeader: false } selected ? selected : null;
        var row = SelectedGraphRow;
        var range = _rangeOlder is not null && _rangeNewer is not null;
        var workingCopy = !range && (row is null || row.IsWorkingCopy);
        if (_session is null || (!AllFiles && file is null))
        {
            ClearDiff(workingCopy ? "Select a file." : "");
            _diffReady = true;
            _allFilesShown = false;
            return;
        }

        var merge = workingCopy
            && file is { Kind: ChangeKind.Unmerged }
            && (!AllFiles || UseInAppMerge());
        if (!merge)
            ClearMerge();

        _diffReady = false;
        ReplaceDetails();
        var token = _details!.Token;
        var allowLarge = _allowLarge;
        var ignoreWhitespace = IgnoreWhitespace;
        try
        {
            if (merge)
            {
                await LoadMergeAsync(file!, allowLarge, token);
                return;
            }

            DiffDocument? document;
            if (range)
            {
                _viewingStaged = false;
                document = await _session.RangeDiffAsync(
                    _rangeOlder!,
                    _rangeNewer!,
                    AllFiles ? null : file!.Path,
                    allowLarge,
                    ignoreWhitespace,
                    token);
            }
            else if (workingCopy)
            {
                _viewingStaged = file?.FromStagedList == true;
                document = AllFiles
                    ? await _session.WorktreeDiffAsync(_viewingStaged, allowLarge, ignoreWhitespace, token)
                    : await _session.WorkingDiffAsync(file!.Path, file.FromStagedList, file.Untracked, allowLarge, token, ignoreWhitespace);
            }
            else
            {
                _viewingStaged = false;
                var parent = _diffParent ?? (row?.Commit?.Parents.Count > 0 ? row.Commit.Parents[0] : null);
                document = await _session.CommitDiffAsync(row!.Sha!, parent, AllFiles ? null : file!.Path, allowLarge, token, ignoreWhitespace);
            }

            if (document is null || token.IsCancellationRequested)
                return;
            RememberObjects(range, workingCopy, file);
            RenderDiff(document, file, workingCopy);
            if (armJump && AllFiles && jumpPath is { Length: > 0 })
            {
                OpenForJump(jumpPath, jumpOriginal);
                JumpToFile?.Invoke(jumpPath, jumpOriginal);
            }
            await LoadImageAsync(file, workingCopy, range, token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
        }
        catch (RepositoryActionException exception)
        {
            Fail(exception.Message);
        }
    }

    private async Task LoadMergeAsync(FileRowViewModel file, bool allowLarge, CancellationToken token)
    {
        var document = await _session!.ConflictAsync(file.Path, allowLarge, token);
        if (document is null || token.IsCancellationRequested)
            return;
        if (document.IsTooLarge)
        {
            ClearDiff("This diff is large. Load it only if you need the whole file.");
            ShowLoadDiff = true;
            return;
        }

        if (document.IsBinary)
        {
            ClearDiff(UseInAppMerge()
                ? "This conflict is binary, so the editor cannot open it."
                : "This conflict is binary. Open it in the external merge tool.");
            return;
        }

        ClearPreview();
        MergePath = file.Path;
        MergeEdit = MergeSession.FromPieces(document.Pieces);
        DiffRows.Clear();
        _rawPatch = null;
        ShowLoadDiff = false;
        HasDiffNotice = false;
        DiffNotice = "";
        ShowingMerge = true;
        _allFilesShown = false;
        _diffReady = true;
    }

    private void ClearMerge()
    {
        ShowingMerge = false;
        MergeEdit = null;
        MergePath = "";
    }

    private void RenderDiff(DiffDocument document, FileRowViewModel? file, bool workingCopy)
    {
        ClearSections();
        ClearMerge();
        ClearPreview();
        DiffRows.Clear();
        _rawPatch = document.RawPatch;
        NoteLfs(document, !AllFiles);
        ShowLoadDiff = document.IsTooLarge;
        if (document.IsTooLarge)
        {
            HasDiffNotice = true;
            DiffNotice = "This diff is large. Load it only if you need the whole file.";
            _allFilesShown = false;
            _diffReady = true;
            return;
        }

        if (AllFiles)
        {
            NoteChangelist();
            _allFilesShown = true;
            var files = string.IsNullOrEmpty(document.RawPatch) ? [] : DiffParser.ParseFiles(document.RawPatch);
            if (files.Count == 0)
            {
                HasDiffNotice = true;
                DiffNotice = "No textual changes.";
                _diffReady = true;
                return;
            }

            HasDiffNotice = false;
            DiffNotice = "";
            foreach (var entry in files)
            {
                var label = string.IsNullOrEmpty(entry.Path) ? "Diff" : entry.Path;
                var key = entry.Path.Length > 0 ? entry.Path : label;
                var trackedFile = MarkedLfs(entry.Path);
                var header = new DiffFileRow
                {
                    Path = entry.Path,
                    Label = label,
                    LfsTracked = trackedFile,
                    CanFold = true,
                    Expanded = IsFoldOpen(key),
                    FileMenu = FileMenuFor(entry.Path, trackedFile),
                };
                var section = new DiffSection(key, header);
                header.ToggleCommand = new RelayCommand(() => SetExpanded(section, !header.Expanded));
                _rowSink = section.Body;
                try
                {
                    AppendFileDiff(entry.Document, workingCopy, KindForDiff(entry.Document), path: entry.Path, notes: document.LfsFiles);
                }
                finally
                {
                    _rowSink = null;
                }

                _sections.Add(section);
                DiffRows.Add(header);
                if (header.Expanded)
                {
                    foreach (var row in section.Body)
                        DiffRows.Add(row);
                }
            }

            _diffReady = true;
            NoteSectionFolds();
            return;
        }

        _allFilesShown = false;
        AppendFileDiff(document, workingCopy, file?.Kind ?? ChangeKind.Modified, notice: true, path: file?.Path, notes: document.LfsFiles);
        _diffReady = true;
    }

    private void AppendFileDiff(
        DiffDocument document,
        bool workingCopy,
        ChangeKind kind,
        bool notice = false,
        string? path = null,
        IReadOnlyList<LfsFileNote>? notes = null)
    {
        _linePath = path;
        if (document.IsBinary)
        {
            if (notes is not null && notes.Any(note => note.Path == (path ?? "")))
                return;
            // The preview is a row in this file, in order with the text around it.
            if (PreviewPath(path))
                return;
            NoteFile(notice, "Binary file.");
            return;
        }

        if (document.Hunks.Count == 0)
        {
            NoteFile(notice, document.IsNewFile ? "New file." : "No textual changes.");
            return;
        }

        if (notice)
        {
            HasDiffNotice = false;
            DiffNotice = "";
        }

        var parts = CanStageParts(workingCopy, kind, document);
        var patch = document.RawPatch;
        var hunkLabel = _viewingStaged ? "Unstage hunk" : "Stage hunk";
        var lineLabel = _viewingStaged ? "Unstage line" : "Stage line";
        for (var index = 0; index < document.Hunks.Count; index++)
        {
            var hunk = document.Hunks[index];
            var hunkIndex = index;
            AddRow(new DiffHunkRow
            {
                Header = hunk.Header,
                ShowAction = parts,
                ActionLabel = hunkLabel,
                ActionCommand = parts
                    ? new AsyncRelayCommand(() => ApplyShownHunkAsync(patch, hunkIndex))
                    : UiCommands.Disabled,
            });
            if (SideBySide)
                AppendSideBySide(hunk);
            else
                AppendInline(hunk, parts, patch, hunkIndex, lineLabel);
        }
    }

    private void NoteFile(bool notice, string message)
    {
        if (notice)
        {
            HasDiffNotice = true;
            DiffNotice = message;
            return;
        }

        AddRow(new DiffLineRow { Text = message, Background = DiffColors.Clear });
    }

    private void AppendInline(DiffHunk hunk, bool parts, string patch, int hunkIndex, string lineLabel)
    {
        var oldLine = hunk.OldStart;
        var newLine = hunk.NewStart;
        var lines = new List<EditorLine>(hunk.Lines.Count);
        for (var lineIndex = 0; lineIndex < hunk.Lines.Count; lineIndex++)
        {
            var line = hunk.Lines[lineIndex];
            var kind = line.Kind switch
            {
                DiffLineKind.Added => EditorLineKind.Added,
                DiffLineKind.Removed => EditorLineKind.Removed,
                _ => EditorLineKind.Context,
            };
            var show = parts && line.Kind is DiffLineKind.Added or DiffLineKind.Removed;
            var captured = lineIndex;
            var command = show
                ? new AsyncRelayCommand(() => ApplyShownLineAsync(patch, hunkIndex, captured))
                : UiCommands.Disabled;
            var number = DiffLineNumbers.For(line.Kind, ref oldLine, ref newLine);
            lines.AddRange(FoldEditorLines(line.Text, number.Old, number.New, "", kind, show, lineLabel, command));
        }

        AddEditor(_linePath, lines, []);
    }

    private void AppendSideBySide(DiffHunk hunk)
    {
        var left = new List<EditorLine>();
        var right = new List<EditorLine>();
        foreach (var line in DiffLineNumbers.SideBySide(hunk))
        {
            var leftKind = line.LeftText is null
                ? EditorLineKind.Empty
                : line.LeftRemoved ? EditorLineKind.Removed : EditorLineKind.Context;
            var rightKind = line.RightText is null
                ? EditorLineKind.Empty
                : line.RightAdded ? EditorLineKind.Added : EditorLineKind.Context;
            var leftLines = FoldEditorLines(line.LeftText, line.LeftNumber, "", "", leftKind, false, "", UiCommands.Disabled, line.LeftText is null);
            var rightLines = FoldEditorLines(line.RightText, "", line.RightNumber, "", rightKind, false, "", UiCommands.Disabled, line.RightText is null);
            var count = Math.Max(leftLines.Count, rightLines.Count);
            PadEditorLines(leftLines, count);
            PadEditorLines(rightLines, count);
            left.AddRange(leftLines);
            right.AddRange(rightLines);
        }

        AddEditor(_linePath, left, right);
    }

    private void AddEditor(string? path, List<EditorLine> lines, List<EditorLine> right)
    {
        if (lines.Count == 0 && right.Count == 0)
            return;
        AddRow(new DiffEditorRow
        {
            Path = path,
            SideBySide = right.Count > 0,
            Lines = lines,
            RightLines = right,
        });
    }

    private static List<EditorLine> FoldEditorLines(
        string? text,
        string oldNumber,
        string newNumber,
        string meta,
        EditorLineKind kind,
        bool showAction,
        string actionLabel,
        ICommand command,
        bool skip = false)
    {
        if (text is null || skip)
            return [new EditorLine { Kind = EditorLineKind.Empty, SkipCopy = true }];

        var count = LineFold.Count(text);
        var lines = new List<EditorLine>(count);
        for (var index = 0; index < count; index++)
        {
            lines.Add(new EditorLine
            {
                Text = LineFold.Piece(text, index) ?? "",
                OldNumber = index == 0 ? oldNumber : "",
                NewNumber = index == 0 ? newNumber : "",
                Meta = index == 0 ? meta : "",
                Kind = kind,
                Continues = index > 0,
                ShowAction = index == 0 && showAction,
                ActionLabel = index == 0 ? actionLabel : "",
                ActionCommand = index == 0 ? command : UiCommands.Disabled,
            });
        }

        return lines;
    }

    private static void PadEditorLines(List<EditorLine> lines, int count)
    {
        while (lines.Count < count)
            lines.Add(new EditorLine { Kind = EditorLineKind.Empty, SkipCopy = true, Continues = true });
    }

    private static bool CanStageParts(bool workingCopy, ChangeKind kind, DiffDocument document) =>
        workingCopy
        && !document.IsRename
        && !document.IsBinary
        && !string.IsNullOrEmpty(document.RawPatch)
        && kind is ChangeKind.Modified or ChangeKind.Added or ChangeKind.Untracked or ChangeKind.Deleted;

    private static ChangeKind KindForDiff(DiffDocument document)
    {
        if (document.IsNewFile)
            return ChangeKind.Added;
        if (document.IsDeleted)
            return ChangeKind.Deleted;
        return ChangeKind.Modified;
    }

    private async Task ApplyShownHunkAsync(string patch, int index)
    {
        if (_session is null || string.IsNullOrEmpty(patch))
            return;
        var reverse = _viewingStaged;
        await RunAsync(reverse ? "Unstaging hunk…" : "Staging hunk…", ct => _session.ApplyHunkAsync(patch, index, reverse, ct));
    }

    private async Task ApplyShownLineAsync(string patch, int hunkIndex, int lineIndex)
    {
        if (_session is null || string.IsNullOrEmpty(patch))
            return;
        var reverse = _viewingStaged;
        try
        {
            await RunAsync(reverse ? "Unstaging line…" : "Staging line…", ct => _session.ApplyLineAsync(patch, hunkIndex, lineIndex, reverse, ct));
        }
        catch (InvalidOperationException exception)
        {
            Fail(exception.Message);
        }
    }

    private void AddRow(DiffRow row)
    {
        if (_rowSink is not null)
            _rowSink.Add(row);
        else
            DiffRows.Add(row);
    }

    private void SetExpanded(DiffSection section, bool expanded)
    {
        if (section.Header.Expanded == expanded)
            return;
        var index = DiffRows.IndexOf(section.Header);
        section.Header.Expanded = expanded;
        SetFold(section.Key, expanded);
        if (index >= 0)
        {
            if (expanded)
            {
                for (var i = 0; i < section.Body.Count; i++)
                    DiffRows.Insert(index + 1 + i, section.Body[i]);
            }
            else
            {
                for (var i = 0; i < section.Body.Count; i++)
                    DiffRows.RemoveAt(index + 1);
            }
        }

        ApplyImageFolds();
        if (expanded)
            RequestBlame(section);
    }

    private void ToggleFileSection(string path)
    {
        foreach (var section in _sections)
        {
            if (!SectionMatches(section, path, null))
                continue;
            SetExpanded(section, !section.Header.Expanded);
            return;
        }
    }

    private void OpenForJump(string path, string? original)
    {
        SetFold(path, true);
        if (original is { Length: > 0 } old)
            SetFold(old, true);
        foreach (var section in _sections)
        {
            if (!SectionMatches(section, path, original))
                continue;
            SetExpanded(section, true);
            return;
        }

        ApplyImageFolds();
    }

    private static bool SectionMatches(DiffSection section, string path, string? original) =>
        DiffParser.SameFile(section.Key, path)
        || DiffParser.SameFile(section.Header.Path, path)
        || (original is { Length: > 0 } old
            && (DiffParser.SameFile(section.Key, old) || DiffParser.SameFile(section.Header.Path, old)));

    // The hotkey stays registered while the command bar is hidden, so this is what keeps the keys from running.
    private bool CanFoldSections() => ShowSectionFolds;

    [RelayCommand(CanExecute = nameof(CanFoldSections))]
    private void ExpandAllSections()
    {
        if (_sections.Count == 0)
            return;
        _foldsOpen = true;
        _foldExceptions.Clear();
        foreach (var section in _sections)
            section.Header.Expanded = true;
        PublishSections();
        ApplyImageFolds();
        if (!ShowingBlame)
            return;
        foreach (var section in _sections)
            RequestBlame(section, showLoading: false);
    }

    [RelayCommand(CanExecute = nameof(CanFoldSections))]
    private void CollapseAllSections()
    {
        if (_sections.Count == 0)
            return;
        _foldsOpen = false;
        _foldExceptions.Clear();
        foreach (var section in _sections)
            section.Header.Expanded = false;

        PublishSections();
        ApplyImageFolds();
    }

    private void PublishSections()
    {
        var rows = new List<DiffRow>();
        foreach (var section in _sections)
        {
            rows.Add(section.Header);
            if (section.Header.Expanded)
                rows.AddRange(section.Body);
        }

        DiffRows.Clear();
        foreach (var row in rows)
            DiffRows.Add(row);
    }

    private void ClearSections()
    {
        if (_sections.Count == 0)
            return;
        _sections.Clear();
        NoteSectionFolds();
    }

    private void ApplyImageFolds()
    {
        foreach (var row in ImageCompares)
            row.IsOpen = !IsCollapsed(row.Path);
    }

    private void NoteChangelist()
    {
        var scope = FoldScope();
        if (string.Equals(scope, _foldScope, StringComparison.Ordinal))
            return;
        _foldScope = scope;
        _foldsOpen = false;
        _foldExceptions.Clear();
    }

    /// <summary>Working copy, index, one commit, and a commit range each start collapsed.</summary>
    private string FoldScope()
    {
        if (_rangeOlder is not null && _rangeNewer is not null)
            return "range\n" + _rangeOlder + "\n" + _rangeNewer;
        var row = SelectedGraphRow;
        if (row is null || row.IsWorkingCopy)
            return _viewingStaged ? "staged" : "unstaged";
        return "commit\n" + (row.Sha ?? "") + "\n" + (_diffParent ?? "");
    }

    private void SetFold(string key, bool open)
    {
        if (key.Length == 0)
            return;
        if (open == _foldsOpen)
            RemoveFold(key);
        else
            _foldExceptions.Add(key);
    }

    private void RemoveFold(string key)
    {
        List<string>? drop = null;
        foreach (var item in _foldExceptions)
        {
            if (string.Equals(item, key, StringComparison.Ordinal) || DiffParser.SameFile(item, key))
                (drop ??= []).Add(item);
        }

        if (drop is null)
            return;
        foreach (var item in drop)
            _foldExceptions.Remove(item);
    }

    private bool IsFoldOpen(string key)
    {
        if (key.Length == 0)
            return true;
        var exception = _foldExceptions.Contains(key);
        if (!exception)
        {
            foreach (var item in _foldExceptions)
            {
                if (DiffParser.SameFile(item, key))
                {
                    exception = true;
                    break;
                }
            }
        }

        return _foldsOpen ? !exception : exception;
    }

    private bool IsCollapsed(string path)
    {
        if (path.Length == 0)
            return false;
        return !IsFoldOpen(path);
    }

    private void ClearDiff(string notice)
    {
        ClearSections();
        ClearMerge();
        ClearPreview();
        ResetBlameQueue();
        DiffRows.Clear();
        _rawPatch = null;
        ShowLoadDiff = false;
        DiffNotice = notice;
        HasDiffNotice = notice.Length > 0;
    }

    private void ReplaceDetails()
    {
        _details?.Cancel();
        _details?.Dispose();
        _details = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
    }

    private void UpdateCommands(SessionState state)
    {
        var signature = state.Commands.Count == 0
            ? ""
            : state.Commands.Count + ":" + state.Commands[^1].At.ToUnixTimeMilliseconds();
        if (signature == _commandSignature)
            return;
        _commandSignature = signature;
        CommandLog = FormatCommandLog(state.Commands);
    }

    private static string FormatCommandLog(IReadOnlyList<CommandLogEntry> commands)
    {
        if (commands.Count == 0)
            return "";
        var builder = new StringBuilder();
        foreach (var entry in commands.TakeLast(40))
        {
            if (builder.Length > 0)
                builder.Append('\n');
            var arguments = string.Join(" ", entry.Arguments);
            builder.Append(entry.At.LocalDateTime.ToString("HH:mm:ss", CultureInfo.CurrentCulture));
            builder.Append("  ");
            builder.Append(entry.Duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture));
            builder.Append("ms  ");
            builder.Append(entry.ExitCode.ToString(CultureInfo.InvariantCulture));
            builder.Append("  ");
            builder.Append(arguments);
            var error = ArgumentRedactor.RedactText(entry.StandardError.Replace("\r\n", "\n").Replace('\r', '\n')).Trim();
            if (error.Length > 0)
            {
                builder.Append('\n');
                builder.Append(error);
            }
        }

        return builder.ToString();
    }

    private static string CommitDetail(CommitRecord commit, string refs, bool head)
    {
        var parts = new List<string>();
        if (commit.AuthorName.Length > 0)
            parts.Add(commit.AuthorName);
        parts.Add(Relative(commit.AuthorUnixSeconds));
        parts.Add(CommitDate(commit.AuthorUnixSeconds));
        var sha = Short(commit.Sha);
        if (sha.Length > 0)
            parts.Add(sha);
        if (head)
            parts.Add("HEAD");
        if (refs.Length > 0)
            parts.Add(refs);
        return string.Join("  ·  ", parts);
    }

    private static string CommitDate(long unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    private static string CommitStamp(long unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime.ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture);

    private static string AuthorLine(CommitRecord? commit)
    {
        if (commit is null)
            return "";
        if (commit.AuthorName.Length == 0)
            return commit.AuthorEmail;
        if (commit.AuthorEmail.Length == 0)
            return commit.AuthorName;
        return commit.AuthorName + " <" + commit.AuthorEmail + ">";
    }

    private void ShowCommitFields(string message, string sha, string author, string date)
    {
        CommitMessageText = message;
        CommitShaText = sha;
        CommitAuthorText = author;
        CommitDateText = date;
        ShowCommitIdentity = author.Length > 0 || date.Length > 0;
    }

    private void ClearCommitFields()
    {
        CommitMessageText = "";
        CommitShaText = "";
        CommitAuthorText = "";
        CommitDateText = "";
        ShowCommitIdentity = false;
        ShowCommitStats = false;
        CommitStatsFiles = "";
        CommitStatsRemoved = "";
        CommitStatsAdded = "";
    }

    private async Task ApplyCommitStatsAsync(string? older, string newer, CancellationToken token)
    {
        if (_session is null)
            return;
        try
        {
            var stat = await _session.DiffStatAsync(older, newer, token);
            if (!token.IsCancellationRequested)
            {
                CommitStatsFiles = DiffStatText.Files(stat.Files);
                CommitStatsRemoved = DiffStatText.Removed(stat.Removed);
                CommitStatsAdded = DiffStatText.Added(stat.Added);
                ShowCommitStats = true;
            }
        }
        catch (GitCommandFailedException)
        {
        }
    }

    private static string WorkingDetail(SessionState state)
    {
        var sequencer = state.Sequencer switch
        {
            SequencerKind.Rebase => "Rebase in progress",
            SequencerKind.CherryPick => "Cherry-pick in progress",
            SequencerKind.Revert => "Revert in progress",
            SequencerKind.Merge => "Merge in progress",
            _ => null,
        };
        if (sequencer is not null)
            return sequencer;
        if (state.Entries.Count == 0)
            return state.Branch.Unborn ? "No commits yet" : "Clean";
        var conflicts = state.Entries.Count(entry => entry.Kind == ChangeKind.Unmerged);
        var staged = state.Entries.Count(entry => entry.Staged && entry.Kind != ChangeKind.Unmerged);
        var unstaged = state.Entries.Count(entry => entry.Kind != ChangeKind.Unmerged && (entry.Unstaged || entry.Kind == ChangeKind.Untracked));
        var parts = new List<string>();
        if (conflicts > 0)
            parts.Add($"{conflicts} conflicted");
        if (staged > 0)
            parts.Add($"{staged} staged");
        if (unstaged > 0)
            parts.Add($"{unstaged} unstaged");
        return string.Join(", ", parts);
    }

    private static string HeadLabel(BranchHeader branch) =>
        branch.Detached || string.IsNullOrEmpty(branch.HeadName) ? "HEAD" : branch.HeadName;

    private static string DescribeBranch(BranchHeader branch)
    {
        if (branch.Detached)
            return "detached " + Short(branch.Oid);
        if (string.IsNullOrEmpty(branch.HeadName))
            return branch.Unborn ? "No branch" : "HEAD";
        return branch.HeadName;
    }

    private Task ToggleHiddenAsync(string refName)
    {
        if (_session is null)
            return Task.CompletedTask;
        var next = new HashSet<string>(_session.Snapshot().HiddenBranches, StringComparer.Ordinal);
        if (!next.Remove(refName))
            next.Add(refName);
        return CommitHiddenAsync(next);
    }

    private Task HideOthersAsync(string refName)
    {
        if (_session is null)
            return Task.CompletedTask;
        var snapshot = _session.Snapshot();
        var next = BranchVisibility.HiddenExcept(snapshot.Refs, refName);
        foreach (var name in snapshot.HiddenBranches)
        {
            if (BranchVisibility.IsStashToken(name))
                next.Add(name);
        }

        return CommitHiddenAsync(next);
    }

    private Task ShowAllBranchesAsync()
    {
        if (_session is null)
            return Task.CompletedTask;
        var kept = _session.Snapshot().HiddenBranches.Where(BranchVisibility.IsStashToken).ToArray();
        return CommitHiddenAsync(kept);
    }

    private async Task CommitHiddenAsync(IReadOnlyCollection<string> hidden)
    {
        if (_session is null)
            return;
        var ok = await RunAsync("Loading history…", ct => _session.SetHiddenBranchesAsync(hidden, ct));
        if (!ok || _session is null)
            return;
        UseHiddenBranches(_session.Snapshot().HiddenBranches);
        _host.Save();
    }

    private static string RefLabel(string sha, IReadOnlyList<GitRef> refs, IReadOnlySet<string> hidden)
    {
        var names = new List<string>();
        foreach (var reference in refs)
        {
            if (hidden.Contains(reference.Name))
                continue;
            if (!string.Equals(reference.Oid, sha, StringComparison.OrdinalIgnoreCase))
                continue;
            if (reference.Name.StartsWith("refs/tags/", StringComparison.Ordinal))
                continue;
            if (reference.Name.StartsWith("refs/remotes/", StringComparison.Ordinal)
                && reference.Name.EndsWith("/HEAD", StringComparison.Ordinal))
                continue;
            names.Add(ShortRef(reference.Name));
        }

        if (names.Count == 0)
            return "";
        if (names.Count <= 4)
            return string.Join("  ", names);
        return string.Join("  ", names.Take(4)) + $"  +{names.Count - 4}";
    }

    private static IReadOnlyList<string> TagNames(string sha, IReadOnlyList<GitRef> refs, IReadOnlySet<string> hidden)
    {
        var names = new List<string>();
        foreach (var reference in refs)
        {
            if (!reference.Name.StartsWith("refs/tags/", StringComparison.Ordinal))
                continue;
            if (hidden.Contains(reference.Name))
                continue;
            if (!string.Equals(reference.Oid, sha, StringComparison.OrdinalIgnoreCase))
                continue;
            names.Add(reference.Name["refs/tags/".Length..]);
        }

        if (names.Count <= 4)
            return names;
        var shown = names.Take(4).ToList();
        shown.Add($"+{names.Count - 4}");
        return shown;
    }

    private static string ShortRef(string name)
    {
        if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
            return name["refs/heads/".Length..];
        if (name.StartsWith("refs/remotes/", StringComparison.Ordinal))
            return name["refs/remotes/".Length..];
        if (name.StartsWith("refs/tags/", StringComparison.Ordinal))
            return name["refs/tags/".Length..];
        if (name == "refs/stash")
            return "stash";
        return name;
    }

    private static string ShortHead(string name) =>
        name.StartsWith("refs/heads/", StringComparison.Ordinal) ? name["refs/heads/".Length..] : name;

    private static string ShortRemote(string name) =>
        name.StartsWith("refs/remotes/", StringComparison.Ordinal) ? name["refs/remotes/".Length..] : name;

    private static string RemoteGroup(string name)
    {
        var rest = ShortRemote(name);
        var slash = rest.IndexOf('/');
        return slash < 0 ? rest : rest[..slash];
    }

    private static string Short(string? oid) =>
        string.IsNullOrEmpty(oid) ? "" : oid.Length <= 7 ? oid : oid[..7];

    private static string Letter(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "A",
        ChangeKind.Deleted => "D",
        ChangeKind.Renamed => "R",
        ChangeKind.Copied => "C",
        ChangeKind.Unmerged => "U",
        ChangeKind.Untracked => "?",
        ChangeKind.TypeChanged => "T",
        _ => "M",
    };

    private static string Relative(long unixSeconds)
    {
        var delta = DateTimeOffset.Now - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (delta.TotalMinutes < 1)
            return "just now";
        if (delta.TotalHours < 1)
            return $"{(int)delta.TotalMinutes}m";
        if (delta.TotalDays < 1)
            return $"{(int)delta.TotalHours}h";
        if (delta.TotalDays < 30)
            return $"{(int)delta.TotalDays}d";
        if (delta.TotalDays < 365)
            return $"{(int)(delta.TotalDays / 30)}mo";
        return $"{(int)(delta.TotalDays / 365)}y";
    }

    private static string EntrySignature(IReadOnlyList<StatusEntry> entries)
    {
        var builder = new StringBuilder();
        foreach (var entry in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
            builder.Append(entry.Path).Append(entry.IndexStatus).Append(entry.WorkTreeStatus).Append((int)entry.Kind).Append('|');
        return builder.ToString();
    }

    private static string RefSignature(SessionState state)
    {
        var builder = new StringBuilder();
        foreach (var reference in state.Refs.OrderBy(reference => reference.Name, StringComparer.Ordinal))
            builder.Append(reference.Name).Append('=').Append(reference.Oid).Append(reference.IsHead ? '*' : ' ').Append(reference.Upstream).Append('|');
        foreach (var remote in state.Remotes)
            builder.Append(remote).Append(';');
        foreach (var stash in state.Stashes)
            builder.Append(stash.Ref).Append('=').Append(stash.Sha).Append('|');
        foreach (var module in state.Submodules)
            builder.Append('u').Append(module.Path).Append(module.Sha).Append((int)module.State).Append(module.Describe).Append('|');
        foreach (var tree in state.Worktrees)
            builder.Append('w').Append(tree.Path).Append(tree.Head).Append(tree.Branch).Append(tree.Detached ? 'd' : ' ').Append('|');
        foreach (var name in state.HiddenBranches.OrderBy(name => name, StringComparer.Ordinal))
            builder.Append('h').Append(name).Append('|');
        return builder.ToString();
    }

    private Task CopyText(string text) => _host.Dialogs is null ? Task.CompletedTask : _host.Dialogs.CopyAsync(text);

    private WorktreeFileMenu? FileMenuFor(string relative, bool? lfsTracked = null)
    {
        var name = DesktopOpen.FileName(relative);
        if (name is null)
            return null;
        var full = string.IsNullOrEmpty(Toplevel) ? null : DesktopOpen.FullPath(Toplevel, relative);
        var tracked = lfsTracked ?? MarkedLfs(relative);
        var actions = ViewingWorktree();
        return new WorktreeFileMenu
        {
            OpenFolderLabel = DesktopOpen.FolderLabel(DesktopOpen.Current),
            CopyFileNameCommand = new RelayCommand(() => _ = CopyText(name)),
            CopyPathCommand = new RelayCommand(() => _ = CopyText(relative)),
            CopyFullPathCommand = full is null ? UiCommands.Disabled : new RelayCommand(() => _ = CopyText(full)),
            OpenFolderCommand = full is null ? UiCommands.Disabled : new RelayCommand(() => OpenFolder(full)),
            OpenEditorCommand = full is null ? UiCommands.Disabled : new RelayCommand(() => OpenEditor(full)),
            ShowLfsTrack = actions && !tracked,
            ShowLfsUntrack = actions && tracked,
            ShowLfsDownload = actions && tracked,
            LfsTrackCommand = new AsyncRelayCommand(() => TrackWithLfs(relative)),
            LfsUntrackCommand = new AsyncRelayCommand(() => UntrackLfs(relative)),
            LfsDownloadCommand = new AsyncRelayCommand(() => DownloadLfs(relative)),
        };
    }

    private void UseViewLfs(IEnumerable<string> paths)
    {
        _viewLfs.Clear();
        foreach (var path in paths)
            _viewLfs.Add(path);
    }

    private bool PathIsLfs(string? path) => path is { Length: > 0 } && _viewLfs.Contains(path);

    private bool MarkedLfs(string path)
    {
        if (PathIsLfs(path))
            return true;
        foreach (var row in Files)
        {
            if (!row.IsHeader && row.LfsTracked && (row.Path == path || row.OriginalPath == path))
                return true;
        }

        return false;
    }

    private bool ViewingWorktree() =>
        _rangeOlder is null && (SelectedGraphRow is null || SelectedGraphRow.IsWorkingCopy);

    private async Task<IReadOnlySet<string>> LfsMarksAsync(IReadOnlyList<CommitFileChange> files, string? source, CancellationToken token)
    {
        if (_session is null || files.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);
        var paths = new List<string>(files.Count);
        foreach (var change in files)
        {
            if (change.Path.Length > 0)
                paths.Add(change.Path);
            if (change.OriginalPath is { Length: > 0 } original)
                paths.Add(original);
        }

        try
        {
            return await _session.LfsTrackedAsync(paths, source, token);
        }
        catch (GitCommandFailedException exception)
        {
            Fail(exception.Message);
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private void OpenFolder(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
                DesktopOpen.Start(DesktopOpen.RevealFile(DesktopOpen.Current, fullPath));
            else if (DesktopOpen.NearestDirectory(fullPath) is { } folder)
                DesktopOpen.Start(DesktopOpen.OpenDirectory(DesktopOpen.Current, folder));
            else
                Fail("That file is not in the working tree.");
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
    }

    private void OpenEditor(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            Fail("That file is not in the working tree.");
            return;
        }

        try
        {
            DesktopOpen.Start(DesktopOpen.EditFile(DesktopOpen.Current, fullPath));
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
    }

    private sealed class DiffSection
    {
        public DiffSection(string key, DiffFileRow header)
        {
            Key = key;
            Header = header;
        }

        public string Key { get; }

        public DiffFileRow Header { get; }

        public List<DiffRow> Body { get; } = [];

        public bool BlamePending { get; set; }

        public bool BlameReady { get; set; }
    }
}
