using System.Text;
using Sextant.Git.Parsing;

namespace Sextant.Git;

public sealed partial class RepositorySession : IAsyncDisposable
{
    private readonly GitProcessRunner _runner;
    private readonly string _executable;
    private readonly RepositoryScheduler _scheduler = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _stateLock = new();
    private readonly LaneAssigner _lanes = new();
    private readonly RequestGate _diffGate = new();
    private readonly RequestGate _filesGate = new();
    private readonly List<CommandLogEntry> _commands = [];
    private string _toplevel = "";
    private string _gitDirectory = "";
    private Encoding _encoding = Encoding.UTF8;
    private BranchHeader _branch = new(null, null, false, true, null, 0, 0);
    private List<StatusEntry> _entries = [];
    private List<GitRef> _refs = [];
    private List<GraphCommit> _commits = [];
    private List<string> _remotes = [];
    private Dictionary<string, string> _config = new(StringComparer.OrdinalIgnoreCase);
    private bool _historyEnded;
    private bool _historyCapped;
    private int _historyGeneration = 1;
    private bool _merge;
    private SequencerKind _sequencer;
    private string? _mergeMessage;
    private HistoryQuery? _historyQuery;
    private readonly HashSet<string> _hiddenBranches = new(StringComparer.Ordinal);
    private bool _includeStash;
    private List<StashEntry> _stashes = [];
    private HashSet<string> _lfsPaths = new(StringComparer.Ordinal);
    private List<SubmoduleEntry> _submodules = [];
    private List<WorktreeEntry> _worktrees = [];
    private TimeSpan _statusDuration;
    private PerformanceSuggestion? _suggestion;
    private string _tipSignature = "";

    private RepositorySession(GitProcessRunner runner, string executable)
    {
        _runner = runner;
        _executable = executable;
    }

    public string Toplevel => _toplevel;

    public string GitDirectory => _gitDirectory;

    public string DisplayName
    {
        get
        {
            var name = Path.GetFileName(_toplevel);
            return string.IsNullOrEmpty(name) ? _toplevel : name;
        }
    }

    public static async Task<RepositorySession> OpenAsync(
        GitProcessRunner runner,
        string executable,
        string path,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? hiddenBranches = null)
    {
        var session = new RepositorySession(runner, executable);
        if (hiddenBranches is not null)
        {
            foreach (var name in hiddenBranches)
            {
                if (BranchVisibility.IsRemembered(name))
                    session._hiddenBranches.Add(name);
            }
        }

        await session.OpenCoreAsync(path, cancellationToken).ConfigureAwait(false);
        return session;
    }

    public SessionState Snapshot()
    {
        lock (_stateLock)
        {
            return new SessionState
            {
                Branch = _branch,
                Entries = _entries.ToArray(),
                Refs = _refs.ToArray(),
                Commits = _commits.ToArray(),
                HistoryEnded = _historyEnded,
                HistoryCapped = _historyCapped,
                HistoryGeneration = _historyGeneration,
                MergeInProgress = _merge,
                Sequencer = _sequencer,
                MergeMessage = _mergeMessage,
                HistoryLabel = BranchVisibility.Caption(_historyQuery, _hiddenBranches),
                HiddenBranches = new HashSet<string>(_hiddenBranches, StringComparer.Ordinal),
                Stashes = _stashes.ToArray(),
                LastStatusDuration = _statusDuration,
                Config = new Dictionary<string, string>(_config, StringComparer.OrdinalIgnoreCase),
                Remotes = _remotes.ToArray(),
                Commands = _commands.ToArray(),
                Suggestion = _suggestion,
                Submodules = _submodules.ToArray(),
                Worktrees = _worktrees.ToArray(),
                SparseCheckout = ConfigParser.IsEnabled(_config, "core.sparseCheckout"),
                LfsPaths = _lfsPaths,
            };
        }
    }

