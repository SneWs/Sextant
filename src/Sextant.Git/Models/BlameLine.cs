namespace Sextant.Git.Models;

public sealed record BlameLine(int Number, string Sha, string Author, string Summary, string Text, bool Uncommitted);