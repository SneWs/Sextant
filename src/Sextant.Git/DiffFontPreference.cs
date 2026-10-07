namespace Sextant.Git;

/// <summary>The font used for diff text and the merge editor. Empty means the built-in stack.</summary>
public static class DiffFontPreference
{
    public const string Fallback = "Cascadia Mono, Consolas, DejaVu Sans Mono";

    public const double DefaultSize = 12;

    public const double MinSize = 8;

    public const double MaxSize = 48;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var trimmed = value.Trim();
        if (trimmed.Length > 200)
            return "";
        foreach (var character in trimmed)
        {
            if (char.IsControl(character) || character is '"' or '\'')
                return "";
        }

        return trimmed;
    }

    /// <summary>A family list. A chosen face is tried first, then the built-in stack.</summary>
    public static string Family(string? saved)
    {
        var name = Normalize(saved);
        if (name.Length == 0 || name.Equals(Fallback, StringComparison.OrdinalIgnoreCase))
            return Fallback;
        if (name.Contains(Fallback, StringComparison.OrdinalIgnoreCase))
            return name;
        return name + ", " + Fallback;
    }

    public static double NormalizeSize(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return DefaultSize;
        if (value < MinSize)
            return MinSize;
        if (value > MaxSize)
            return MaxSize;
        return Math.Round(value);
    }
}
