namespace Sextant.Git;

/// <summary>
/// SSH passphrases kept for one Sextant process. Nothing is written to disk.
/// The same prompt from the same git command means the previous answer was rejected.
/// </summary>
public sealed class AskPassSession
{
    private const int MaxCommands = 256;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _asked = new(StringComparer.Ordinal);
    private readonly Queue<string> _commands = new();

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
    /// Returns a remembered passphrase. A second ask for the same key in one git command
    /// forgets that passphrase and returns false so the dialog can be shown again.
    /// </summary>
    public bool TryReuse(string? commandId, string prompt, out string? secret, out bool rejectedRemembered)
    {
        secret = null;
        rejectedRemembered = false;
        var key = KeyOf(prompt);
        lock (_gate)
        {
            if (AlreadyAsked(commandId, key))
            {
                rejectedRemembered = _secrets.Remove(key);
                return false;
            }

            MarkAsked(commandId, key);
            if (!_secrets.TryGetValue(key, out var stored))
                return false;
            secret = stored;
            return true;
        }
    }

    public void Store(string? commandId, string prompt, string secret, bool remember)
    {
        var key = KeyOf(prompt);
        lock (_gate)
        {
            MarkAsked(commandId, key);
            if (remember)
                _secrets[key] = secret;
        }
    }

    private bool AlreadyAsked(string? commandId, string key) =>
        !string.IsNullOrEmpty(commandId)
        && _asked.TryGetValue(commandId, out var keys)
        && keys.Contains(key);

    private void MarkAsked(string? commandId, string key)
    {
        if (string.IsNullOrEmpty(commandId))
            return;
        if (!_asked.TryGetValue(commandId, out var keys))
        {
            keys = new HashSet<string>(StringComparer.Ordinal);
            _asked[commandId] = keys;
            _commands.Enqueue(commandId);
            while (_commands.Count > MaxCommands && _commands.TryDequeue(out var old))
                _asked.Remove(old);
        }

        keys.Add(key);
    }

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
