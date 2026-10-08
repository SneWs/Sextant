namespace Sextant.Git.Models;

public sealed class SessionState
{
    public required BranchHeader Branch { get; init; }

    public required IReadOnlyList<StatusEntry> Entries { get; init; }

    public required IReadOnlyList<GitRef> Refs { get; init; }

    public required IReadOnlyList<GraphCommit> Commits { get; init; }

    public required bool HistoryEnded { get; init; }

    public required bool HistoryCapped { get; init; }

    public required int HistoryGeneration { get; init; }

    public required bool MergeInProgress { get; init; }

    public required SequencerKind Sequencer { get; init; }

    public required string? MergeMessage { get; init; }

    public required string? HistoryLabel { get; init; }

    /// <summary>A file history or a search is limiting the graph. Hidden branches alone leave this false.</summary>
    public required bool HasHistoryQuery { get; init; }

    /// <summary>Search results are a flat list. A branch pin and the normal graph keep their lanes.</summary>
    public required bool FlatHistory { get; init; }

    /// <summary>Full ref names left out of the commit graph. Empty shows every branch.</summary>
    public required IReadOnlySet<string> HiddenBranches { get; init; }

    public required IReadOnlyList<StashEntry> Stashes { get; init; }

    public required TimeSpan LastStatusDuration { get; init; }

    public required IReadOnlyDictionary<string, string> Config { get; init; }

    public required IReadOnlyList<string> Remotes { get; init; }

    public required IReadOnlyList<CommandLogEntry> Commands { get; init; }

    public required PerformanceSuggestion? Suggestion { get; init; }

    public required IReadOnlyList<SubmoduleEntry> Submodules { get; init; }

    public required IReadOnlyList<WorktreeEntry> Worktrees { get; init; }

    public required bool SparseCheckout { get; init; }

    /// <summary>Worktree paths whose filter attribute is lfs. Empty when nothing in the status list is tracked.</summary>
    public required IReadOnlySet<string> LfsPaths { get; init; }
}