using System.Text;

namespace Sextant.Git.Parsing;

public readonly record struct DiffStat(int Files, int Added, int Removed);

public static class DiffStatText
{
    public static string Files(int count) =>
        count == 1
            ? "1 file changed"
            : count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " files changed";

    public static string Removed(int count) => "-" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string Added(int count) => "+" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Reads <c>git diff --numstat -z</c>. A binary file is <c>-</c> for both columns and still counts as one file.
/// </summary>
public static class NumStatParser
{
    public static DiffStat Parse(byte[] stdout)
    {
        var files = 0;
        long added = 0;
        long removed = 0;
        var start = 0;
        for (var i = 0; i <= stdout.Length; i++)
        {
            if (i < stdout.Length && stdout[i] != 0)
                continue;
            if (i > start && TryRecord(stdout.AsSpan(start, i - start), out var recordAdded, out var recordRemoved))
            {
                files++;
                added += recordAdded;
                removed += recordRemoved;
            }

            start = i + 1;
        }

        return new DiffStat(files, Cap(added), Cap(removed));
    }

    /// <summary>
    /// Reads <c>git log --no-walk --numstat -z --format=@@%H</c>: a <c>@@&lt;sha&gt;</c> record opens each
    /// commit and the numstat records that follow are its changed files. git glues the format's newline
    /// onto the record after each sentinel, so leading and trailing newlines are trimmed before a record
    /// is read. A commit with no numstat records (a merge) counts zero.
    /// </summary>
    public static Dictionary<string, int> ParseFileCounts(byte[] stdout)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string? sha = null;
        var start = 0;
        for (var i = 0; i <= stdout.Length; i++)
        {
            if (i < stdout.Length && stdout[i] != 0)
                continue;
            var record = TrimNewlines(stdout.AsSpan(start, i - start));
            start = i + 1;
            if (record.Length == 0)
                continue;
            if (record[0] == (byte)'@' && record.Length > 1 && record[1] == (byte)'@')
            {
                sha = Encoding.ASCII.GetString(record.Slice(2));
                counts[sha] = 0;
                continue;
            }

            if (sha is not null && IsNumStat(record))
                counts[sha]++;
        }

        return counts;
    }

    private static ReadOnlySpan<byte> TrimNewlines(ReadOnlySpan<byte> record)
    {
        var end = record.Length;
        while (end > 0 && (record[end - 1] == (byte)'\n' || record[end - 1] == (byte)'\r'))
            end--;
        var start = 0;
        while (start < end && (record[start] == (byte)'\n' || record[start] == (byte)'\r'))
            start++;
        return record.Slice(start, end - start);
    }

    private static bool IsNumStat(ReadOnlySpan<byte> record)
    {
        var tab = record.IndexOf((byte)'\t');
        if (tab <= 0)
            return false;
        var rest = record[(tab + 1)..];
        return rest.IndexOf((byte)'\t') >= 0;
    }

    private static bool TryRecord(ReadOnlySpan<byte> record, out int added, out int removed)
    {
        added = 0;
        removed = 0;
        var tab = record.IndexOf((byte)'\t');
        if (tab <= 0)
            return false;
        var rest = record[(tab + 1)..];
        var tab2 = rest.IndexOf((byte)'\t');
        if (tab2 < 0)
            return false;
        return TryCount(record[..tab], out added) && TryCount(rest[..tab2], out removed);
    }

    private static bool TryCount(ReadOnlySpan<byte> text, out int value)
    {
        value = 0;
        if (text.Length == 1 && text[0] == (byte)'-')
            return true;
        if (text.Length == 0)
            return false;
        long total = 0;
        foreach (var digit in text)
        {
            if (digit is < (byte)'0' or > (byte)'9')
                return false;
            total = total * 10 + (digit - '0');
            if (total > int.MaxValue)
                return false;
        }

        value = (int)total;
        return true;
    }

    private static int Cap(long value) => value > int.MaxValue ? int.MaxValue : (int)value;
}
