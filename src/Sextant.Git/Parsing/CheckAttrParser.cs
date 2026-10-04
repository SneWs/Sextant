using System.Text;

namespace Sextant.Git.Parsing;

public static class CheckAttrParser
{
    public static byte[] Input(IReadOnlyList<string> paths)
    {
        using var stream = new MemoryStream();
        foreach (var path in paths)
        {
            var bytes = Encoding.UTF8.GetBytes(path);
            stream.Write(bytes);
            stream.WriteByte(0);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Paths whose <c>filter</c> attribute is <c>lfs</c>. Records are <c>path NUL filter NUL value NUL</c>.
    /// </summary>
    public static HashSet<string> LfsTracked(ReadOnlySpan<byte> data)
    {
        var tracked = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        while (index < data.Length)
        {
            if (!Field(data, ref index, out var path) || !Field(data, ref index, out var attribute) || !Field(data, ref index, out var value))
                break;
            if (path.Length > 0 && attribute == "filter" && value == "lfs")
                tracked.Add(path);
        }

        return tracked;
    }

    public static bool IsTracked(IReadOnlySet<string> tracked, string path, string? original) =>
        tracked.Contains(path) || (original is { Length: > 0 } && tracked.Contains(original));

    private static bool Field(ReadOnlySpan<byte> data, ref int index, out string value)
    {
        if (index >= data.Length)
        {
            value = "";
            return false;
        }

        var end = data[index..].IndexOf((byte)0);
        if (end < 0)
        {
            value = "";
            return false;
        }

        value = Encoding.UTF8.GetString(data.Slice(index, end));
        index += end + 1;
        return true;
    }
}
