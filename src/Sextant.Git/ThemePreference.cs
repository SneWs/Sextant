namespace Sextant.Git;

/// <summary>Saved appearance. <see cref="System"/> follows the operating system.</summary>
public static class ThemePreference
{
    public const string System = "system";

    public const string Light = "light";

    public const string Dark = "dark";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Light => Light,
        Dark => Dark,
        _ => System,
    };
}
