namespace Sextant.Git.Models;

public sealed record CommitFileChange(string Path, string? OriginalPath, ChangeKind Kind);