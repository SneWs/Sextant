using Sextant.Git.Models;

namespace Sextant.Git.Diff;

/// <summary>
/// Line numbers for one hunk. A start of 0, which git uses for a new or deleted side, stays blank.
/// </summary>
public static class DiffLineNumbers
{
    public readonly record struct LineNumber(string Old, string New);

    /// <summary>
    /// One side-by-side row. A null side is not in this row. An empty string is a blank line.
    /// </summary>
    public readonly record struct SideLine(
        string? LeftText,
        string LeftNumber,
        bool LeftRemoved,
        string? RightText,
        string RightNumber,
        bool RightAdded);

    public static LineNumber For(DiffLineKind kind, ref int oldLine, ref int newLine) => kind switch
    {
        DiffLineKind.Context => new(Take(ref oldLine), Take(ref newLine)),
        DiffLineKind.Removed => new(Take(ref oldLine), ""),
        DiffLineKind.Added => new("", Take(ref newLine)),
        _ => new("", ""),
    };

    public static List<SideLine> SideBySide(DiffHunk hunk)
    {
        var rows = new List<SideLine>();
        var removed = new Queue<(string Text, string Number)>();
        var oldLine = hunk.OldStart;
        var newLine = hunk.NewStart;
        foreach (var line in hunk.Lines)
        {
            switch (line.Kind)
            {
                case DiffLineKind.Removed:
                    removed.Enqueue((line.Text, Take(ref oldLine)));
                    break;
                case DiffLineKind.Added:
                    var added = Take(ref newLine);
                    if (removed.Count > 0)
                    {
                        var prior = removed.Dequeue();
                        rows.Add(new SideLine(prior.Text, prior.Number, true, line.Text, added, true));
                    }
                    else
                    {
                        rows.Add(new SideLine(null, "", false, line.Text, added, true));
                    }

                    break;
                case DiffLineKind.Context:
                    Flush();
                    rows.Add(new SideLine(line.Text, Take(ref oldLine), false, line.Text, Take(ref newLine), false));
                    break;
            }
        }

        Flush();
        return rows;

        void Flush()
        {
            while (removed.Count > 0)
            {
                var prior = removed.Dequeue();
                rows.Add(new SideLine(prior.Text, prior.Number, true, null, "", false));
            }
        }
    }

    /// <summary>
    /// Number for one displayed row of a whole file. The empty row after a trailing newline is not a line.
    /// </summary>
    public static string FileLine(int index, int rowCount, bool endsWithNewline)
    {
        if (index < 0 || index >= rowCount)
            return "";
        if (endsWithNewline && index == rowCount - 1)
            return "";
        return (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Take(ref int line)
    {
        if (line <= 0)
            return "";
        var text = line.ToString(System.Globalization.CultureInfo.InvariantCulture);
        line++;
        return text;
    }
}
