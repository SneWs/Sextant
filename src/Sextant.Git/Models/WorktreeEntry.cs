namespace Sextant.Git.Models;

public sealed record WorktreeEntry(
    string Path,
    string? Head,
    string? Branch,
    bool Detached,
    bool Bare,
    bool Locked,
    string? LockReason);