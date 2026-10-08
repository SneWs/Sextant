namespace Sextant.Git.AskPass;

/// <summary>
/// SSH passphrases kept for one Sextant process. Nothing is written to disk.
/// A checked passphrase is reused for later prompts in that repository, including
/// Git LFS transfers started by the same git command.
/// </summary>
public sealed class AskPassSession
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public static string KeyOf(string prompt)
    {
        var text = prompt.Trim();
        var quoted = Quoted(text);
        if (quoted is not null)
            return quoted;
        foreach (var token in text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = token.Trim('\'', '"', ':');
            if (LooksLikeKey(trimmed))
                return trimmed;
        }

        return text;
    }

    /// <summary>
    /// Returns a passphrase remembered for this repository. A different prompt in the
    /// same repository still matches, so a Git LFS transfer does not ask again.
    /// </summary>
    public bool TryReuse(string? repository, string prompt, out string? secret)
    {
        secret = null;
        if (IsIdentityPrompt(prompt))
            return false;
        var key = CacheKey(repository, prompt);
        lock (_gate)
        {
            if (!_secrets.TryGetValue(key, out var stored))
                return false;
            secret = stored;
            return true;
        }
    }

    public void Store(string? repository, string prompt, string secret, bool remember)
    {
        if (!remember || IsIdentityPrompt(prompt))
            return;
        lock (_gate)
            _secrets[CacheKey(repository, prompt)] = secret;
    }

    internal static string CacheKey(string? repository, string prompt)
    {
        if (!string.IsNullOrWhiteSpace(repository))
            return "repo\0" + Normalize(repository);
        return "key\0" + KeyOf(prompt);
    }

    private static string Normalize(string repository)
    {
        var path = repository.Trim().Replace('/', '\\').TrimEnd('\\');
        if (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]))
            return path.ToLowerInvariant();
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return path.ToLowerInvariant();
        return path;
    }

    private static bool IsIdentityPrompt(string prompt) =>
        prompt.Contains("username", StringComparison.OrdinalIgnoreCase)
        || prompt.Contains("user name", StringComparison.OrdinalIgnoreCase);

    private static string? Quoted(string text)
    {
        string? first = null;
        foreach (var mark in new[] { '\'', '"' })
        {
            var start = 0;
            while (start < text.Length)
            {
                var open = text.IndexOf(mark, start);
                if (open < 0 || open + 1 >= text.Length)
                    break;
                var close = text.IndexOf(mark, open + 1);
                if (close <= open + 1)
                    break;
                var inner = text[(open + 1)..close];
                first ??= inner;
                if (LooksLikeKey(inner))
                    return inner;
                start = close + 1;
            }
        }

        return first;
    }

    private static bool LooksLikeKey(string value) =>
        value.Contains(".ssh", StringComparison.OrdinalIgnoreCase)
        || value.Contains("id_", StringComparison.Ordinal);
}
