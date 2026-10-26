using System.Text.RegularExpressions;

namespace Sextant.Git.Staging;

/// <summary>
/// Builds a patch that keeps the chosen added and removed lines of one hunk.
/// Staging leaves unselected additions out and keeps unselected deletions as context.
/// Unstaging keeps unselected additions as context, so the index still matches, and leaves unselected deletions out.
/// </summary>
public static partial class LinePatch
{
    public static string? Slice(string patch, int hunkIndex, int lineIndex) =>
        Slice(patch, hunkIndex, [lineIndex], reverse: false);

    public static string? Slice(string patch, int hunkIndex, IReadOnlyList<int> lineIndexes, bool reverse)
    {
        if (lineIndexes.Count == 0)
            return null;

        var normalized = patch.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var starts = new List<int>();
        for (var i = 0; i < normalized.Length; i++)
        {
            if ((i == 0 || normalized[i - 1] == '\n')
                && i + 1 < normalized.Length
                && normalized[i] == '@'
                && normalized[i + 1] == '@')
                starts.Add(i);
        }

        if (hunkIndex < 0 || hunkIndex >= starts.Count)
            throw new ArgumentOutOfRangeException(nameof(hunkIndex), "That hunk is not in the patch.");

        var header = normalized[..starts[0]];
        var end = hunkIndex + 1 < starts.Count ? starts[hunkIndex + 1] : normalized.Length;
        var body = normalized[starts[hunkIndex]..end];
        var hunkLines = body.Split('\n');
        if (hunkLines.Length == 0)
            return null;

        var hunkHeader = hunkLines[0];
        var match = HunkPattern().Match(hunkHeader);
        if (!match.Success)
            return null;

        var selected = new HashSet<int>(lineIndexes);
        var suffix = match.Groups[5].Value;
        var oldStart = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var newStartText = match.Groups[3].Value;
        var newStart = newStartText.Length == 0
            ? 0
            : int.Parse(newStartText, System.Globalization.CultureInfo.InvariantCulture);
        var rebuilt = new List<string>();
        var oldCount = 0;
        var newCount = 0;
        var changed = false;
        for (var i = 1; i < hunkLines.Length; i++)
        {
            var line = hunkLines[i];
            if (line.Length == 0 && i == hunkLines.Length - 1)
                continue;
            if (line.StartsWith("\\", StringComparison.Ordinal))
            {
                if (rebuilt.Count > 0)
                    rebuilt.Add(line);
                continue;
            }

            var index = i - 1;
            var marker = line.Length == 0 ? ' ' : line[0];
            var chosen = selected.Contains(index);
            if (marker == '+' && !chosen)
            {
                // An unselected addition is not in the index yet. Staging drops it.
                // Unstaging has to keep it, or the reverse patch cannot find the later lines.
                if (!reverse)
                    continue;
                rebuilt.Add(AsContext(line));
                oldCount++;
                newCount++;
                continue;
            }

            if (marker == '-' && !chosen)
            {
                // An unselected deletion is still in the index. Staging keeps that line as context.
                // Unstaging leaves it out so the line stays deleted.
                if (reverse)
                    continue;
                rebuilt.Add(AsContext(line));
                oldCount++;
                newCount++;
                continue;
            }

            if (marker is '+' or '-' or ' ' || line.Length == 0)
            {
                var text = marker is '+' or '-' or ' ' ? line : " " + line;
                if (line.Length == 0)
                    text = " ";
                rebuilt.Add(text);
                if (text[0] != '+')
                    oldCount++;
                if (text[0] != '-')
                    newCount++;
                if (text[0] is '+' or '-')
                    changed = true;
            }
        }

        if (!changed)
            return null;

        return header
            + FormattableString.Invariant($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@{suffix}")
            + "\n"
            + string.Join('\n', rebuilt)
            + "\n";
    }

    private static string AsContext(string line) =>
        " " + (line.Length > 0 ? line[1..] : "");

    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex HunkPattern();
}
