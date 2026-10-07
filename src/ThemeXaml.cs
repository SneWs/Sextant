using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Sextant.Git;

namespace Sextant;

/// <summary>Loads a theme file with Avalonia's runtime XAML loader and installs its palettes.</summary>
public static class ThemeXaml
{
    public const string OnAccentKey = "OnAccentBrush";

    public const string AddedPillKey = "CommitAddedPillBrush";

    public const string AddedTextKey = "CommitAddedTextBrush";

    public const string RemovedPillKey = "CommitRemovedPillBrush";

    public const string RemovedTextKey = "CommitRemovedTextBrush";

    public static bool TryValidate(ThemeChoice choice, out string error)
    {
        if (choice.Path is null)
        {
            error = "";
            return true;
        }

        try
        {
            Read(LoadFile(choice.Path));
            error = "";
            return true;
        }
        catch (Exception exception)
        {
            error = FirstLine(exception.Message);
            return false;
        }
    }

    public static void Install(ThemeChoice choice)
    {
        if (choice.Path is not null)
        {
            try
            {
                Install(Read(LoadFile(choice.Path)));
                return;
            }
            catch (Exception)
            {
            }
        }

        Install(BuiltInPalettes.For(choice.Id));
    }

    public static ResourceDictionary LoadFile(string path)
    {
        using var stream = File.OpenRead(path);
        var loaded = AvaloniaRuntimeXamlLoader.Load(stream, localAssembly: typeof(ThemeXaml).Assembly, uri: new Uri(Path.GetFullPath(path)));
        if (loaded is not ResourceDictionary dictionary)
            throw new InvalidOperationException("A theme file must be a ResourceDictionary.");
        return dictionary;
    }

    static (PaletteColors Light, PaletteColors Dark) Read(ResourceDictionary source)
    {
        var light = Palette(source, "Light") ?? throw new InvalidOperationException("The theme file has no Light palette.");
        var dark = Palette(source, "Dark") ?? throw new InvalidOperationException("The theme file has no Dark palette.");
        return (
            WithBrushes(light, Nested(source, "Light"), fallbackDark: false),
            WithBrushes(dark, Nested(source, "Dark"), fallbackDark: true));
    }

    static void Install((PaletteColors Light, PaletteColors Dark) pair)
    {
        if (Application.Current is not { } app)
            return;
        var fluent = app.Styles.OfType<FluentTheme>().FirstOrDefault();
        if (fluent is null)
            return;

        Put(fluent.Palettes, ThemeVariant.Light, pair.Light.Resources);
        Put(fluent.Palettes, ThemeVariant.Dark, pair.Dark.Resources);
        Paint(app, ThemeVariant.Light, pair.Light);
        Paint(app, ThemeVariant.Dark, pair.Dark);
        FluentChrome.Repaint(app, fluent);
    }

    /// <summary>
    /// Copies colors onto a new palette. A file's palette is owned by the loaded dictionary, and Fluent refuses a second owner.
    /// Replacing the entry notifies the theme.
    /// </summary>
    static void Put(IDictionary<ThemeVariant, ColorPaletteResources> palettes, ThemeVariant variant, ColorPaletteResources source)
    {
        palettes[variant] = Clone(source);
    }

    static ColorPaletteResources Clone(ColorPaletteResources source) => new()
    {
        Accent = source.Accent,
        AltHigh = source.AltHigh,
        AltLow = source.AltLow,
        AltMedium = source.AltMedium,
        AltMediumHigh = source.AltMediumHigh,
        AltMediumLow = source.AltMediumLow,
        BaseHigh = source.BaseHigh,
        BaseLow = source.BaseLow,
        BaseMedium = source.BaseMedium,
        BaseMediumHigh = source.BaseMediumHigh,
        BaseMediumLow = source.BaseMediumLow,
        ChromeAltLow = source.ChromeAltLow,
        ChromeBlackHigh = source.ChromeBlackHigh,
        ChromeBlackLow = source.ChromeBlackLow,
        ChromeBlackMedium = source.ChromeBlackMedium,
        ChromeBlackMediumLow = source.ChromeBlackMediumLow,
        ChromeDisabledHigh = source.ChromeDisabledHigh,
        ChromeDisabledLow = source.ChromeDisabledLow,
        ChromeGray = source.ChromeGray,
        ChromeHigh = source.ChromeHigh,
        ChromeLow = source.ChromeLow,
        ChromeMedium = source.ChromeMedium,
        ChromeMediumLow = source.ChromeMediumLow,
        ChromeWhite = source.ChromeWhite,
        ErrorText = source.ErrorText,
        ListLow = source.ListLow,
        ListMedium = source.ListMedium,
        RegionColor = source.RegionColor,
    };

