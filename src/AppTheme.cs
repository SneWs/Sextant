using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Sextant.Git;
using Sextant.Git.Workspace;

namespace Sextant;

/// <summary>Applies the saved appearance to the Fluent theme.</summary>
public static class AppTheme
{
    public static void Apply(string? theme, string? palette)
    {
        var choices = ThemeFiles.Choices(AppPaths.ThemesDirectory());
        var id = PalettePreference.Normalize(palette);
        var choice = choices.FirstOrDefault(item => item.Id == id) ?? choices[0];
        ThemeXaml.Install(choice);
        if (Application.Current is not { } app)
            return;
        
        app.RequestedThemeVariant = Variant(theme);
        if (app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;
        
        foreach (var window in desktop.Windows)
            Invalidate(window);
    }

    private static ThemeVariant Variant(string? theme) => ThemePreference.Normalize(theme) switch
    {
        ThemePreference.Light => ThemeVariant.Light,
        ThemePreference.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    private static void Invalidate(Visual visual)
    {
        visual.InvalidateVisual();
        foreach (var child in visual.GetVisualChildren())
            Invalidate(child);
        if (visual is Window window)
        {
            foreach (var owned in window.OwnedWindows)
                Invalidate(owned);
        }
    }
}
