using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant;
using Sextant.Git;
using Sextant.Git.Parsing;
using System.Globalization;
using System.Text;

namespace Sextant.ViewModels;

public partial class RepositoryViewModel
{
    private string? _rangeOlder;
    private string? _rangeNewer;
    private string _rangeOlderSubject = "";
    private string _rangeNewerSubject = "";

    [ObservableProperty]
    public partial string HistoryText { get; set; } = "";

    [ObservableProperty]
    public partial string HistoryCaption { get; set; } = "";

    [ObservableProperty]
    public partial bool HasHistoryFilter { get; set; }

    [ObservableProperty]
    public partial bool ShowHistorySearch { get; set; }

    public bool ShowHistoryChrome => ShowHistorySearch || HasHistoryFilter;

    [ObservableProperty]
    public partial string ConflictText { get; set; } = "";

    [ObservableProperty]
    public partial bool IgnoreWhitespace { get; set; }

    [ObservableProperty]
    public partial bool SideBySide { get; set; }

    [ObservableProperty]
    public partial bool AllFiles { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowingBlame { get; set; }

    [ObservableProperty]
    public partial bool ShowingMerge { get; set; }

    [ObservableProperty]
    public partial bool ShowMergeBase { get; set; }

    [ObservableProperty]
    public partial MergeSession? MergeEdit { get; set; }

    [ObservableProperty]
    public partial string MergePath { get; set; } = "";

    [ObservableProperty]
    public partial int MergeConflictIndex { get; set; }

    public bool ShowingDiff => !ShowingBlame && !ShowingMerge;

    public bool ShowingRows => !ShowingMerge;

    /// <summary>The Diff tab stays selected while a merge is open. Blame is the other tab.</summary>
    public bool DiffTabOn => !ShowingBlame;

    public string SideBySideLabel => SideBySide ? "Inline" : "Side by side";

    public string MergeBaseLabel => ShowMergeBase ? "Hide base" : "Show base";

    public string MergeConflictLabel => MergeEdit is { ConflictCount: > 0 } edit
        ? $"Conflict {MergeConflictIndex + 1} of {edit.ConflictCount}"
        : "No conflicts in this file";

    public string WhitespaceLabel => IgnoreWhitespace ? "Show whitespace" : "Ignore whitespace";

    public string FilesModeLabel => AllFiles ? "Selected file" : "All files";

    partial void OnSideBySideChanged(bool value) => OnPropertyChanged(nameof(SideBySideLabel));

    partial void OnIgnoreWhitespaceChanged(bool value) => OnPropertyChanged(nameof(WhitespaceLabel));

    partial void OnAllFilesChanged(bool value)
    {
        OnPropertyChanged(nameof(FilesModeLabel));
        OnPropertyChanged(nameof(ShowSectionFolds));
    }

    partial void OnShowingBlameChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowingDiff));
        OnPropertyChanged(nameof(DiffTabOn));
        OnPropertyChanged(nameof(ShowSectionFolds));
    }

    partial void OnShowingMergeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowingDiff));
        OnPropertyChanged(nameof(ShowingRows));
        OnPropertyChanged(nameof(ShowSectionFolds));
    }

    partial void OnHasHistoryFilterChanged(bool value) => OnPropertyChanged(nameof(ShowHistoryChrome));

    partial void OnShowHistorySearchChanged(bool value) => OnPropertyChanged(nameof(ShowHistoryChrome));

    [RelayCommand]
    private void ToggleHistorySearch() => ShowHistorySearch = !ShowHistorySearch;

    partial void OnShowMergeBaseChanged(bool value) => OnPropertyChanged(nameof(MergeBaseLabel));

    partial void OnMergeEditChanged(MergeSession? oldValue, MergeSession? newValue)
    {
        if (oldValue is not null)
            oldValue.Changed -= OnMergeEdited;
        if (newValue is not null)
            newValue.Changed += OnMergeEdited;
        MergeConflictIndex = 0;
        RefreshMergeCommands();
    }

    partial void OnMergeConflictIndexChanged(int value) => RefreshMergeCommands();

    private void OnMergeEdited()
    {
        if (MergeEdit is { } edit && (MergeConflictIndex < 0 || MergeConflictIndex >= edit.ConflictCount))
            MergeConflictIndex = edit.ConflictCount == 0 ? 0 : edit.ConflictCount - 1;
        RefreshMergeCommands();
    }

    private void RefreshMergeCommands()
    {
        OnPropertyChanged(nameof(MergeConflictLabel));
        PreviousConflictCommand.NotifyCanExecuteChanged();
        NextConflictCommand.NotifyCanExecuteChanged();
        TakeOursCommand.NotifyCanExecuteChanged();
        TakeTheirsCommand.NotifyCanExecuteChanged();
    }

    private bool CanTakeConflict() => MergeEdit is { ConflictCount: > 0 } edit
        && (uint)MergeConflictIndex < (uint)edit.ConflictCount;

    private bool CanPreviousConflict() => CanTakeConflict() && MergeConflictIndex > 0;

    private bool CanNextConflict() => MergeEdit is { } edit && MergeConflictIndex < edit.ConflictCount - 1;

    [RelayCommand]
    private void ToggleMergeBase() => ShowMergeBase = !ShowMergeBase;

    [RelayCommand(CanExecute = nameof(CanPreviousConflict))]
    private void PreviousConflict() => MergeConflictIndex--;

    [RelayCommand(CanExecute = nameof(CanNextConflict))]
    private void NextConflict() => MergeConflictIndex++;

    [RelayCommand(CanExecute = nameof(CanTakeConflict))]
    private void TakeOurs() => MergeEdit?.TakeOurs(MergeConflictIndex);

    [RelayCommand(CanExecute = nameof(CanTakeConflict))]
    private void TakeTheirs() => MergeEdit?.TakeTheirs(MergeConflictIndex);

    [RelayCommand]
    public async Task SaveConflict()
    {
        if (_session is null || !ShowingMerge || string.IsNullOrEmpty(MergePath) || MergeEdit is null)
            return;
        var text = MergeEdit.TextForDisk();
        var path = MergePath;
        await RunAsync("Saving resolution…", ct => _session.SaveResolutionAsync(path, text, ct));
    }

    public void NoteGraphSelection(IReadOnlyList<GraphRowViewModel> rows)
    {
        if (_applying || _session is null)
            return;
        var commits = rows.Where(row => !row.IsWorkingCopy && row.Sha is not null).ToList();
        _selectedShas.Clear();
        _selectedShas.AddRange(commits.Select(row => row.Sha!));
        if (commits.Count >= 2)
        {
            var ordered = commits
                .Select(row => (Row: row, Index: Rows.IndexOf(row)))
                .Where(item => item.Index >= 0)
                .OrderByDescending(item => item.Index)
                .Select(item => item.Row)
                .ToList();
            if (ordered.Count < 2)
                return;
            _rangeOlder = ordered[0].Sha;
            _rangeNewer = ordered[^1].Sha;
            _rangeOlderSubject = ordered[0].Subject;
            _rangeNewerSubject = ordered[^1].Subject;
            _ = LoadRangeAsync();
            return;
        }

        var wasRange = _rangeOlder is not null;
        _rangeOlder = null;
        _rangeNewer = null;
        if (wasRange)
            _ = LoadDetailsAsync();
    }

    [RelayCommand]
    private Task SearchHistory()
    {
        var query = HistoryQueryParser.Parse(HistoryText);
        return RunAsync("Searching…", ct => _session!.SetHistoryAsync(query.IsEmpty ? null : query, ct));
    }

    [RelayCommand]
    private Task ClearHistory()
    {
        HistoryText = "";
        return RunAsync("Loading history…", ct => _session!.SetHistoryAsync(null, ct));
    }

    [RelayCommand]
    private Task Stash() => StashAsync();

    [RelayCommand]
    private Task AddRemote() => AddRemoteAsync();

    public void ApplyDiffPreferences(bool sideBySide, bool ignoreWhitespace)
    {
        var changed = SideBySide != sideBySide || IgnoreWhitespace != ignoreWhitespace;
        SideBySide = sideBySide;
        IgnoreWhitespace = ignoreWhitespace;
        if (changed && _session is not null)
            _ = ReloadDiffViewAsync();
    }

    [RelayCommand]
    private Task ToggleWhitespace()
    {
        IgnoreWhitespace = !IgnoreWhitespace;
        return ReloadDiffViewAsync();
    }

    [RelayCommand]
    private Task ToggleSideBySide()
    {
        SideBySide = !SideBySide;
        return ReloadDiffViewAsync();
    }

    [RelayCommand]
    private Task ToggleAllFiles()
    {
        AllFiles = !AllFiles;
        return ReloadDiffViewAsync();
    }

    [RelayCommand]
    private Task ShowDiff()
    {
        if (!ShowingBlame)
            return Task.CompletedTask;
        ShowingBlame = false;
        return LoadDiffAsync();
    }

    [RelayCommand]
    private Task ShowBlame()
    {
        if (ShowingBlame)
            return Task.CompletedTask;
        ShowingBlame = true;
        return LoadDiffAsync();
    }

    [RelayCommand]
    private Task ToggleBlame()
    {
        ShowingBlame = !ShowingBlame;
        return LoadDiffAsync();
    }

    private Task ShowFileHistoryAsync(string path) =>
        RunAsync("Loading file history…", ct => _session!.SetHistoryAsync(HistoryQuery.ForPath(path), ct));

    private Task StashAsync()
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var message = await dialogs.PromptAsync("Stash", "Stash message. Leave it blank to let git describe the stash.", allowEmpty: true);
            if (message is null || _session is null)
                return;
            await RunAsync("Stashing…", ct => _session.StashPushAsync(message, ct));
        });
    }

    private Task AddRemoteAsync()
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var name = await dialogs.PromptAsync("Add remote", "Remote name");
            if (string.IsNullOrWhiteSpace(name) || _session is null)
                return;
            var url = await dialogs.PromptAsync("Add remote", "Remote URL or path");
            if (string.IsNullOrWhiteSpace(url) || _session is null)
                return;
            await RunAsync("Adding remote…", ct => _session.AddRemoteAsync(name, url, ct));
        });
    }

    private Task RemoveRemoteAsync(string name)
    {
        return ConfirmRun("Remove remote", $"Remove remote {name}?", "Remove", "Removing remote…", ct => _session!.RemoveRemoteAsync(name, ct));
    }

    private Task RenameRemoteAsync(string name)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var next = await dialogs.PromptAsync("Rename remote", $"New name for {name}", name);
            if (string.IsNullOrWhiteSpace(next) || next == name || _session is null)
                return;
            await RunAsync("Renaming remote…", ct => _session.RenameRemoteAsync(name, next, ct));
        });
    }

    private Task DeleteTagAsync(string name) =>
        ConfirmRun("Delete tag", $"Delete tag {name}?", "Delete", "Deleting tag…", ct => _session!.DeleteTagAsync(name, ct));

    private Task CreateBranchFromTagAsync(string tag)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var name = await dialogs.PromptAsync("Create branch", $"Branch name from {tag}");
            if (string.IsNullOrWhiteSpace(name) || _session is null)
                return;
            await RunAsync("Creating branch…", ct => _session.CreateBranchAtAsync(name, tag, ct));
        });
    }

    private Task CheckoutTagAsync(string tag) =>
        RunAsync("Checking out tag…", ct => _session!.SwitchDetachAsync(tag, ct));

    private Task PushTagAsync(string tag)
    {
        if (_session is null || IsBusy)
            return Task.CompletedTask;
        var progress = Progress();
        return RunAsync("Pushing tag…", ct => _session.PushTagAsync("origin", tag, progress, ct));
    }

    private Task DeleteRemoteTagAsync(string tag)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        var progress = Progress();
        return HoldFocus(async () =>
        {
            var ok = await dialogs.ConfirmAsync("Delete tag from origin", $"Delete tag {tag} from origin?", "Delete");
            if (!ok || _session is null)
                return;
            await RunAsync("Deleting tag…", ct => _session.DeleteRemoteTagAsync("origin", tag, progress, ct));
        });
    }

    private Task PopStashAsync(StashEntry stash) =>
        ConfirmRun("Pop stash", $"Pop {stash.Ref}? Git will try to apply it and then drop it.", "Pop", "Popping stash…", ct => _session!.StashPopAsync(stash.Ref, ct));

    private Task ApplyStashAsync(StashEntry stash) =>
        RunAsync("Applying stash…", ct => _session!.StashApplyAsync(stash.Ref, ct));

    private Task DropStashAsync(StashEntry stash) =>
        ConfirmRun("Drop stash", $"Drop {stash.Ref}? The stashed changes will be discarded.", "Drop", "Dropping stash…", ct => _session!.StashDropAsync(stash.Ref, ct));

    private Task ResetAsync(CommitRecord commit, string mode)
    {
        if (mode == "--hard")
        {
            var head = _session?.Snapshot().Branch;
            var discarded = head?.Oid is { } oid && !string.Equals(oid, commit.Sha, StringComparison.OrdinalIgnoreCase)
                ? $" Commit {Short(oid)} will be discarded."
                : "";
            return ConfirmRun(
                "Hard reset",
                $"Hard reset to {Short(commit.Sha)} {commit.Subject}? Uncommitted changes will be discarded.{discarded}",
                "Reset hard",
                "Resetting…",
                ct => _session!.ResetAsync(mode, commit.Sha, ct));
        }

        var label = mode == "--soft" ? "Resetting soft…" : "Resetting mixed…";
        return RunAsync(label, ct => _session!.ResetAsync(mode, commit.Sha, ct));
    }

    private Task CherryPickAsync(CommitRecord commit) =>
        RunAsync("Cherry-picking…", ct => _session!.CherryPickAsync(commit.Sha, ct));

    private Task RevertAsync(CommitRecord commit) =>
        RunAsync("Reverting…", ct => _session!.RevertAsync(commit.Sha, ct));

    private Task CreateBranchAtAsync(CommitRecord commit)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var name = await dialogs.PromptAsync("Create branch", $"Branch name at {Short(commit.Sha)}");
            if (string.IsNullOrWhiteSpace(name) || _session is null)
                return;
            await RunAsync("Creating branch…", ct => _session.CreateBranchAtAsync(name, commit.Sha, ct));
        });
    }

    [RelayCommand]
    private Task ApplyPatch()
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var path = await dialogs.PickFileAsync("Apply patch", "Patch", ["*.patch", "*.diff"]);
            if (string.IsNullOrWhiteSpace(path) || _session is null)
                return;
            await RunAsync("Applying patch…", ct => _session.ApplyPatchFileAsync(path, ct));
        });
    }

    private Task SavePatchAsync(CommitRecord commit)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var path = await dialogs.SaveFileAsync("Patch", PatchFileName(commit));
            if (string.IsNullOrWhiteSpace(path) || _session is null)
                return;
            await RunAsync("Saving patch…", async ct =>
            {
                var bytes = await _session.FormatPatchAsync(commit.Sha, ct);
                try
                {
                    await File.WriteAllBytesAsync(path, bytes, ct);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new RepositoryActionException(exception.Message);
                }
            });
        });
    }

    private static string PatchFileName(CommitRecord commit)
    {
        var cleaned = new StringBuilder(commit.Subject.Length);
        foreach (var character in commit.Subject.Trim())
            cleaned.Append(character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' or '\0' ? '-' : character);
        var name = cleaned.ToString().Trim();
        if (name.Length > 60)
            name = name[..60].Trim();
        if (name.Length == 0)
            name = commit.Sha.Length <= 10 ? commit.Sha : commit.Sha[..10];
        return name + ".patch";
    }

    private Task TagAsync(CommitRecord commit)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var name = await dialogs.PromptAsync("Create tag", $"Tag name for {Short(commit.Sha)}");
            if (string.IsNullOrWhiteSpace(name) || _session is null)
                return;
            await RunAsync("Tagging…", ct => _session.CreateTagAsync(name, commit.Sha, ct));
        });
    }

    private Task ConfirmRun(string title, string message, string confirm, string busy, Func<CancellationToken, Task> action)
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            var ok = await dialogs.ConfirmAsync(title, message, confirm);
            if (!ok || _session is null)
                return;
            await RunAsync(busy, action);
        });
    }

    private Task ReloadDiffViewAsync()
    {
        if (ShowingBlame)
            ShowingBlame = false;
        return LoadDiffAsync();
    }

    private async Task LoadRangeAsync()
    {
        if (_session is null || _rangeOlder is null || _rangeNewer is null || _lifetime.IsCancellationRequested)
            return;
        ShowingWorkingCopy = false;
        ShowingCommit = true;
        if (ShowingBlame)
            ShowingBlame = false;
        CommitTitle = Short(_rangeOlder) + ".." + Short(_rangeNewer);
        CommitMeta = _rangeOlderSubject + "  →  " + _rangeNewerSubject;
        _shownSha = null;
        ReplaceDetails();
        var token = _details!.Token;
        try
        {
            var files = await _session.RangeFilesAsync(_rangeOlder, _rangeNewer, token);
            if (files is null || token.IsCancellationRequested)
                return;
            ShowCommitFiles(files);
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

    private Task LoadBlameAsync(bool armJump, string? jumpPath, string? jumpOriginal)
    {
        if (_lifetime.IsCancellationRequested)
            return Task.CompletedTask;
        ClearMerge();
        if (_session is null)
        {
            ClearDiff(AllFiles ? "No files to blame." : "Select a file to blame.");
            return Task.CompletedTask;
        }

        var files = BlameFiles();
        if (files.Count == 0)
        {
            ClearDiff(AllFiles ? "No files to blame." : "Select a file to blame.");
            _allFilesShown = false;
            _diffReady = true;
            return Task.CompletedTask;
        }

        ResetBlameQueue();
        ReplaceDetails();
        _blameRevision = _rangeNewer ?? (SelectedGraphRow is { IsWorkingCopy: false, Sha: { } sha } ? sha : null);
        ClearSections();
        ClearPreview();
        DiffRows.Clear();
        _rawPatch = null;
        ShowLoadDiff = false;
        HasDiffNotice = false;
        DiffNotice = "";
        if (AllFiles)
            NoteChangelist();

        foreach (var file in files)
        {
            var key = file.Path;
            var header = new DiffFileRow
            {
                Path = file.Path,
                Label = file.Path,
                CanFold = true,
                Expanded = AllFiles ? IsFoldOpen(key) : true,
                FileMenu = FileMenuFor(file.Path),
            };
            var section = new DiffSection(key, header);
            header.ToggleCommand = new RelayCommand(() => SetExpanded(section, !header.Expanded));
            _sections.Add(section);
            DiffRows.Add(header);
        }

        _allFilesShown = AllFiles;
        _diffReady = true;
        OnPropertyChanged(nameof(ShowSectionFolds));
        foreach (var section in _sections)
        {
            if (section.Header.Expanded)
                RequestBlame(section);
        }

        if (AllFiles && !armJump && SelectedFile is { IsHeader: false, Path.Length: > 0 } selected)
        {
            armJump = true;
            jumpPath = selected.Path;
            jumpOriginal = selected.OriginalPath;
        }

        if (armJump && AllFiles && jumpPath is { Length: > 0 })
        {
            OpenForJump(jumpPath, jumpOriginal);
            JumpToFile?.Invoke(jumpPath, jumpOriginal);
        }

        return Task.CompletedTask;
    }

    private List<FileRowViewModel> BlameFiles()
    {
        if (!AllFiles)
            return SelectedFile is { IsHeader: false, Path.Length: > 0 } file ? [file] : [];

        var files = new List<FileRowViewModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Files)
        {
            if (file.IsHeader || file.Path.Length == 0 || !seen.Add(file.Path))
                continue;
            files.Add(file);
        }

        return files;
    }

    private void ResetBlameQueue()
    {
        _blameGeneration++;
        _blameQueue.Clear();
    }

    private void RequestBlame(DiffSection section, bool showLoading = true)
    {
        if (!ShowingBlame || !section.Header.Expanded || section.BlameReady || section.BlamePending)
            return;
        section.BlamePending = true;
        if (showLoading)
            InsertBlameLoading(section);
        _blameQueue.Enqueue(section);
        if (!_blamePump)
            _ = PumpBlameAsync(_blameGeneration);
    }

    private async Task PumpBlameAsync(int generation)
    {
        if (_blamePump)
            return;
        _blamePump = true;
        try
        {
            while (generation == _blameGeneration && _blameQueue.Count > 0)
            {
                var section = _blameQueue.Dequeue();
                if (generation != _blameGeneration || !_sections.Contains(section) || !section.Header.Expanded)
                {
                    section.BlamePending = false;
                    section.Body.Clear();
                    continue;
                }

                if (section.Body.Count == 0)
                    InsertBlameLoading(section);
                var token = _details?.Token ?? CancellationToken.None;
                try
                {
                    await FillBlameSectionAsync(section, generation, token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    if (generation != _blameGeneration || !_sections.Contains(section))
                        continue;
                    section.BlamePending = false;
                    section.BlameReady = true;
                    ReplaceBlameBody(section, [new DiffLineRow { Text = exception.Message, Background = DiffColors.Clear }]);
                }
            }
        }
        finally
        {
            _blamePump = false;
            if (_blameQueue.Count > 0)
                _ = PumpBlameAsync(_blameGeneration);
        }
    }

    private async Task FillBlameSectionAsync(DiffSection section, int generation, CancellationToken token)
    {
        if (_session is null)
            return;
        BlameDocument document;
        try
        {
            document = await _session.ReadBlameAsync(_blameRevision, section.Key, _allowLarge, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (generation != _blameGeneration || token.IsCancellationRequested || !_sections.Contains(section))
            return;
        if (!section.Header.Expanded)
        {
            section.BlamePending = false;
            section.Body.Clear();
            return;
        }

        ReplaceBlameBody(section, BlameBody(section.Key, document));
        section.BlameReady = true;
        section.BlamePending = false;
        if (!document.IsTooLarge)
            return;
        ShowLoadDiff = true;
        HasDiffNotice = true;
        DiffNotice = "This blame is large. Load it only if you need the whole file.";
    }

    private static List<DiffRow> BlameBody(string path, BlameDocument document)
    {
        if (!string.IsNullOrEmpty(document.Error))
            return [new DiffLineRow { Text = document.Error, Background = DiffColors.Clear }];
        if (document.IsTooLarge)
            return [new DiffLineRow { Text = "This blame is large.", Background = DiffColors.Clear }];
        if (document.Lines.Count == 0)
            return [new DiffLineRow { Text = "This file has no lines.", Background = DiffColors.Clear }];

        var lines = new List<EditorLine>(document.Lines.Count);
        foreach (var line in document.Lines)
        {
            var who = line.Uncommitted ? "Not committed" : line.Author;
            var id = line.Sha.Length <= 7 ? line.Sha : line.Sha[..7];
            var number = line.Number.ToString(CultureInfo.InvariantCulture);
            lines.AddRange(FoldEditorLines(line.Text, "", number, id + "  " + who, EditorLineKind.Context, false, "", UiCommands.Disabled));
        }

        return
        [
            new DiffEditorRow
            {
                Path = path,
                Blame = true,
                Lines = lines,
            },
        ];
    }

    private void InsertBlameLoading(DiffSection section)
    {
        if (section.Body.Count > 0)
            return;
        var line = new BlameRow { Number = "", Meta = "", Text = "Loading…", Language = null };
        section.Body.Add(line);
        if (!section.Header.Expanded)
            return;
        var index = DiffRows.IndexOf(section.Header);
        if (index >= 0)
            DiffRows.Insert(index + 1, line);
    }

    private void ReplaceBlameBody(DiffSection section, IReadOnlyList<DiffRow> rows)
    {
        var old = section.Body.Count;
        var index = section.Header.Expanded ? DiffRows.IndexOf(section.Header) : -1;
        section.Body.Clear();
        section.Body.AddRange(rows);
        if (index < 0)
            return;
        for (var i = 0; i < old && index + 1 < DiffRows.Count; i++)
            DiffRows.RemoveAt(index + 1);
        for (var i = 0; i < section.Body.Count; i++)
            DiffRows.Insert(index + 1 + i, section.Body[i]);
    }
}
