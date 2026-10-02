using Avalonia;
using Avalonia.Styling;
using Sextant.Git;

namespace Sextant;

/// <summary>Applies the saved appearance to the Fluent theme.</summary>
public static class AppTheme
{
    public static ThemeVariant Variant(string? theme) => ThemePreference.Normalize(theme) switch
    {
        ThemePreference.Light => ThemeVariant.Light,
        ThemePreference.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    public static void Apply(string? theme)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = Variant(theme);
    }
}
