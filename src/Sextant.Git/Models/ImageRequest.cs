namespace Sextant.Git.Models;

public sealed record ImageRequest(
    string Path,
    string? BeforeRevision,
    string? AfterRevision,
    bool BeforeIsWorktree,
    bool AfterIsWorktree,
    string? BeforePath = null);