    static ColorPaletteResources? Palette(ResourceDictionary source, string name)
    {
        if (source.TryGetValue(name, out var value) && value is ColorPaletteResources palette)
            return palette;
        var variant = name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        if (source.TryGetResource(variant, null, out value) && value is ColorPaletteResources themed)
            return themed;
        return null;
    }

    static ResourceDictionary? Nested(ResourceDictionary source, string name)
    {
        if (source.ThemeDictionaries is null)
            return null;
        var variant = name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        if (source.ThemeDictionaries.TryGetValue(variant, out var found) && found is ResourceDictionary dictionary)
            return dictionary;
        return null;
    }

    static PaletteColors WithBrushes(ColorPaletteResources resources, ResourceDictionary? brushes, bool fallbackDark)
    {
        var paper = resources.ChromeMediumLow;
        return new PaletteColors
        {
            Resources = resources,
            OnAccent = ColorOf(brushes, OnAccentKey, "CatppuccinOnAccentBrush") ?? (fallbackDark ? paper : Colors.White),
            AddedPill = ColorOf(brushes, AddedPillKey) ?? (fallbackDark ? Color.Parse("#244032") : Color.Parse("#D8F5DC")),
            AddedText = ColorOf(brushes, AddedTextKey) ?? (fallbackDark ? Color.Parse("#A6E3A1") : Color.Parse("#2B8A3E")),
            RemovedPill = ColorOf(brushes, RemovedPillKey) ?? (fallbackDark ? Color.Parse("#4A2C35") : Color.Parse("#FDE2E4")),
            RemovedText = ColorOf(brushes, RemovedTextKey) ?? resources.ErrorText,
        };
    }

    static Color? ColorOf(ResourceDictionary? brushes, params string[] keys)
    {
        if (brushes is null)
            return null;
        foreach (var key in keys)
        {
            if (!brushes.TryGetValue(key, out var value))
                continue;
            if (value is SolidColorBrush brush)
                return brush.Color;
            if (value is Color color)
                return color;
        }

        return null;
    }

    static void Paint(Application app, ThemeVariant variant, PaletteColors colors)
    {
        if (!app.Resources.ThemeDictionaries.TryGetValue(variant, out var found) || found is not ResourceDictionary dictionary)
        {
            dictionary = new ResourceDictionary();
            app.Resources.ThemeDictionaries[variant] = dictionary;
        }

        Set(dictionary, OnAccentKey, colors.OnAccent);
        Set(dictionary, "CatppuccinOnAccentBrush", colors.OnAccent);
        Set(dictionary, AddedPillKey, colors.AddedPill);
        Set(dictionary, AddedTextKey, colors.AddedText);
        Set(dictionary, RemovedPillKey, colors.RemovedPill);
        Set(dictionary, RemovedTextKey, colors.RemovedText);
    }

    static void Set(ResourceDictionary dictionary, string key, Color color)
    {
        if (dictionary.TryGetValue(key, out var existing) && existing is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        dictionary[key] = new SolidColorBrush(color);
    }

    static string FirstLine(string message)
    {
        var line = message.Split('\n', 2)[0].Trim();
        return line.Length == 0 ? "That theme file could not be loaded." : "That theme file could not be loaded. " + line;
    }
}
