namespace Sextant.Git;

/// <summary>A built-in or user color theme. Light and dark are chosen separately.</summary>
public sealed class ThemeChoice
{
    public ThemeChoice(string id, string title, string? path)
    {
        Id = id;
        Title = title;
        Path = path;
    }

    public string Id { get; }

    public string Title { get; }

    /// <summary>A user .xaml file that replaces the built-in palette. Null uses the built-in palette.</summary>
    public string? Path { get; }

    public override string ToString() => Title;
}

/// <summary>Saved color theme. Light, dark, and follow-system stay on <see cref="ThemePreference"/>.</summary>
public static class PalettePreference
{
    public const string Catppuccin = "catppuccin";

    public const string Gruvbox = "gruvbox";

    public const string Monokai = "monokai";

    public const string TokyoNight = "tokyonight";

    public const string Dracula = "dracula";

    public const string GitHub = "github";

    public const string Black = "black";

    public static IReadOnlyList<ThemeChoice> BuiltIn { get; } =
    [
        new(Catppuccin, "Catppuccin", null),
        new(Gruvbox, "Gruvbox", null),
        new(Monokai, "Monokai", null),
        new(TokyoNight, "Tokyo Night", null),
        new(Dracula, "Dracula", null),
        new(GitHub, "GitHub", null),
        new(Black, "Black", null),
    ];

    public static string Normalize(string? value) => IdFromStem(value) ?? Catppuccin;

    public static bool IsBuiltIn(string? id) =>
        BuiltIn.Any(choice => string.Equals(choice.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// A theme id is the file name without its extension, in lower case.
    /// Spaces and underscores become hyphens. Anything else is refused.
    /// </summary>
    public static string? IdFromStem(string? stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
            return null;
        var built = new System.Text.StringBuilder();
        foreach (var character in stem.Trim())
        {
            if (char.IsAsciiLetterOrDigit(character))
                built.Append(char.ToLowerInvariant(character));
            else if (character is ' ' or '-' or '_')
                built.Append('-');
            else
                return null;
        }

        var id = built.ToString().Trim('-');
        while (id.Contains("--", StringComparison.Ordinal))
            id = id.Replace("--", "-", StringComparison.Ordinal);
        if (id.Length is 0 or > 64 || !char.IsAsciiLetter(id[0]))
            return null;
        return id;
    }
}