    public Task RefreshStatusAsync(CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            string? previous;
            lock (_stateLock)
                previous = _branch.Oid;
            var status = await QueryStatusAsync(ct).ConfigureAwait(false);
            var headChanged = false;
            lock (_stateLock)
            {
                headChanged = previous != status.Snapshot.Branch.Oid;
                ApplyStatus(status);
            }

            if (headChanged)
                await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: true).ConfigureAwait(false);
        }, cancellationToken);

    public Task RefreshRefsAndStatusAsync(CancellationToken cancellationToken) =>
        RunAsync(ct => LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false), cancellationToken);

    public Task ReloadHistoryAsync(CancellationToken cancellationToken) =>
        RunAsync(ReloadHistoryCoreAsync, cancellationToken);

    public Task<bool> LoadMoreHistoryAsync(bool pastCap, CancellationToken cancellationToken) =>
        RunAsync(ct => LoadMoreCoreAsync(pastCap, ct), cancellationToken);

    public Task<IReadOnlyList<CommitFileChange>?> CommitFilesAsync(string sha, string? firstParent, CancellationToken cancellationToken)
    {
        var token = _filesGate.Next();
        return RunAsync(async ct =>
        {
            var changes = await _scheduler.ReadAsync(async inner =>
            {
                // git show on a merge is a combined diff and lists only paths that differ from every parent.
                // A clean merge then has an empty file list. The patch view already diffs the first parent.
                var arguments = string.IsNullOrEmpty(firstParent)
                    ? GitCommands.NameStatus(_toplevel, sha)
                    : GitCommands.RangeNameStatus(_toplevel, firstParent, sha);
                var output = await ExecuteAsync(arguments, null, inner).ConfigureAwait(false);
                Checked(output);
                return NameStatusParser.Parse(output.Stdout);
            }, ct).ConfigureAwait(false);
            return _filesGate.IsCurrent(token) ? changes : null;
        }, cancellationToken);
    }

    public Task<DiffDocument?> WorkingDiffAsync(
        string path,
        bool staged,
        bool untracked,
        bool allowLarge,
        CancellationToken cancellationToken,
        bool ignoreWhitespace = false)
    {
        var token = _diffGate.Next();
        return RunAsync(async ct =>
        {
            var document = await _scheduler.ReadAsync(async inner =>
            {
                if (untracked && !staged)
                {
                    var untrackedDiff = await DiffUntrackedAsync(path, allowLarge, ignoreWhitespace, inner).ConfigureAwait(false);
                    return await AnnotateAsync(untrackedDiff, path, null, null, true, inner).ConfigureAwait(false);
                }

                var arguments = staged
                    ? GitCommands.DiffStaged(_toplevel, path, ignoreWhitespace)
                    : GitCommands.DiffUnstaged(_toplevel, path, ignoreWhitespace);
                var output = await ExecuteAsync(arguments, null, inner).ConfigureAwait(false);
                Checked(output);
                var before = staged ? "HEAD" : "";
                string? after = staged ? "" : null;
                return await AnnotateAsync(ToDiff(output, allowLarge), path, before, after, !staged, inner).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
            return _diffGate.IsCurrent(token) ? document : null;
        }, cancellationToken);
    }

    public Task<DiffDocument?> WorktreeDiffAsync(bool staged, bool allowLarge, bool ignoreWhitespace, CancellationToken cancellationToken)
    {
        var token = _diffGate.Next();
        var before = staged ? "HEAD" : "";
        string? after = staged ? "" : null;
        return ReadDiffAsync(token, allowLarge, cancellationToken, inner =>
            ExecuteAsync(GitCommands.DiffWorktree(_toplevel, staged, ignoreWhitespace), null, inner),
            async (document, inner) =>
            {
                // git diff omits untracked paths. Without them, a working copy whose only
                // change is a new file has an empty diff, and the view falls through to history.
                if (!staged)
                    document = await AppendUntrackedAsync(document, allowLarge, ignoreWhitespace, inner).ConfigureAwait(false);
                return await AnnotateAsync(document, null, before, after, !staged, inner).ConfigureAwait(false);
            });
    }

    public Task<DiffDocument?> CommitDiffAsync(
        string sha,
        string? firstParent,
        string? path,
        bool allowLarge,
        CancellationToken cancellationToken,
        bool ignoreWhitespace = false)
    {
        var token = _diffGate.Next();
        return ReadDiffAsync(token, allowLarge, cancellationToken, inner =>
        {
            var arguments = string.IsNullOrEmpty(firstParent)
                ? GitCommands.ShowPatch(_toplevel, sha, path, ignoreWhitespace)
                : GitCommands.DiffRange(_toplevel, firstParent, sha, path, ignoreWhitespace);
            return ExecuteAsync(arguments, null, inner);
        },
        (document, inner) => AnnotateAsync(document, path, firstParent, sha, false, inner));
    }

    public Task<DiffDocument?> RangeDiffAsync(
        string older,
        string newer,
        string? path,
        bool allowLarge,
        bool ignoreWhitespace,
        CancellationToken cancellationToken)
    {
        var token = _diffGate.Next();
        return ReadDiffAsync(token, allowLarge, cancellationToken, inner =>
            ExecuteAsync(GitCommands.DiffRange(_toplevel, older, newer, path, ignoreWhitespace), null, inner),
            (document, inner) => AnnotateAsync(document, path, older, newer, false, inner));
    }

    public Task<IReadOnlyList<CommitFileChange>?> RangeFilesAsync(string older, string newer, CancellationToken cancellationToken)
    {
        var token = _filesGate.Next();
        return RunAsync(async ct =>
        {
            var changes = await _scheduler.ReadAsync(async inner =>
            {
                var output = await ExecuteAsync(GitCommands.RangeNameStatus(_toplevel, older, newer), null, inner).ConfigureAwait(false);
                Checked(output);
                return NameStatusParser.Parse(output.Stdout);
            }, ct).ConfigureAwait(false);
            return _filesGate.IsCurrent(token) ? changes : null;
        }, cancellationToken);
    }

    public Task<BlameDocument?> BlameAsync(string? revision, string path, bool allowLarge, CancellationToken cancellationToken)
    {
        var token = _diffGate.Next();
        return RunAsync(async ct =>
        {
            var document = await _scheduler.ReadAsync(
                inner => LoadBlameAsync(revision, path, allowLarge, throwOnFailure: true, inner),
                ct).ConfigureAwait(false);
            return _diffGate.IsCurrent(token) ? document : null;
        }, cancellationToken);
    }

    /// <summary>Blame one file without cancelling another file's blame. A refusal is <see cref="BlameDocument.Error"/>.</summary>
    public Task<BlameDocument> ReadBlameAsync(string? revision, string path, bool allowLarge, CancellationToken cancellationToken) =>
        RunAsync(
            ct => _scheduler.ReadAsync(
                inner => LoadBlameAsync(revision, path, allowLarge, throwOnFailure: false, inner),
                ct),
            cancellationToken);

    private async Task<BlameDocument> LoadBlameAsync(
        string? revision,
        string path,
        bool allowLarge,
        bool throwOnFailure,
        CancellationToken cancellationToken)
    {
        var output = await ExecuteAsync(GitCommands.Blame(_toplevel, revision, path), null, cancellationToken).ConfigureAwait(false);
        Track(output);
        if (output.ExitCode != 0)
        {
            if (throwOnFailure)
                throw new GitCommandFailedException(output);
            return new BlameDocument(false, [], BlameParser.Notice(output.StandardError));
        }

        if (!allowLarge && output.Stdout.Length > HistoryLimits.MaxDiffBytes)
            return BlameDocument.TooLarge;
        var lines = BlameParser.Parse(_encoding.GetString(output.Stdout));
        if (!allowLarge && lines.Count > HistoryLimits.MaxDiffLines)
            return BlameDocument.TooLarge;
        return new BlameDocument(false, lines);
    }

    public Task<ConflictDocument?> ConflictAsync(string path, bool allowLarge, CancellationToken cancellationToken)
    {
        var full = RepoPath.CombineUnder(_toplevel, path);
        if (full is null)
            throw new RepositoryActionException("That path is outside the repository.");
        var token = _diffGate.Next();
        return RunAsync(async ct =>
        {
            var document = await _scheduler.ReadAsync(async inner =>
            {
                byte[] worktree;
                try
                {
                    worktree = File.Exists(full)
                        ? await File.ReadAllBytesAsync(full, inner).ConfigureAwait(false)
                        : [];
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new RepositoryActionException("Could not read the conflicted file. " + exception.Message);
                }

                if (ContainsNul(worktree))
                    return ConflictDocument.Binary;
                if (!allowLarge && worktree.Length > HistoryLimits.MaxDiffBytes)
                    return ConflictDocument.TooLarge;

                var ours = await ReadStageAsync(2, path, inner).ConfigureAwait(false);
                var theirs = await ReadStageAsync(3, path, inner).ConfigureAwait(false);
                var baseBytes = await ReadStageAsync(1, path, inner).ConfigureAwait(false);
                if (ContainsNul(ours) || ContainsNul(theirs) || ContainsNul(baseBytes))
                    return ConflictDocument.Binary;
                if (!allowLarge && (ours.Length > HistoryLimits.MaxDiffBytes || theirs.Length > HistoryLimits.MaxDiffBytes || baseBytes.Length > HistoryLimits.MaxDiffBytes))
                    return ConflictDocument.TooLarge;

                var workText = Encoding.UTF8.GetString(worktree);
                var pieces = ConflictParser.Parse(workText);
                if (pieces.Any(piece => piece.IsConflict))
                    return new ConflictDocument(false, false, false, pieces);

                var synthetic = new ConflictPiece(
                    true,
                    "",
                    Encoding.UTF8.GetString(ours),
                    Encoding.UTF8.GetString(theirs),
                    Encoding.UTF8.GetString(baseBytes),
                    workText);
                return new ConflictDocument(false, false, true, [synthetic]);
            }, ct).ConfigureAwait(false);
            return _diffGate.IsCurrent(token) ? document : null;
        }, cancellationToken);
    }

    public Task SetHistoryAsync(HistoryQuery? query, CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            lock (_stateLock)
                _historyQuery = query is null || query.IsEmpty ? null : query;
            await ReloadHistoryCoreAsync(ct).ConfigureAwait(false);
        }, cancellationToken);

    public Task SetHiddenBranchesAsync(IReadOnlyCollection<string> hidden, CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            string[] previous;
            lock (_stateLock)
            {
                previous = _hiddenBranches.ToArray();
                if (!ReplaceHidden(hidden))
                    return;
            }

            try
            {
                await ReloadHistoryCoreAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                lock (_stateLock)
                    RestoreHidden(previous);
                throw;
            }
        }, cancellationToken);

    private bool ReplaceHidden(IReadOnlyCollection<string> hidden)
    {
        var next = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in hidden)
        {
            if (BranchVisibility.IsRemembered(name))
                next.Add(name);
        }

        if (next.Count == _hiddenBranches.Count && next.All(_hiddenBranches.Contains))
            return false;
        _hiddenBranches.Clear();
        foreach (var name in next)
            _hiddenBranches.Add(name);
        return true;
    }

    private void RestoreHidden(IReadOnlyList<string> names)
    {
        _hiddenBranches.Clear();
        foreach (var name in names)
            _hiddenBranches.Add(name);
    }

    /// <summary>
    /// The checked-out branch is a hidden tip. Passing HEAD would walk that branch anyway.
    /// A detached HEAD stays, because it is not a branch ref.
    /// </summary>
    private bool CurrentBranchHidden()
    {
        if (_branch.Detached || _branch.Unborn || string.IsNullOrEmpty(_branch.HeadName))
            return false;
        return _hiddenBranches.Contains("refs/heads/" + _branch.HeadName);
    }

    public Task StashPushAsync(string? message, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.StashPush(_toplevel, message), null, cancellationToken);

    public Task StashPopAsync(string stashRef, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.StashPop(_toplevel, stashRef), null, cancellationToken);

    public Task StashApplyAsync(string stashRef, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.StashApply(_toplevel, stashRef), null, cancellationToken);

    public Task StashDropAsync(string stashRef, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.StashDrop(_toplevel, stashRef), null, cancellationToken);

    public Task ResetAsync(string mode, string sha, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Reset(_toplevel, mode, sha), null, cancellationToken);

    public Task CherryPickAsync(string sha, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.CherryPick(_toplevel, sha), null, cancellationToken);

    public Task RevertAsync(string sha, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Revert(_toplevel, sha), null, cancellationToken);

    public async Task AbortSequencerAsync(CancellationToken cancellationToken)
    {
        SequencerKind kind;
        lock (_stateLock)
            kind = _sequencer;
        var command = kind switch
        {
            SequencerKind.Rebase => GitCommands.AbortRebase(_toplevel),
            SequencerKind.CherryPick => GitCommands.AbortCherryPick(_toplevel),
            SequencerKind.Revert => GitCommands.AbortRevert(_toplevel),
            _ => GitCommands.AbortMerge(_toplevel),
        };
        try
        {
            await MutateAsync(command, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CleanupRebaseEditorIfIdle();
        }
    }

    public async Task ContinueSequencerAsync(CancellationToken cancellationToken)
    {
        SequencerKind kind;
        lock (_stateLock)
            kind = _sequencer;
        var command = kind switch
        {
            SequencerKind.Rebase => GitCommands.ContinueRebase(_toplevel),
            SequencerKind.CherryPick => GitCommands.ContinueCherryPick(_toplevel),
            SequencerKind.Revert => GitCommands.ContinueRevert(_toplevel),
            SequencerKind.Merge => GitCommands.ContinueMerge(_toplevel),
            _ => null,
        };
        if (command is null)
            return;
        // Git for Windows runs the editor through its shell, which treats backslashes as escapes.
        // `true` is that shell's no-op, and it is also the no-op on Linux and macOS.
        var environment = new Dictionary<string, string>
        {
            ["GIT_EDITOR"] = "true",
            ["GIT_SEQUENCE_EDITOR"] = "true",
        };
        try
        {
            await MutateAsync(command, null, cancellationToken, environment: environment).ConfigureAwait(false);
        }
        finally
        {
            CleanupRebaseEditorIfIdle();
        }
    }

    public Task RebaseInteractiveAsync(string? upstream, IReadOnlyList<RebaseStep> steps, CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            var problem = RebasePlan.Validate(steps);
            if (problem is not null)
                throw new RepositoryActionException(problem);

            GitCommandFailedException? failure = null;
            try
            {
                await _scheduler.WriteAsync(async token =>
                {
                    var editor = RebaseEditor.Create(_gitDirectory, steps);
                    var environment = new Dictionary<string, string>
                    {
                        ["GIT_SEQUENCE_EDITOR"] = editor,
                        ["GIT_EDITOR"] = "true",
                    };
                    Checked(await ExecuteAsync(GitCommands.RebaseInteractive(_toplevel, upstream), null, token, environment).ConfigureAwait(false));
                    return 0;
                }, ct).ConfigureAwait(false);
            }
            catch (GitCommandFailedException exception)
            {
                failure = exception;
            }

            try
            {
                await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false).ConfigureAwait(false);
            }
            catch (GitCommandFailedException) when (failure is not null)
            {
            }

            CleanupRebaseEditorIfIdle();
            if (failure is not null)
                throw failure;
        }, cancellationToken);

    public Task AmendAsync(string? message, CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            await _scheduler.WriteAsync(async token =>
            {
                string? file = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        file = Path.Combine(Path.GetTempPath(), "sextant-msg-" + Guid.NewGuid().ToString("N"));
                        var text = message.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                        if (!text.EndsWith('\n'))
                            text += "\n";
                        await File.WriteAllTextAsync(file, text, new UTF8Encoding(false), token).ConfigureAwait(false);
                    }

                    Checked(await ExecuteAsync(GitCommands.Amend(_toplevel, file), null, token).ConfigureAwait(false));
                    return 0;
                }
                finally
                {
                    if (file is not null)
                        TryDelete(file);
                }
            }, ct).ConfigureAwait(false);
            await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false).ConfigureAwait(false);
        }, cancellationToken);

    public Task<IReadOnlyList<(string Sha, string Subject)>> ListUpstreamOnlyAsync(CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            var output = await _scheduler.ReadAsync(
                inner => ExecuteAsync(GitCommands.UpstreamOnly(_toplevel), null, inner),
                ct).ConfigureAwait(false);
            Track(output);
            if (output.ExitCode != 0)
                throw new GitCommandFailedException(output);
            return RebasePlan.ParseSubjects(_encoding.GetString(output.Stdout));
        }, cancellationToken);

    public Task CreateTagAsync(string name, string sha, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.CreateTag(_toplevel, name, sha), null, cancellationToken);

    public Task DeleteTagAsync(string name, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.DeleteTag(_toplevel, name), null, cancellationToken);

    public Task SwitchDetachAsync(string revision, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.SwitchDetach(_toplevel, revision), null, cancellationToken, environment: CheckoutEnvironment, hydrateLfs: true);

    public Task PushTagAsync(string remote, string name, IProgress<string>? progress, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.PushTag(_toplevel, remote, name), progress, cancellationToken);

    public Task DeleteRemoteTagAsync(string remote, string name, IProgress<string>? progress, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.DeleteRemoteTag(_toplevel, remote, name), progress, cancellationToken);

    public Task AddRemoteAsync(string name, string url, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.AddRemote(_toplevel, name, url), null, cancellationToken);

    public Task RemoveRemoteAsync(string name, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.RemoveRemote(_toplevel, name), null, cancellationToken);

    public Task RenameRemoteAsync(string name, string newName, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.RenameRemote(_toplevel, name, newName), null, cancellationToken);

    public Task StageFileAsync(string path, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Stage(_toplevel, path), null, cancellationToken);

    public Task StageAllAsync(CancellationToken cancellationToken)
    {
        var entries = Snapshot().Entries;
        var pending = entries.Where(entry => entry.Kind != ChangeKind.Unmerged && (entry.Unstaged || entry.Kind == ChangeKind.Untracked)).ToList();
        if (pending.Count == 0)
            return Task.CompletedTask;
        if (entries.Any(entry => entry.Kind == ChangeKind.Unmerged))
        {
            var paths = new List<string>();
            foreach (var entry in pending)
            {
                if (!string.IsNullOrEmpty(entry.OriginalPath))
                    paths.Add(entry.OriginalPath);
                paths.Add(entry.Path);
            }

            return MutateAsync(GitCommands.StagePaths(_toplevel, paths), null, cancellationToken);
        }

        return MutateAsync(GitCommands.StageAll(_toplevel), null, cancellationToken);
    }

    public Task UnstageAllAsync(CancellationToken cancellationToken)
    {
        var staged = Snapshot().Entries.Any(entry => entry.Staged && entry.Kind != ChangeKind.Unmerged);
        if (!staged)
            return Task.CompletedTask;
        var command = IsUnborn() ? GitCommands.UnstageAllUnborn(_toplevel) : GitCommands.UnstageAll(_toplevel);
        return MutateAsync(command, null, cancellationToken);
    }

    public Task UnstageFileAsync(string path, CancellationToken cancellationToken)
    {
        var unborn = IsUnborn();
        var command = unborn ? GitCommands.UnstageUnborn(_toplevel, path) : GitCommands.Unstage(_toplevel, path);
        var fallback = unborn ? null : GitCommands.UnstageUnborn(_toplevel, path);
        return MutateAsync(command, null, cancellationToken, fallback);
    }

    public Task DiscardTrackedAsync(string path, CancellationToken cancellationToken)
    {
        var unborn = IsUnborn();
        var command = unborn ? GitCommands.DiscardUnborn(_toplevel, path) : GitCommands.DiscardTracked(_toplevel, path);
        var fallback = unborn ? null : GitCommands.DiscardUnborn(_toplevel, path);
        return MutateAsync(command, null, cancellationToken, fallback);
    }

    public Task DiscardUntrackedAsync(string path, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.DiscardUntracked(_toplevel, path), null, cancellationToken);

    public Task DiscardAllAsync(CancellationToken cancellationToken)
    {
        var entries = Snapshot().Entries.Where(entry => entry.Kind != ChangeKind.Unmerged).ToList();
        if (entries.Count == 0)
            return Task.CompletedTask;

        return RunAsync(async ct =>
        {
            GitCommandFailedException? failure = null;
            try
            {
                await _scheduler.WriteAsync(async token =>
                {
                    var tracked = new List<string>();
                    var untracked = new List<string>();
                    foreach (var entry in entries)
                    {
                        if (entry.Kind == ChangeKind.Untracked)
                        {
                            untracked.Add(entry.Path);
                            continue;
                        }

                        if (!string.IsNullOrEmpty(entry.OriginalPath))
                            tracked.Add(entry.OriginalPath);
                        tracked.Add(entry.Path);
                    }

                    if (tracked.Count > 0)
                    {
                        var command = IsUnborn()
                            ? GitCommands.DiscardUnbornPaths(_toplevel, tracked)
                            : GitCommands.DiscardTrackedPaths(_toplevel, tracked);
                        Checked(await ExecuteAsync(command, null, token).ConfigureAwait(false));
                    }

                    if (untracked.Count > 0)
                        Checked(await ExecuteAsync(GitCommands.DiscardUntrackedPaths(_toplevel, untracked), null, token).ConfigureAwait(false));
                    return 0;
                }, ct).ConfigureAwait(false);
            }
            catch (GitCommandFailedException exception)
            {
                failure = exception;
            }

            try
            {
                await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false).ConfigureAwait(false);
            }
            catch (GitCommandFailedException) when (failure is not null)
            {
            }

            if (failure is not null)
                throw failure;
        }, cancellationToken);
    }

    public Task ApplyHunkAsync(string rawPatch, int hunkIndex, bool reverse, CancellationToken cancellationToken) =>
        ApplyPatchTextAsync(HunkPatch.Slice(rawPatch, hunkIndex), reverse, cancellationToken);

    public Task ApplyLineAsync(string rawPatch, int hunkIndex, int lineIndex, bool reverse, CancellationToken cancellationToken)
    {
        var patch = LinePatch.Slice(rawPatch, hunkIndex, lineIndex);
        if (patch is null)
            throw new InvalidOperationException("That line cannot be staged on its own.");
        return ApplyPatchTextAsync(patch, reverse, cancellationToken);
    }

    private Task ApplyPatchTextAsync(string patch, bool reverse, CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            await _scheduler.WriteAsync(async token =>
            {
                var file = Path.Combine(Path.GetTempPath(), "sextant-hunk-" + Guid.NewGuid().ToString("N") + ".patch");
                try
                {
                    await File.WriteAllTextAsync(file, patch, new UTF8Encoding(false), token).ConfigureAwait(false);
                    var arguments = reverse
                        ? GitCommands.ApplyCachedReverse(_toplevel, file)
                        : GitCommands.ApplyCached(_toplevel, file);
                    Checked(await ExecuteAsync(arguments, null, token).ConfigureAwait(false));
                    return 0;
                }
                finally
                {
                    TryDelete(file);
                }
            }, ct).ConfigureAwait(false);
            await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false).ConfigureAwait(false);
        }, cancellationToken);

    public Task CommitAsync(string message, CancellationToken cancellationToken, bool noVerify = false) =>
        RunAsync(async ct =>
        {
            await _scheduler.WriteAsync(async token =>
            {
                var file = Path.Combine(Path.GetTempPath(), "sextant-msg-" + Guid.NewGuid().ToString("N"));
                try
                {
                    await File.WriteAllTextAsync(file, message, new UTF8Encoding(false), token).ConfigureAwait(false);
                    Checked(await ExecuteAsync(GitCommands.Commit(_toplevel, file, noVerify), null, token).ConfigureAwait(false));
                    return 0;
                }
                finally
                {
                    TryDelete(file);
                }
            }, ct).ConfigureAwait(false);
            await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false).ConfigureAwait(false);
        }, cancellationToken);

    public Task SwitchAsync(string branch, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Switch(_toplevel, branch), null, cancellationToken, environment: CheckoutEnvironment, hydrateLfs: true);

    public async Task SwitchTrackAsync(string remoteBranch, CancellationToken cancellationToken)
    {
        var local = GitCommands.LocalBranchOfRemote(remoteBranch);
        if (local is null)
        {
            await MutateAsync(GitCommands.SwitchTrack(_toplevel, remoteBranch), null, cancellationToken, environment: CheckoutEnvironment, hydrateLfs: true).ConfigureAwait(false);
            return;
        }

        // --track refuses to run when this name already exists, and it does not check the branch out.
        var exists = await LocalBranchExistsAsync(local, cancellationToken).ConfigureAwait(false);
        var arguments = exists
            ? GitCommands.Switch(_toplevel, local)
            : GitCommands.SwitchCreateTrack(_toplevel, local, remoteBranch);
        await MutateAsync(arguments, null, cancellationToken, environment: CheckoutEnvironment, hydrateLfs: true).ConfigureAwait(false);
    }

    private async Task<bool> LocalBranchExistsAsync(string name, CancellationToken cancellationToken)
    {
        var output = await _scheduler.ReadAsync(
            token => ExecuteAsync(GitCommands.VerifyLocalBranch(_toplevel, name), null, token),
            cancellationToken).ConfigureAwait(false);
        Track(output);
        if (output.ExitCode == 0)
            return true;
        if (output.ExitCode == 1)
            return false;
        throw new GitCommandFailedException(output);
    }

    public Task CreateBranchAsync(string name, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.CreateBranch(_toplevel, name), null, cancellationToken);

    public Task CreateBranchAtAsync(string name, string sha, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.CreateBranchAt(_toplevel, name, sha), null, cancellationToken);

    public Task ApplyPatchFileAsync(string patchFile, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.ApplyPatch(_toplevel, patchFile), null, cancellationToken);

    public Task<byte[]> FormatPatchAsync(string sha, CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            var output = await _scheduler.ReadAsync(
                inner => ExecuteAsync(GitCommands.FormatPatch(_toplevel, sha), null, inner),
                ct).ConfigureAwait(false);
            Track(output);
            if (output.ExitCode != 0)
                throw new GitCommandFailedException(output);
            return output.Stdout;
        }, cancellationToken);

    public Task DeleteBranchAsync(string name, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.DeleteBranch(_toplevel, name), null, cancellationToken);

    public Task ForceDeleteBranchAsync(string name, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.ForceDeleteBranch(_toplevel, name), null, cancellationToken);

    public Task DeleteRemoteBranchAsync(string remote, string branch, IProgress<string>? progress, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.DeleteRemoteBranch(_toplevel, remote, branch), progress, cancellationToken);

    public Task<bool> IsMergedIntoHeadAsync(string revision, CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            var output = await _scheduler.ReadAsync(
                inner => ExecuteAsync(GitCommands.NotInHeadCount(_toplevel, revision), null, inner),
                ct).ConfigureAwait(false);
            Track(output);
            if (output.ExitCode != 0)
                throw new GitCommandFailedException(output);
            return _encoding.GetString(output.Stdout).Trim() == "0";
        }, cancellationToken);

    public Task SetUpstreamAsync(string branch, string upstream, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.SetUpstream(_toplevel, branch, upstream), null, cancellationToken);

    public Task MergeAsync(string branch, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Merge(_toplevel, branch), null, cancellationToken);

    public Task RebaseAsync(string branch, CancellationToken cancellationToken)
    {
        // A conflict stops the rebase. `true` is the no-op editor, so git does not open one.
        var environment = new Dictionary<string, string>
        {
            ["GIT_EDITOR"] = "true",
            ["GIT_SEQUENCE_EDITOR"] = "true",
        };
        return MutateAsync(GitCommands.Rebase(_toplevel, branch), null, cancellationToken, environment: environment);
    }

    public Task AbortMergeAsync(CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.AbortMerge(_toplevel), null, cancellationToken);

    public Task FetchAsync(IProgress<string>? progress, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Fetch(_toplevel), progress, cancellationToken);

    public Task FetchAllAsync(IProgress<string>? progress, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.FetchAll(_toplevel), progress, cancellationToken);

    public Task FetchAllPruneAsync(IProgress<string>? progress, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.FetchAllPrune(_toplevel), progress, cancellationToken);

    public Task PullAsync(IProgress<string>? progress, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Pull(_toplevel), progress, cancellationToken);

    public Task PushAsync(IProgress<string>? progress, CancellationToken cancellationToken, bool noVerify = false) =>
        MutateAsync(GitCommands.Push(_toplevel, noVerify), progress, cancellationToken);

    public Task PushForceWithLeaseAsync(IProgress<string>? progress, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.PushForceWithLease(_toplevel), progress, cancellationToken);

    public Task PushUpstreamAsync(string remote, string branch, IProgress<string>? progress, CancellationToken cancellationToken, bool noVerify = false) =>
        MutateAsync(GitCommands.PushUpstream(_toplevel, remote, branch, noVerify), progress, cancellationToken);

    public Task MergetoolAsync(string path, string? command, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.Mergetool(_toplevel, path, command), null, cancellationToken);

    public Task SaveResolutionAsync(string path, string text, CancellationToken cancellationToken)
    {
        var full = RepoPath.CombineUnder(_toplevel, path);
        if (full is null)
            throw new RepositoryActionException("That path is outside the repository.");
        return RunAsync(async ct =>
        {
            // Markers stay in the working tree so the edit is kept, and the path stays unmerged.
            var markers = ConflictParser.ContainsMarkers(text);
            GitCommandFailedException? failure = null;
            try
            {
                await _scheduler.WriteAsync(async token =>
                {
                    try
                    {
                        var parent = Path.GetDirectoryName(full);
                        if (!string.IsNullOrEmpty(parent))
                            Directory.CreateDirectory(parent);
                        await File.WriteAllTextAsync(full, text, new UTF8Encoding(false), token).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        throw new RepositoryActionException("Could not write the resolved file. " + exception.Message);
                    }

                    if (!markers)
                        Checked(await ExecuteAsync(GitCommands.Stage(_toplevel, path), null, token).ConfigureAwait(false));
                    return 0;
                }, ct).ConfigureAwait(false);
            }
            catch (GitCommandFailedException exception)
            {
                failure = exception;
            }

            try
            {
                await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false).ConfigureAwait(false);
            }
            catch (GitCommandFailedException) when (failure is not null)
            {
            }

            if (failure is not null)
                throw failure;
            if (markers)
                throw new RepositoryActionException("Conflict markers are still in the file. The working copy was saved, and the path stays unmerged until the markers are gone.");
        }, cancellationToken);
    }

    public Task SetLocalConfigAsync(string key, string value, CancellationToken cancellationToken) =>
        SetLocalConfigsAsync([(key, value)], cancellationToken);

    public Task SetLocalConfigsAsync(
        IReadOnlyList<(string Key, string Value)> settings,
        CancellationToken cancellationToken) =>
        RunAsync(async ct =>
        {
            GitCommandFailedException? failure = null;
            try
            {
                await _scheduler.WriteAsync(async token =>
                {
                    foreach (var (key, value) in settings)
                        Checked(await ExecuteAsync(GitCommands.SetLocal(_toplevel, key, value), null, token).ConfigureAwait(false));
                    return 0;
                }, ct).ConfigureAwait(false);
            }
            catch (GitCommandFailedException exception)
            {
                failure = exception;
            }

            // Reload even when a later key is rejected, so a key that did land
            // is what the next status and the next open both see.
            try
            {
                await ReloadConfigAsync(ct).ConfigureAwait(false);
                await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false).ConfigureAwait(false);
            }
            catch (GitCommandFailedException) when (failure is not null)
            {
            }

            if (failure is not null)
                throw failure;
        }, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _scheduler.Dispose();
        await Task.CompletedTask;
    }

    private async Task OpenCoreAsync(string path, CancellationToken cancellationToken)
    {
        await RunAsync(async ct =>
        {
            var top = Checked(await ExecuteInAsync(GitCommands.TopLevel(path), path, ct).ConfigureAwait(false));
            var dir = Checked(await ExecuteInAsync(GitCommands.GitDir(path), path, ct).ConfigureAwait(false));
            _toplevel = RepoPath.Normalize(Encoding.UTF8.GetString(top.Stdout).Trim());
            _gitDirectory = RepoPath.Normalize(Encoding.UTF8.GetString(dir.Stdout).Trim());
            await ReloadConfigAsync(ct).ConfigureAwait(false);
            var statusTask = QueryStatusAsync(ct);
            var refsTask = QueryRefsAsync(ct);
            var logTask = QueryLogAsync(0, HistoryLimits.FirstPage, ct);
            await Task.WhenAll(statusTask, refsTask, logTask).ConfigureAwait(false);
            var status = await statusTask.ConfigureAwait(false);
            var refs = await refsTask.ConfigureAwait(false);
            var log = await logTask.ConfigureAwait(false);
            var includeStash = refs.Refs.Any(reference => reference.Name == "refs/stash");
            lock (_stateLock)
            {
                ApplyStatus(status);
                _refs = refs.Refs.ToList();
                _remotes = refs.Remotes.ToList();
                _stashes = refs.Stashes.ToList();
                _submodules = refs.Submodules.ToList();
                _worktrees = refs.Worktrees.ToList();
                _includeStash = includeStash;
                _tipSignature = Tips(_branch, _refs);
                _lanes.Reset();
                _commits = Build(_lanes, log.Commits);
                _historyEnded = log.Ended;
                _historyCapped = false;
                _historyGeneration = 1;
            }

            if (includeStash)
                await ReloadHistoryCoreAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadRefsAndMaybeHistoryAsync(CancellationToken cancellationToken, bool statusAlreadyApplied)
    {
        var statusTask = statusAlreadyApplied ? null : QueryStatusAsync(cancellationToken);
        var refsTask = QueryRefsAsync(cancellationToken);
        if (statusTask is not null)
            await Task.WhenAll(statusTask, refsTask).ConfigureAwait(false);
        else
            await refsTask.ConfigureAwait(false);

        var refs = await refsTask.ConfigureAwait(false);
        var reload = false;
        lock (_stateLock)
        {
            var oldOid = _branch.Oid;
            var oldTips = _tipSignature;
            if (statusTask is not null)
                ApplyStatus(statusTask.Result);
            _refs = refs.Refs.ToList();
            _remotes = refs.Remotes.ToList();
            _stashes = refs.Stashes.ToList();
            _submodules = refs.Submodules.ToList();
            _worktrees = refs.Worktrees.ToList();
            _includeStash = _refs.Exists(reference => reference.Name == "refs/stash");
            _tipSignature = Tips(_branch, _refs);
            reload = oldOid != _branch.Oid || oldTips != _tipSignature;
        }

        if (reload)
            await ReloadHistoryCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReloadHistoryCoreAsync(CancellationToken cancellationToken)
    {
        var page = await QueryLogAsync(0, HistoryLimits.FirstPage, cancellationToken).ConfigureAwait(false);
        lock (_stateLock)
        {
            _historyGeneration++;
            _lanes.Reset();
            _commits = Build(_lanes, page.Commits);
            _historyEnded = page.Ended;
            _historyCapped = false;
        }
    }

    private async Task<bool> LoadMoreCoreAsync(bool pastCap, CancellationToken cancellationToken)
    {
        int skip;
        int generation;
        lock (_stateLock)
        {
            if (_historyEnded)
                return false;
            if (_commits.Count >= HistoryLimits.SoftCap && !pastCap)
            {
                _historyCapped = true;
                return false;
            }

            skip = _commits.Count;
            generation = _historyGeneration;
        }

        var page = await QueryLogAsync(skip, HistoryLimits.Page, cancellationToken).ConfigureAwait(false);
        lock (_stateLock)
        {
            if (generation != _historyGeneration)
                return false;
            foreach (var commit in page.Commits)
                _commits.Add(new GraphCommit { Commit = commit, Lanes = _lanes.Assign(commit) });
            _historyEnded = page.Ended;
            _historyCapped = !page.Ended && _commits.Count >= HistoryLimits.SoftCap;
            return page.Commits.Count > 0;
        }
    }

    private async Task<StatusLoad> QueryStatusAsync(CancellationToken cancellationToken)
    {
        return await _scheduler.ReadAsync(async token =>
        {
            var output = Checked(await ExecuteAsync(GitCommands.Status(_toplevel), null, token).ConfigureAwait(false));
            var snapshot = StatusParser.Parse(output.Stdout);
            var lfs = await ReadLfsAsync(AttributePaths(snapshot.Entries), null, token).ConfigureAwait(false);
            return new StatusLoad(snapshot, output.Duration, lfs);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RefLoad> QueryRefsAsync(CancellationToken cancellationToken)
    {
        return await _scheduler.ReadAsync(async token =>
        {
            var refsOutput = Checked(await ExecuteAsync(GitCommands.Refs(_toplevel), null, token).ConfigureAwait(false));
            var remoteOutput = Checked(await ExecuteAsync(GitCommands.Remotes(_toplevel), null, token).ConfigureAwait(false));
            var names = Encoding.UTF8.GetString(remoteOutput.Stdout)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var parsed = RefParser.Parse(refsOutput.Stdout, _encoding);
            IReadOnlyList<StashEntry> stashes = [];
            if (parsed.Any(reference => reference.Name == "refs/stash"))
            {
                var stashOutput = Checked(await ExecuteAsync(GitCommands.StashList(_toplevel), null, token).ConfigureAwait(false));
                stashes = StashParser.Parse(stashOutput.Stdout, _encoding);
            }

            var submoduleOutput = await ExecuteAsync(GitCommands.SubmoduleStatus(_toplevel), null, token).ConfigureAwait(false);
            Track(submoduleOutput);
            var submodules = submoduleOutput.ExitCode == 0
                ? SubmoduleParser.Parse(Encoding.UTF8.GetString(submoduleOutput.Stdout))
                : Array.Empty<SubmoduleEntry>();
            var worktreeOutput = await ExecuteAsync(GitCommands.WorktreeList(_toplevel), null, token).ConfigureAwait(false);
            Track(worktreeOutput);
            var worktrees = worktreeOutput.ExitCode == 0
                ? WorktreeParser.Parse(Encoding.UTF8.GetString(worktreeOutput.Stdout))
                : Array.Empty<WorktreeEntry>();
            parsed = await ApplyLocalUpstreamAsync(parsed, token).ConfigureAwait(false);
            return new RefLoad(parsed, names, stashes, submodules, worktrees);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<GitRef>> ApplyLocalUpstreamAsync(
        IReadOnlyList<GitRef> refs,
        CancellationToken cancellationToken)
    {
        if (!refs.Any(reference => reference.Name.StartsWith("refs/heads/", StringComparison.Ordinal)))
            return refs;

        var output = Checked(await ExecuteAsync(GitCommands.LocalUpstream(_toplevel), null, cancellationToken).ConfigureAwait(false));
        var tracking = UpstreamTrackParser.Parse(_encoding.GetString(output.Stdout));
        if (tracking.Count == 0)
            return refs;

        var annotated = new List<GitRef>(refs.Count);
        foreach (var reference in refs)
        {
            annotated.Add(tracking.TryGetValue(reference.Name, out var counts)
                ? reference with { Ahead = counts.Ahead, Behind = counts.Behind }
                : reference);
        }

        return annotated;
    }

    private async Task<LogLoad> QueryLogAsync(int skip, int count, CancellationToken cancellationToken)
    {
        HistoryQuery? query;
        bool includeStash;
        bool currentHidden;
        string[]? hidden;
        IReadOnlyList<string>? visibleStashes;
        lock (_stateLock)
        {
            query = _historyQuery;
            includeStash = _includeStash && query?.Revision is null && query?.ShaLookup != true;
            currentHidden = CurrentBranchHidden();
            hidden = _hiddenBranches.Count == 0 ? null : _hiddenBranches.ToArray();
            visibleStashes = includeStash ? VisibleStashRevisions() : null;
        }

        var path = query?.LogPath;
        return await _scheduler.ReadAsync(async token =>
        {
            if (query is { ShaLookup: true, Revision: { } revision })
            {
                var resolved = await ExecuteAsync(GitCommands.RevParseCommit(_toplevel, revision), null, token).ConfigureAwait(false);
                if (resolved.ExitCode != 0)
                {
                    Track(resolved);
                    throw new GitCommandFailedException(resolved);
                }

                var sha = _encoding.GetString(resolved.Stdout).Trim();
                Track(resolved);
                var found = await ReadLogAsync(token, 0, 1, includeHead: false, includeStash: false, sha, null, null, path, null, null).ConfigureAwait(false);
                return new LogLoad(found.Commits, true);
            }

            if (query is { MatchSubjectOrAuthor: true })
            {
                var take = skip + count;
                var head = query.Revision is null && !currentHidden;
                var bySubject = await ReadLogAsync(token, 0, take, head, includeStash, query.Revision, query.Grep, null, path, hidden, visibleStashes).ConfigureAwait(false);
                var byAuthor = await ReadLogAsync(token, 0, take, head, includeStash, query.Revision, null, query.Author, path, hidden, visibleStashes).ConfigureAwait(false);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var merged = new List<CommitRecord>();
                foreach (var commit in bySubject.Commits.Concat(byAuthor.Commits).OrderByDescending(commit => commit.AuthorUnixSeconds))
                {
                    if (seen.Add(commit.Sha))
                        merged.Add(commit);
                }

                var page = merged.Skip(skip).Take(count).ToList();
                var ended = bySubject.Ended && byAuthor.Ended;
                return new LogLoad(page, ended);
            }

            var includeHead = query?.Revision is null && !currentHidden;
            return await ReadLogAsync(
                token,
                skip,
                count,
                includeHead,
                includeStash,
                query?.Revision,
                query is { MatchSubjectOrAuthor: false } ? query.Grep : null,
                query is { MatchSubjectOrAuthor: false } ? query.Author : null,
                path,
                hidden,
                visibleStashes).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Null until the stash list is known, which keeps the latest stash through <c>refs/stash</c>.
    /// A list is each <c>stash@{n}</c> whose eye is on. <c>refs/stash</c> alone does not walk older stashes.
    /// </summary>
    private IReadOnlyList<string>? VisibleStashRevisions()
    {
        if (!_includeStash || _stashes.Count == 0)
            return null;
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in _hiddenBranches)
        {
            if (BranchVisibility.IsStashToken(name))
                hidden.Add(name[BranchVisibility.StashPrefix.Length..]);
        }

        return _stashes.Where(stash => !hidden.Contains(stash.Sha)).Select(stash => stash.Ref).ToArray();
    }

    private async Task<LogLoad> ReadLogAsync(
        CancellationToken token,
        int skip,
        int count,
        bool includeHead,
        bool includeStash,
        string? revision,
        string? grep,
        string? author,
        string? path,
        IReadOnlyCollection<string>? hiddenBranches,
        IReadOnlyList<string>? visibleStashes)
    {
        var output = await ExecuteAsync(
            GitCommands.Log(_toplevel, skip, count, includeHead, includeStash, revision, grep, author, path, hiddenBranches, visibleStashes),
            null,
            token).ConfigureAwait(false);
        if (output.ExitCode != 0 && includeHead && LogParser.IsUnborn(output.StandardError))
        {
            output = await ExecuteAsync(
                GitCommands.Log(_toplevel, skip, count, includeHead: false, includeStash, revision, grep, author, path, hiddenBranches, visibleStashes),
                null,
                token).ConfigureAwait(false);
        }

        if (output.ExitCode != 0)
        {
            Track(output);
            if (LogParser.IsUnborn(output.StandardError))
                return new LogLoad([], true);
            throw new GitCommandFailedException(output);
        }

        Track(output);
        var commits = LogParser.Parse(output.Stdout, _encoding);
        return new LogLoad(commits, commits.Count < count);
    }

    private async Task ReloadConfigAsync(CancellationToken cancellationToken)
    {
        await _scheduler.ReadAsync(async token =>
        {
            var output = await ExecuteAsync(GitCommands.ConfigList(_toplevel), null, token).ConfigureAwait(false);
            Track(output);
            if (output.ExitCode != 0)
                return 0;
            var config = ConfigParser.Parse(output.Stdout);
            lock (_stateLock)
            {
                _config = config;
                _encoding = ConfigParser.LogEncoding(config);
            }

            return 0;
        }, cancellationToken).ConfigureAwait(false);
    }

    // Asking for an LFS password can sit forever in the credential helper, so checkout must not download.
    // git lfs checkout afterwards writes objects that are already in the local store, and it does not download.
    private static readonly Dictionary<string, string> CheckoutEnvironment = new()
    {
        ["GIT_LFS_SKIP_SMUDGE"] = "1",
    };

    private async Task MutateAsync(
        IReadOnlyList<string> arguments,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? whenHeadMissing = null,
        IReadOnlyDictionary<string, string>? environment = null,
        bool hydrateLfs = false)
    {
        await RunAsync(async ct =>
        {
            GitCommandFailedException? failure = null;
            try
            {
                await _scheduler.WriteAsync(async token =>
                {
                    var output = await ExecuteAsync(arguments, progress, token, environment).ConfigureAwait(false);
                    if (output.ExitCode != 0 && whenHeadMissing is not null && IsMissingHead(output.StandardError))
                        output = await ExecuteAsync(whenHeadMissing, null, token).ConfigureAwait(false);
                    Checked(output);
                    if (hydrateLfs)
                        await HydrateLocalLfsAsync(_toplevel, token).ConfigureAwait(false);
                    return 0;
                }, ct).ConfigureAwait(false);
            }
            catch (GitCommandFailedException exception)
            {
                failure = exception;
            }

            try
            {
                await LoadRefsAndMaybeHistoryAsync(ct, statusAlreadyApplied: false).ConfigureAwait(false);
            }
            catch (GitCommandFailedException) when (failure is not null)
            {
            }

            if (failure is not null)
                throw failure;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fills pointer files from the local LFS store. A repository without Git LFS leaves those pointers as they are.
    /// </summary>
    private async Task HydrateLocalLfsAsync(string directory, CancellationToken cancellationToken)
    {
        var output = await ExecuteAsync(GitCommands.LfsCheckoutAll(directory), null, cancellationToken).ConfigureAwait(false);
        Track(output);
    }

    private async Task RunAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await work(linked.Token).ConfigureAwait(false);
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        return await work(linked.Token).ConfigureAwait(false);
    }

    private Task<GitOutput> ExecuteAsync(
        IReadOnlyList<string> arguments,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null,
        byte[]? standardInput = null) =>
        ExecuteInAsync(arguments, _toplevel, progress, cancellationToken, environment, standardInput);

    private Task<GitOutput> ExecuteInAsync(IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken) =>
        ExecuteInAsync(arguments, workingDirectory, null, cancellationToken);

    private Task<GitOutput> ExecuteInAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null,
        byte[]? standardInput = null) =>
        _runner.RunAsync(new GitRequest
        {
            Executable = _executable,
            Arguments = arguments,
            WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? null : workingDirectory,
            Progress = progress,
            Environment = environment,
            StandardInput = standardInput,
        }, cancellationToken);

    private GitOutput Checked(GitOutput output)
    {
        Track(output);
        if (output.ExitCode != 0)
            throw new GitCommandFailedException(output);
        return output;
    }

    private void Track(GitOutput output)
    {
        var entry = new CommandLogEntry(
            DateTimeOffset.UtcNow,
            output.DisplayArguments,
            output.ExitCode,
            output.Duration,
            output.StandardError);
        lock (_stateLock)
        {
            _commands.Add(entry);
            if (_commands.Count > 200)
                _commands.RemoveAt(0);
        }
    }

    private void ApplyStatus(StatusLoad status)
    {
        _branch = status.Snapshot.Branch;
        _entries = status.Snapshot.Entries.ToList();
        _lfsPaths = new HashSet<string>(status.LfsPaths, StringComparer.Ordinal);
        _statusDuration = status.Duration;
        var git = _gitDirectory;
        var rebase = Directory.Exists(Path.Combine(git, "rebase-merge"))
            || Directory.Exists(Path.Combine(git, "rebase-apply"));
        var mergeHead = File.Exists(Path.Combine(git, "MERGE_HEAD"));
        var cherryPick = File.Exists(Path.Combine(git, "CHERRY_PICK_HEAD"));
        var revert = File.Exists(Path.Combine(git, "REVERT_HEAD"));
        var unmerged = _entries.Exists(entry => entry.Kind == ChangeKind.Unmerged);
        _sequencer = rebase
            ? SequencerKind.Rebase
            : cherryPick
                ? SequencerKind.CherryPick
                : revert
                    ? SequencerKind.Revert
                    : mergeHead || unmerged
                        ? SequencerKind.Merge
                        : SequencerKind.None;
        _merge = _sequencer == SequencerKind.Merge;
        var messagePath = Path.Combine(_gitDirectory, "MERGE_MSG");
        _mergeMessage = null;
        if (_sequencer != SequencerKind.None && File.Exists(messagePath))
        {
            try
            {
                _mergeMessage = File.ReadAllText(messagePath);
            }
            catch (IOException)
            {
            }
        }
        _suggestion = PerformanceAdvisor.Evaluate(status.Duration, _config);
    }

    private bool IsUnborn()
    {
        lock (_stateLock)
            return _branch.Unborn;
    }

    private Task<DiffDocument?> ReadDiffAsync(
        int token,
        bool allowLarge,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<GitOutput>> execute,
        Func<DiffDocument, CancellationToken, Task<DiffDocument>>? annotate = null) =>
        RunAsync(async ct =>
        {
            var document = await _scheduler.ReadAsync(async inner =>
            {
                var output = await execute(inner).ConfigureAwait(false);
                Checked(output);
                var parsed = ToDiff(output, allowLarge);
                return annotate is null ? parsed : await annotate(parsed, inner).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
            return _diffGate.IsCurrent(token) ? document : null;
        }, cancellationToken);

    private async Task<byte[]> ReadStageAsync(int stage, string path, CancellationToken cancellationToken)
    {
        var output = await ExecuteAsync(GitCommands.ShowStage(_toplevel, stage, path), null, cancellationToken).ConfigureAwait(false);
        Track(output);
        return output.ExitCode == 0 ? output.Stdout : [];
    }

    private static bool ContainsNul(byte[] data) => Array.IndexOf(data, (byte)0) >= 0;

    private async Task<DiffDocument> DiffUntrackedAsync(string path, bool allowLarge, bool ignoreWhitespace, CancellationToken cancellationToken)
    {
        // git diff --no-index exits 1 when the files differ. That is a diff, not a failure.
        var output = await ExecuteAsync(GitCommands.DiffUntracked(_toplevel, path, ignoreWhitespace), null, cancellationToken).ConfigureAwait(false);
        var failed = output.ExitCode != 0
            && (output.ExitCode != 1 || output.Stdout.Length == 0 || output.StandardError.Contains("fatal:", StringComparison.OrdinalIgnoreCase));
        if (failed)
        {
            Track(output);
            throw new GitCommandFailedException(output);
        }

        Track(output);
        return ToDiff(output, allowLarge);
    }

    private static bool IsMissingHead(string standardError) =>
        standardError.Contains("could not resolve 'HEAD'", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("ambiguous argument 'HEAD'", StringComparison.OrdinalIgnoreCase);

    private async Task<DiffDocument> AppendUntrackedAsync(
        DiffDocument tracked,
        bool allowLarge,
        bool ignoreWhitespace,
        CancellationToken cancellationToken)
    {
        if (tracked.IsTooLarge)
            return tracked;

        List<string> listed;
        lock (_stateLock)
            listed = _entries.Where(entry => entry.Kind == ChangeKind.Untracked).Select(entry => entry.Path).ToList();
        if (listed.Count == 0)
            return tracked;

        var files = new List<string>();
        foreach (var path in listed)
        {
            var expanded = await ExpandUntrackedAsync(path, cancellationToken).ConfigureAwait(false);
            foreach (var file in expanded)
            {
                if (!files.Contains(file, StringComparer.Ordinal))
                    files.Add(file);
            }
        }

        if (files.Count == 0)
            return tracked;

        var builder = new StringBuilder(tracked.RawPatch);
        var bytes = _encoding.GetByteCount(builder.ToString());
        foreach (var file in files)
        {
            var extra = await DiffUntrackedAsync(file, allowLarge, ignoreWhitespace, cancellationToken).ConfigureAwait(false);
            if (extra.IsTooLarge)
                return DiffDocument.TooLarge;
            if (extra.RawPatch.Length == 0)
                continue;
            if (builder.Length > 0 && builder[^1] != '\n')
                builder.Append('\n');
            builder.Append(extra.RawPatch);
            bytes += _encoding.GetByteCount(extra.RawPatch) + 1;
            if (!allowLarge && bytes > HistoryLimits.MaxDiffBytes)
                return DiffDocument.TooLarge;
        }

        return FromPatch(builder.ToString(), allowLarge);
    }

    private async Task<IReadOnlyList<string>> ExpandUntrackedAsync(string path, CancellationToken cancellationToken)
    {
        if (!path.EndsWith('/') && !path.EndsWith('\\'))
            return [path];

        var output = Checked(await ExecuteAsync(GitCommands.UntrackedIn(_toplevel, path), null, cancellationToken).ConfigureAwait(false));
        return _encoding.GetString(output.Stdout).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private DiffDocument ToDiff(GitOutput output, bool allowLarge)
    {
        if (!allowLarge && output.Stdout.Length > HistoryLimits.MaxDiffBytes)
            return DiffDocument.TooLarge;
        return FromPatch(_encoding.GetString(output.Stdout), allowLarge);
    }

    private DiffDocument FromPatch(string text, bool allowLarge)
    {
        if (!allowLarge && _encoding.GetByteCount(text) > HistoryLimits.MaxDiffBytes)
            return DiffDocument.TooLarge;
        var files = DiffParser.ParseFiles(text);
        var lines = 0;
        foreach (var file in files)
            lines += file.Document.LineCount;
        if (!allowLarge && lines > HistoryLimits.MaxDiffLines)
            return DiffDocument.TooLarge;
        if (files.Count == 1)
            return files[0].Document;
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return new DiffDocument(false, false, false, false, false, [], normalized);
    }

    private static List<GraphCommit> Build(LaneAssigner lanes, IReadOnlyList<CommitRecord> commits)
    {
        var rows = new List<GraphCommit>(commits.Count);
        foreach (var commit in commits)
            rows.Add(new GraphCommit { Commit = commit, Lanes = lanes.Assign(commit) });
        return rows;
    }

    private static string Tips(BranchHeader branch, IReadOnlyList<GitRef> refs)
    {
        var builder = new StringBuilder(branch.Oid);
        foreach (var reference in refs.OrderBy(reference => reference.Name, StringComparer.Ordinal))
            builder.Append('|').Append(reference.Name).Append('=').Append(reference.Oid);
        return builder.ToString();
    }

    private void CleanupRebaseEditorIfIdle()
    {
        SequencerKind kind;
        lock (_stateLock)
            kind = _sequencer;
        if (kind == SequencerKind.None && _gitDirectory.Length > 0)
            RebaseEditor.Cleanup(_gitDirectory);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct StatusLoad(StatusSnapshot Snapshot, TimeSpan Duration, IReadOnlySet<string> LfsPaths);

    private readonly record struct RefLoad(
        IReadOnlyList<GitRef> Refs,
        IReadOnlyList<string> Remotes,
        IReadOnlyList<StashEntry> Stashes,
        IReadOnlyList<SubmoduleEntry> Submodules,
        IReadOnlyList<WorktreeEntry> Worktrees);

    private readonly record struct LogLoad(IReadOnlyList<CommitRecord> Commits, bool Ended);
}
