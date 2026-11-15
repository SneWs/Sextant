using System.Globalization;
using System.Text;
using Sextant.Git.Models;

namespace Sextant.Git.Parsing;

public static class LogParser
{
    private const byte Nul = 0;
    private const byte UnitSeparator = (byte)'\u001f';
    private const byte Space = (byte)' ';

    /// <summary>
    /// Walks NUL-delimited records in <paramref name="data"/> without decoding the whole buffer as one string.
    /// </summary>
    public static IReadOnlyList<CommitRecord> Parse(byte[] data, Encoding encoding)
    {
        _ = encoding;
        var commits = new List<CommitRecord>(data.Length / 150);
        AppendRecords(data, commits, flushTail: true);
        return commits;
    }

    /// <summary>
    /// Reads NUL-delimited <c>git log -z</c> records from <paramref name="stream"/>.
    /// Finished records are parsed and their bytes dropped. Only the current incomplete record stays buffered.
    /// </summary>
    public static async Task<IReadOnlyList<CommitRecord>> ParseAsync(
        Stream stream,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        _ = encoding;
        var commits = new List<CommitRecord>();
        var buffer = new byte[8192];
        var length = 0;
        while (true)
        {
            if (length == buffer.Length)
            {
                var grown = new byte[buffer.Length * 2];
                Buffer.BlockCopy(buffer, 0, grown, 0, length);
                buffer = grown;
            }

            var read = await stream.ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                if (length > 0)
                    AppendRecords(buffer.AsSpan(0, length), commits, flushTail: true);
                return commits;
            }

            length += read;
            var consumed = AppendRecords(buffer.AsSpan(0, length), commits, flushTail: false);
            if (consumed <= 0)
                continue;
            length -= consumed;
            if (length > 0)
                Buffer.BlockCopy(buffer, consumed, buffer, 0, length);
        }
    }

    /// <summary>
    /// Appends every complete record. Returns how many leading bytes were consumed.
    /// When <paramref name="flushTail"/> is false, a trailing record with no NUL stays unconsumed.
    /// </summary>
    private static int AppendRecords(ReadOnlySpan<byte> data, List<CommitRecord> commits, bool flushTail)
    {
        var start = 0;
        while (start < data.Length)
        {
            var relative = data[start..].IndexOf(Nul);
            if (relative < 0)
            {
                if (!flushTail)
                    return start;
                TryAdd(data[start..], commits);
                return data.Length;
            }

            if (relative > 0)
                TryAdd(data.Slice(start, relative), commits);
            start += relative + 1;
        }

        return start;
    }

    private static void TryAdd(ReadOnlySpan<byte> record, List<CommitRecord> commits)
    {
        // Fields: sha, parents, unix time, author name, author email, subject.
        string? sha = null;
        long unix = 0;
        string? authorName = null;
        string? authorEmail = null;
        string? subject = null;
        var parents = new List<string>();
        var fieldStart = 0;
        for (var fieldIndex = 0; fieldIndex <= 5; fieldIndex++)
        {
            if (fieldStart > record.Length)
                break;
            var rest = record[fieldStart..];
            var separator = rest.IndexOf(UnitSeparator);
            var field = separator < 0 ? rest : rest[..separator];
            switch (fieldIndex)
            {
                case 0:
                    if (IsValidSha(field))
                        sha = Encoding.ASCII.GetString(field);
                    break;
                case 1:
                    ParseParentShas(field, parents);
                    break;
                case 2:
                    if (TryParseLong(field, out var ts))
                        unix = ts;
                    break;
                case 3:
                    authorName = Encoding.UTF8.GetString(field);
                    break;
                case 4:
                    authorEmail = Encoding.UTF8.GetString(field);
                    break;
                case 5:
                    subject = Encoding.UTF8.GetString(field);
                    break;
            }

            if (separator < 0)
                break;
            fieldStart += separator + 1;
        }

        if (sha is null)
            return;
        commits.Add(new CommitRecord(
            sha,
            parents.Count == 0 ? [] : parents.ToArray(),
            unix,
            authorName ?? "",
            authorEmail ?? "",
            subject ?? ""));
    }

    private static bool IsValidSha(ReadOnlySpan<byte> field)
    {
        if (field.Length == 0)
            return false;
        foreach (var b in field)
        {
            if ((b >= '0' && b <= '9') || (b >= 'a' && b <= 'f'))
                continue;
            return false;
        }

        return true;
    }

    private static void ParseParentShas(ReadOnlySpan<byte> field, List<string> parents)
    {
        var pos = 0;
        while (pos < field.Length)
        {
            while (pos < field.Length && field[pos] == Space)
                pos++;
            if (pos >= field.Length)
                break;
            var shaEnd = pos;
            while (shaEnd < field.Length && field[shaEnd] != Space)
                shaEnd++;
            var sha = field[pos..shaEnd];
            if (IsValidSha(sha))
                parents.Add(Encoding.ASCII.GetString(sha));
            pos = shaEnd;
        }
    }

    private static bool TryParseLong(ReadOnlySpan<byte> field, out long value)
    {
        if (field.Length == 0)
        {
            value = 0;
            return false;
        }

        var start = 0;
        while (start < field.Length && field[start] <= (byte)' ')
            start++;
        if (start >= field.Length)
        {
            value = 0;
            return false;
        }

        long result = 0;
        for (var i = start; i < field.Length; i++)
        {
            var b = field[i];
            if (b < (byte)'0' || b > (byte)'9')
                break;
            result = result * 10 + (b - (byte)'0');
        }

        value = result;
        return true;
    }

    /// <summary>
    /// String parser kept for tests that already hold the log as text.
    /// Production log loads use <see cref="ParseAsync"/> so the process buffer is not retained.
    /// </summary>
    public static IReadOnlyList<CommitRecord> Parse(string text)
    {
        var commits = new List<CommitRecord>(text.Length / 150);
        foreach (var record in text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.Split('\u001f');
            if (fields.Length < 6 || fields[0].Length == 0)
                continue;
            if (!long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
                unix = 0;
            var parents = fields[1].Length == 0
                ? []
                : fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            commits.Add(new CommitRecord(fields[0], parents, unix, fields[3], fields[4], fields[5]));
        }

        return commits;
    }

    public static bool IsUnborn(string standardError) =>
        standardError.Contains("does not have any commits yet", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("ambiguous argument 'HEAD'", StringComparison.OrdinalIgnoreCase);
}
