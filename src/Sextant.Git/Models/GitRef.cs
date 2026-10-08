namespace Sextant.Git.Models;

public sealed record GitRef(string Oid, string Name, bool IsHead, string? Upstream, int? Ahead = null, int? Behind = null);