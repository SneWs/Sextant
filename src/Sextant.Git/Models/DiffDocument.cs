namespace Sextant.Git.Models;

public sealed record DiffDocument(
    bool IsBinary,
    bool IsNewFile,
    bool IsDeleted,
    bool IsRename,
    bool IsTooLarge,
    IReadOnlyList<DiffHunk> Hunks,
    string RawPatch)
{
    public int LineCount => Hunks.Sum(hunk => hunk.Lines.Count);

    public static DiffDocument Empty { get; } = new(false, false, false, false, false, [], "");

    public static DiffDocument TooLarge { get; } = new(false, false, false, false, true, [], "");

    public static DiffDocument Binary { get; } = new(true, false, false, false, false, [], "");

    public IReadOnlyList<LfsFileNote> LfsFiles { get; init; } = [];

    /// <summary>Paths whose diff is the tool output, not the file git has. Those hunks must not be staged.</summary>
    public IReadOnlyList<string> FormattedPaths { get; init; } = [];

    public IReadOnlyList<DiffFormatNote> FormatNotes { get; init; } = [];
}