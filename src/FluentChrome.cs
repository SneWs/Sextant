using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Sextant;

/// <summary>
/// Fluent builds its chrome brushes once, from StaticResource colors. Swapping
/// <see cref="FluentTheme.Palettes"/> does not repaint those brushes, so an open window
/// keeps the old theme until the process starts again.
/// </summary>
static class FluentChrome
{
    public static void Repaint(Application app, FluentTheme fluent)
    {
        if (!fluent.Palettes.TryGetValue(ThemeVariant.Light, out var light)
            || !fluent.Palettes.TryGetValue(ThemeVariant.Dark, out var dark))
            return;

        if (fluent.Resources is ResourceDictionary resources)
            Walk(resources, light, dark);

        // The instance a control already holds is the one TryFindResource returns.
        PaintFound(app, ThemeVariant.Light, light);
        PaintFound(app, ThemeVariant.Default, light);
        PaintFound(app, ThemeVariant.Dark, dark);
    }

    static void Walk(ResourceDictionary dictionary, ColorPaletteResources light, ColorPaletteResources dark)
    {
        foreach (var pair in dictionary.ThemeDictionaries)
        {
            if (pair.Value is not ResourceDictionary themed)
                continue;
            var palette = pair.Key == ThemeVariant.Dark ? dark : light;
            Paint(themed, palette);
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (merged is ResourceDictionary child)
                Walk(child, light, dark);
        }
    }

    static void PaintFound(Application app, ThemeVariant variant, ColorPaletteResources palette)
    {
        foreach (var (colorKey, brushes) in Brushes)
        {
            var color = Read(palette, colorKey);
            foreach (var key in brushes)
            {
                if (app.TryFindResource(key, variant, out var value) && value is SolidColorBrush brush)
                    brush.Color = color;
            }
        }
    }

    static void Paint(ResourceDictionary dictionary, ColorPaletteResources palette)
    {
        foreach (var (colorKey, brushes) in Brushes)
        {
            var color = Read(palette, colorKey);
            foreach (var key in brushes)
            {
                if (dictionary.TryGetValue(key, out var value) && value is SolidColorBrush brush)
                    brush.Color = color;
            }
        }
    }

    static Color Read(ColorPaletteResources palette, string key) => key switch
    {
        "SystemAltHighColor" => palette.AltHigh,
        "SystemAltMediumColor" => palette.AltMedium,
        "SystemAltMediumHighColor" => palette.AltMediumHigh,
        "SystemAltMediumLowColor" => palette.AltMediumLow,
        "SystemBaseHighColor" => palette.BaseHigh,
        "SystemBaseLowColor" => palette.BaseLow,
        "SystemBaseMediumColor" => palette.BaseMedium,
        "SystemBaseMediumHighColor" => palette.BaseMediumHigh,
        "SystemBaseMediumLowColor" => palette.BaseMediumLow,
        "SystemChromeAltLowColor" => palette.ChromeAltLow,
        "SystemChromeBlackHighColor" => palette.ChromeBlackHigh,
        "SystemChromeBlackLowColor" => palette.ChromeBlackLow,
        "SystemChromeBlackMediumColor" => palette.ChromeBlackMedium,
        "SystemChromeBlackMediumLowColor" => palette.ChromeBlackMediumLow,
        "SystemChromeDisabledHighColor" => palette.ChromeDisabledHigh,
        "SystemChromeDisabledLowColor" => palette.ChromeDisabledLow,
        "SystemChromeGrayColor" => palette.ChromeGray,
        "SystemChromeHighColor" => palette.ChromeHigh,
        "SystemChromeLowColor" => palette.ChromeLow,
        "SystemChromeMediumColor" => palette.ChromeMedium,
        "SystemChromeMediumLowColor" => palette.ChromeMediumLow,
        "SystemChromeWhiteColor" => palette.ChromeWhite,
        "SystemErrorTextColor" => palette.ErrorText,
        "SystemListLowColor" => palette.ListLow,
        "SystemListMediumColor" => palette.ListMedium,
        "SystemRegionColor" => palette.RegionColor,
        _ => default,
    };

