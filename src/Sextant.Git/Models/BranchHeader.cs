namespace Sextant.Git.Models;

public sealed record BranchHeader(
    string? Oid,
    string? HeadName,
    bool Detached,
    bool Unborn,
    string? Upstream,
    int Ahead,
    int Behind);