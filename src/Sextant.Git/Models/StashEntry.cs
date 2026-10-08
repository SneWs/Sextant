namespace Sextant.Git.Models;

public sealed record StashEntry(string Ref, string Sha, string Subject);