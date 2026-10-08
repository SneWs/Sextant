namespace Sextant.Git.Models;

public sealed record StatusEntry(
    string Path,
    string? OriginalPath,
    ChangeKind Kind,
    bool Staged,
    bool Unstaged,
    char IndexStatus,
    char WorkTreeStatus);