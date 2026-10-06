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
