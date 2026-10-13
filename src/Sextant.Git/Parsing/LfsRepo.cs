namespace Sextant.Git.Parsing;

/// <summary>
/// Whether one repository uses Git LFS. A global <c>git lfs install</c> does not.
/// </summary>
public static class LfsRepo
{
    public static bool ConfigEnables(IReadOnlyDictionary<string, string> config) =>
        HasValue(config, "lfs.url")
        || HasValue(config, "filter.lfs.clean")
        || HasValue(config, "filter.lfs.smudge")
        || HasValue(config, "filter.lfs.process");

    /// <summary>
    /// True when a line sets <c>filter=lfs</c> as its own attribute.
    /// A leading <c>#</c> is a comment. <c>filter=lfs-extra</c> does not match.
    /// </summary>
    public static bool AttributesEnable(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (HasFilterLfs(line))
                return true;
        }

        return false;
    }

    private static bool HasValue(IReadOnlyDictionary<string, string> config, string key) =>
        config.TryGetValue(key, out var value) && value.Trim().Length > 0;

    private static bool HasFilterLfs(string line)
    {
        const string token = "filter=lfs";
        for (var index = 0; index <= line.Length - token.Length; index++)
        {
            if (!line.AsSpan(index, token.Length).SequenceEqual(token))
                continue;
            var before = index == 0 || char.IsWhiteSpace(line[index - 1]);
            var after = index + token.Length == line.Length || char.IsWhiteSpace(line[index + token.Length]);
            if (before && after)
                return true;
        }

        return false;
    }
}
