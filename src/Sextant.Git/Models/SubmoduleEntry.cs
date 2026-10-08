namespace Sextant.Git.Models;

public sealed record SubmoduleEntry(string Path, string Sha, string? Describe, SubmoduleState State);