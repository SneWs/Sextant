namespace Sextant.Git.Models;

public sealed record LfsLock(string Id, string Path, string Owner);

/// <summary>
/// Locks from <c>git lfs locks --verify</c>. Mine are the server's <c>ours</c> list.
/// Force unlock is offered when that call succeeds, because the same locking API accepts force.
/// </summary>
public sealed record LfsLockListing(
    IReadOnlyList<LfsLock> Mine,
    IReadOnlyList<LfsLock> Theirs,
    bool ForceUnlockSupported);