namespace Sextant.Git.Models;

public sealed record DiffLine(DiffLineKind Kind, string Text);