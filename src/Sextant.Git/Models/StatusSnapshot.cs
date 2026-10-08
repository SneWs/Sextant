namespace Sextant.Git.Models;

public sealed record StatusSnapshot(BranchHeader Branch, IReadOnlyList<StatusEntry> Entries);