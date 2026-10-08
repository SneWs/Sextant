namespace Sextant.Git.Models;

public sealed record BlameDocument(bool IsTooLarge, IReadOnlyList<BlameLine> Lines, string? Error = null)
{
    public static BlameDocument TooLarge { get; } = new(true, []);
}