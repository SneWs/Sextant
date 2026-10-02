namespace Sextant.Git.Parsing;

public readonly record struct UpstreamCounts(int Ahead, int Behind);

public static class UpstreamTrackParser
{
    public static IReadOnlyDictionary<string, UpstreamCounts> Parse(string text)
    {
        var result = new Dictionary<string, UpstreamCounts>(StringComparer.Ordinal);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            if (line.Length == 0)
                continue;
            var fields = line.Split('\t');
            if (fields.Length < 3 || fields[0].Length == 0 || fields[1].Length == 0)
                continue;
            if (!TryParse(fields[2], out var counts))
                continue;
            result[fields[0]] = counts;
        }

        return result;
    }

    public static string Suffix(int? ahead, int? behind)
    {
        if (ahead is null || behind is null || (ahead == 0 && behind == 0))
            return "";
        if (ahead > 0 && behind > 0)
            return $"  ↑{ahead}  ↓{behind}";
        if (behind > 0)
            return $"  ↓{behind}";
        return $"  ↑{ahead}";
    }

    private static bool TryParse(string track, out UpstreamCounts counts)
    {
        counts = default;
        if (track.Length == 0)
        {
            counts = new UpstreamCounts(0, 0);
            return true;
        }

        if (track.Equals("[gone]", StringComparison.Ordinal))
            return false;
        if (track.Length < 3 || track[0] != '[' || track[^1] != ']')
            return false;

        var ahead = 0;
        var behind = 0;
        foreach (var part in track[1..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length != 2 || !int.TryParse(pieces[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count))
                return false;
            if (pieces[0] == "ahead")
                ahead = count;
            else if (pieces[0] == "behind")
                behind = count;
            else
                return false;
        }

        counts = new UpstreamCounts(ahead, behind);
        return true;
    }
}
