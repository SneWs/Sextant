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
    /// Streaming, span-based parser that walks NUL-delimited records without creating
    /// an intermediate string or an array of all commit strings. Returns each parsed
    /// <see cref="CommitRecord"/> in an eagerly-collected list so the existing API surface
    /// is preserved — callers still get an <c>IReadOnlyList&lt;CommitRecord&gt;</c> but the
    /// internal parsing avoids the giant intermediate string allocation from
    /// <c>encoding.GetString(data)</c>.
    /// </summary>
    public static IReadOnlyList<CommitRecord> Parse(byte[] data, Encoding encoding)
    {
        var commits = new List<CommitRecord>(data.Length / 150);

        int start = 0; // start of current NUL-delimited record
        while (start < data.Length)
        {
            // Find next NUL delimiter using IndexOf on the raw byte array
            int end = Array.IndexOf(data, Nul, start);
            if (end == -1)
                end = data.Length;

            int recordLength = end - start;
            if (recordLength == 0)
            {
                // Skip empty records / trailing NUL
                start = end + 1;
                continue;
            }

            // Walk the byte slice to extract fields delimited by unit separator.
            // Fields: sha(0), parents(1), unix_ts(2), author_name(3), author_email(4), subject(5)
            string? sha = null;
            long unix = 0;
            string? authorName = null;
            string? authorEmail = null;
            string? subject = null;

            // Parse parent shas into a list directly (no intermediate strings until needed)
            var parents = new List<string>();

            int fieldStart = start;
            for (int fieldIndex = 0; fieldIndex <= 5; fieldIndex++)
            {
                // Find next unit separator within this record
                int nextSep = Array.IndexOf(data, UnitSeparator, fieldStart);
                if (nextSep == -1 || nextSep >= end)
                    nextSep = end;

                int fieldLen = nextSep - fieldStart;
                if (fieldLen < 0)
                    fieldLen = 0;

                switch (fieldIndex)
                {
                    case 0: // sha — must be non-empty hex string (typically 40 chars)
                        if (fieldLen > 0 && IsValidSha(data, fieldStart, fieldLen))
                            sha = Encoding.ASCII.GetString(data, fieldStart, fieldLen);
                        break;
                    case 1: // parents — space-separated shas, may be empty
                        ParseParentShas(data, fieldStart, fieldLen, parents);
                        break;
                    case 2: // unix timestamp
                        if (TryParseLong(data, fieldStart, fieldLen, out var ts))
                            unix = ts;
                        break;
                    case 3: // author name
                        authorName = Encoding.UTF8.GetString(data, fieldStart, fieldLen);
                        break;
                    case 4: // author email
                        authorEmail = Encoding.UTF8.GetString(data, fieldStart, fieldLen);
                        break;
                    case 5: // subject
                        subject = Encoding.UTF8.GetString(data, fieldStart, fieldLen);
                        break;
                }

                fieldStart = nextSep + 1;
            }

            if (sha is null)
            {
                start = end + 1;
                continue;
            }

            commits.Add(new CommitRecord(sha, parents.Count == 0 ? [] : parents.ToArray(), unix, authorName ?? "", authorEmail ?? "", subject ?? ""));

            start = end + 1;
        }

        return commits;
    }

    private static bool IsValidSha(byte[] data, int offset, int length)
    {
        for (int i = 0; i < length; i++)
        {
            byte b = data[offset + i];
            if ((b >= '0' && b <= '9') || (b >= 'a' && b <= 'f'))
                continue;
            return false;
        }
        return true;
    }

    private static void ParseParentShas(byte[] data, int offset, int length, List<string> parents)
    {
        // Parents are space-separated hex shas. Parse directly from bytes.
        int pos = offset;
        int limit = offset + length;

        while (pos < limit)
        {
            // Skip leading spaces
            while (pos < limit && data[pos] == Space)
                pos++;
            if (pos >= limit)
                break;

            // Find the end of this potential SHA (next space or end of field)
            int shaEnd = pos;
            while (shaEnd < limit && data[shaEnd] != Space)
                shaEnd++;

            int shaLen = shaEnd - pos;
            if (shaLen >= 1 && IsValidSha(data, pos, shaLen))
            {
                parents.Add(Encoding.ASCII.GetString(data, pos, shaLen));
                pos = shaEnd;
            }
            else
            {
                // Skip non-hex garbage to next space
                pos = shaEnd;
            }
        }
    }

    private static bool TryParseLong(byte[] data, int offset, int length, out long value)
    {
        // Fast path: ASCII digits only (which is the common case for unix timestamps)
        if (length == 0)
        {
            value = 0;
            return false;
        }

        int start = offset;
        while (start < offset + length && data[start] <= ' ')
            start++; // skip leading whitespace

        if (start >= offset + length)
        {
            value = 0;
            return false;
        }

        long result = 0;
        for (int i = start; i < offset + length; i++)
        {
            byte b = data[i];
            if (b < '0' || b > '9')
                break;
            result = result * 10 + (b - '0');
        }

        value = result;
        return true;
    }

    /// <summary>
    /// Legacy string-based parser kept for backward compatibility with unit tests.
    /// For production use prefer the <c>Parse(byte[], Encoding)</c> overload which
    /// avoids a giant intermediate string allocation.
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