    static readonly Dictionary<string, string[]> Brushes = new()
    {
        ["SystemAltHighColor"] =
        [
            "SystemControlBackgroundAltHighBrush",
            "SystemControlForegroundAltHighBrush",
            "SystemControlHighlightAltAltHighBrush",
            "SystemControlPageBackgroundAltHighBrush",
        ],
        ["SystemAltMediumColor"] =
        [
            "SystemControlBackgroundAltMediumBrush",
            "SystemControlPageBackgroundAltMediumBrush",
            "SystemControlPageBackgroundMediumAltMediumBrush",
        ],
        ["SystemAltMediumHighColor"] =
        [
            "SystemControlBackgroundAltMediumHighBrush",
            "SystemControlForegroundAltMediumHighBrush",
            "SystemControlHighlightAltAltMediumHighBrush",
        ],
        ["SystemAltMediumLowColor"] = ["SystemControlBackgroundAltMediumLowBrush"],
        ["SystemBaseHighColor"] =
        [
            "SystemControlBackgroundBaseHighBrush",
            "SystemControlDisabledBaseHighBrush",
            "SystemControlForegroundBaseHighBrush",
            "SystemControlHighlightAltBaseHighBrush",
            "SystemControlHighlightBaseHighBrush",
            "SystemControlHyperlinkBaseHighBrush",
            "SystemControlPageTextBaseHighBrush",
            "ButtonBackgroundPointerOver",
            "ExpanderChevronBackgroundPointerOver",
            "RepeatButtonBackgroundPointerOver",
            "SplitButtonBackgroundPointerOver",
            "ToggleButtonBackgroundPointerOver",
        ],
        ["SystemBaseLowColor"] =
        [
            "SystemControlBackgroundBaseLowBrush",
            "SystemControlDisabledBaseLowBrush",
            "SystemControlForegroundBaseLowBrush",
            "SystemControlHighlightAltBaseLowBrush",
            "SystemControlHighlightBaseLowBrush",
            "SystemControlPageBackgroundBaseLowBrush",
        ],
        ["SystemBaseMediumColor"] =
        [
            "SystemControlBackgroundBaseMediumBrush",
            "SystemControlForegroundBaseMediumBrush",
            "SystemControlHighlightAltBaseMediumBrush",
            "SystemControlHighlightBaseMediumBrush",
            "SystemControlHyperlinkBaseMediumBrush",
            "SystemControlPageBackgroundBaseMediumBrush",
            "SystemControlPageTextBaseMediumBrush",
        ],
        ["SystemBaseMediumHighColor"] =
        [
            "SystemControlBackgroundBaseMediumHighBrush",
            "SystemControlForegroundBaseMediumHighBrush",
            "SystemControlHighlightAltBaseMediumHighBrush",
            "SystemControlHighlightBaseMediumHighBrush",
            "SystemControlHyperlinkBaseMediumHighBrush",
        ],
        ["SystemBaseMediumLowColor"] =
        [
            "SystemControlBackgroundBaseMediumLowBrush",
            "SystemControlDisabledBaseMediumLowBrush",
            "SystemControlForegroundBaseMediumLowBrush",
            "SystemControlHighlightAltBaseMediumLowBrush",
            "SystemControlHighlightBaseMediumLowBrush",
        ],
        ["SystemChromeAltLowColor"] = ["SystemControlHighlightChromeAltLowBrush"],
        ["SystemChromeBlackHighColor"] =
        [
            "SystemControlBackgroundChromeBlackHighBrush",
            "SystemControlForegroundChromeBlackHighBrush",
        ],
        ["SystemChromeBlackLowColor"] = ["SystemControlBackgroundChromeBlackLowBrush"],
        ["SystemChromeBlackMediumColor"] =
        [
            "SystemControlBackgroundChromeBlackMediumBrush",
            "SystemControlForegroundChromeBlackMediumBrush",
        ],
        ["SystemChromeBlackMediumLowColor"] =
        [
            "SystemControlBackgroundChromeBlackMediumLowBrush",
            "SystemControlForegroundChromeBlackMediumLowBrush",
            "SystemControlPageTextChromeBlackMediumLowBrush",
        ],
        ["SystemChromeDisabledHighColor"] = ["SystemControlDisabledChromeDisabledHighBrush"],
        ["SystemChromeDisabledLowColor"] =
        [
            "SystemControlDisabledChromeDisabledLowBrush",
            "SystemControlForegroundChromeDisabledLowBrush",
        ],
        ["SystemChromeGrayColor"] = ["SystemControlForegroundChromeGrayBrush"],
        ["SystemChromeHighColor"] =
        [
            "SystemControlDisabledChromeHighBrush",
            "SystemControlForegroundChromeHighBrush",
            "SystemControlHighlightChromeHighBrush",
        ],
        ["SystemChromeLowColor"] = ["SystemControlPageBackgroundChromeLowBrush"],
        ["SystemChromeMediumColor"] =
        [
            "SystemControlBackgroundChromeMediumBrush",
            "SystemControlForegroundChromeMediumBrush",
            "ScrollBarTrackFill",
            "ScrollBarTrackFillPointerOver",
            "ScrollViewerScrollBarsSeparatorBackground",
        ],
        ["SystemChromeMediumLowColor"] =
        [
            "SystemControlBackgroundChromeMediumLowBrush",
            "SystemControlDisabledChromeMediumLowBrush",
            "SystemControlPageBackgroundChromeMediumLowBrush",
            "SystemControlTransientBackgroundBrush",
        ],
        ["SystemChromeWhiteColor"] =
        [
            "SystemControlBackgroundChromeWhiteBrush",
            "SystemControlBackgroundChromeWhiteRevealBorderBrush",
            "SystemControlForegroundChromeWhiteBrush",
            "SystemControlHighlightAltChromeWhiteBrush",
            "SystemControlHighlightChromeWhiteBrush",
        ],
        ["SystemErrorTextColor"] = ["SystemControlErrorTextForegroundBrush"],
        ["SystemListLowColor"] =
        [
            "SystemControlBackgroundListLowBrush",
            "SystemControlForegroundListLowBrush",
            "SystemControlHighlightListLowBrush",
            "SystemControlPageBackgroundListLowBrush",
        ],
        ["SystemListMediumColor"] =
        [
            "SystemControlBackgroundListMediumBrush",
            "SystemControlDisabledListMediumBrush",
            "SystemControlForegroundListMediumBrush",
            "SystemControlHighlightListLowRevealBackgroundBrush",
            "SystemControlHighlightListMediumBrush",
            "SystemControlHighlightListMediumRevealBackgroundBrush",
        ],
        ["SystemRegionColor"] = ["SystemRegionBrush"],
    };
}
