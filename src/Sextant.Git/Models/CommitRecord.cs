namespace Sextant.Git.Models;

public sealed record CommitRecord(
    string Sha,
    IReadOnlyList<string> Parents,
    long AuthorUnixSeconds,
    string AuthorName,
    string AuthorEmail,
    string Subject);