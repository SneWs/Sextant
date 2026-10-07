using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace Sextant;

/// <summary>One light or dark Fluent palette, plus the brushes Sextant paints itself.</summary>
sealed class PaletteColors
{
    public required ColorPaletteResources Resources { get; init; }

    public required Color OnAccent { get; init; }

    public required Color AddedPill { get; init; }

    public required Color AddedText { get; init; }

    public required Color RemovedPill { get; init; }

    public required Color RemovedText { get; init; }
}

static class BuiltInPalettes
{
    public static (PaletteColors Light, PaletteColors Dark) For(string id) => id switch
    {
        Git.PalettePreference.Gruvbox => (GruvboxLight(), GruvboxDark()),
        Git.PalettePreference.Monokai => (MonokaiLight(), MonokaiDark()),
        Git.PalettePreference.TokyoNight => (TokyoDay(), TokyoNight()),
        Git.PalettePreference.Dracula => (DraculaLight(), DraculaDark()),
        Git.PalettePreference.GitHub => (GitHubLight(), GitHubDark()),
        Git.PalettePreference.Black => (BlackLight(), BlackDark()),
        _ => (CatppuccinLight(), CatppuccinDark()),
    };

    static PaletteColors CatppuccinLight() => Make(
        dark: false,
        accent: "#1E66F5",
        text: "#4C4F69",
        chromeLow: "#EFF1F5",
        chromeMedium: "#E6E9EF",
        chromeMediumLow: "#EFF1F5",
        chromeHigh: "#ACB0BE",
        chromeGray: "#8C8FA1",
        disabledHigh: "#CCD0DA",
        disabledLow: "#9CA0B0",
        error: "#D20F39",
        onAccent: "#EFF1F5",
        addedPill: "#D8F5DC",
        addedText: "#2B8A3E",
        removedPill: "#FDE2E4",
        removedText: "#C92A2A");

    static PaletteColors CatppuccinDark() => Make(
        dark: true,
        accent: "#89B4FA",
        text: "#CDD6F4",
        chromeLow: "#11111B",
        chromeMedium: "#181825",
        chromeMediumLow: "#1E1E2E",
        chromeHigh: "#585B70",
        chromeGray: "#7F849C",
        disabledHigh: "#313244",
        disabledLow: "#6C7086",
        error: "#F38BA8",
        onAccent: "#1E1E2E",
        addedPill: "#244032",
        addedText: "#A6E3A1",
        removedPill: "#4A2C35",
        removedText: "#F38BA8");

    static PaletteColors GruvboxLight() => Make(
        dark: false,
        accent: "#076678",
        text: "#3C3836",
        chromeLow: "#F9F5D7",
        chromeMedium: "#EBDBB2",
        chromeMediumLow: "#FBF1C7",
        chromeHigh: "#D5C4A1",
        chromeGray: "#928374",
        disabledHigh: "#D5C4A1",
        disabledLow: "#7C6F64",
        error: "#9D0006",
        onAccent: "#FBF1C7",
        addedPill: "#E6EDC5",
        addedText: "#79740E",
        removedPill: "#F5D5C8",
        removedText: "#9D0006");

    static PaletteColors GruvboxDark() => Make(
        dark: true,
        accent: "#83A598",
        text: "#EBDBB2",
        chromeLow: "#1D2021",
        chromeMedium: "#3C3836",
        chromeMediumLow: "#282828",
        chromeHigh: "#504945",
        chromeGray: "#928374",
        disabledHigh: "#665C54",
        disabledLow: "#7C6F64",
        error: "#FB4934",
        onAccent: "#1D2021",
        addedPill: "#3A4428",
        addedText: "#B8BB26",
        removedPill: "#4A2C28",
        removedText: "#FB4934");

    static PaletteColors MonokaiLight() => Make(
        dark: false,
        accent: "#2188B6",
        text: "#272822",
        chromeLow: "#F4F4EC",
        chromeMedium: "#EFEFE6",
        chromeMediumLow: "#F8F8F2",
        chromeHigh: "#E2E2D6",
        chromeGray: "#75715E",
        disabledHigh: "#E2E2D6",
        disabledLow: "#908E82",
        error: "#F92672",
        onAccent: "#F8F8F2",
        addedPill: "#E7F5C8",
        addedText: "#5A8A00",
        removedPill: "#FDE2EA",
        removedText: "#C41A5B");

    static PaletteColors MonokaiDark() => Make(
        dark: true,
        accent: "#66D9EF",
        text: "#F8F8F2",
        chromeLow: "#1E1F1C",
        chromeMedium: "#3E3D32",
        chromeMediumLow: "#272822",
        chromeHigh: "#49483E",
        chromeGray: "#75715E",
        disabledHigh: "#3E3D32",
        disabledLow: "#75715E",
        error: "#F92672",
        onAccent: "#272822",
        addedPill: "#2E3A22",
        addedText: "#A6E22E",
        removedPill: "#4A2433",
        removedText: "#F92672");

    static PaletteColors TokyoDay() => Make(
        dark: false,
        accent: "#2E7DE9",
        text: "#3760BF",
        chromeLow: "#D0D5E3",
        chromeMedium: "#C4C8DA",
        chromeMediumLow: "#E1E2E7",
        chromeHigh: "#A8AECB",
        chromeGray: "#848CB5",
        disabledHigh: "#C4C8DA",
        disabledLow: "#848CB5",
        error: "#F52A65",
        onAccent: "#E1E2E7",
        addedPill: "#D5E6C8",
        addedText: "#587539",
        removedPill: "#F8D5DF",
        removedText: "#F52A65");

    static PaletteColors TokyoNight() => Make(
        dark: true,
        accent: "#7AA2F7",
        text: "#C0CAF5",
        chromeLow: "#16161E",
        chromeMedium: "#292E42",
        chromeMediumLow: "#1A1B26",
        chromeHigh: "#414868",
        chromeGray: "#565F89",
        disabledHigh: "#292E42",
        disabledLow: "#565F89",
        error: "#F7768E",
        onAccent: "#1A1B26",
        addedPill: "#20362A",
        addedText: "#9ECE6A",
        removedPill: "#3F2A36",
        removedText: "#F7768E");

    static PaletteColors DraculaLight() => Make(
        dark: false,
        accent: "#6C4AB5",
        text: "#282A36",
        chromeLow: "#FFFFFF",
        chromeMedium: "#F0F0EA",
        chromeMediumLow: "#F8F8F2",
        chromeHigh: "#D6D6D0",
        chromeGray: "#6272A4",
        disabledHigh: "#E6E6E0",
        disabledLow: "#6272A4",
        error: "#E23D3D",
        onAccent: "#F8F8F2",
        addedPill: "#D9F5E3",
        addedText: "#1F7A3A",
        removedPill: "#FDE2E4",
        removedText: "#C92A2A");

    static PaletteColors DraculaDark() => Make(
        dark: true,
        accent: "#BD93F9",
        text: "#F8F8F2",
        chromeLow: "#21222C",
        chromeMedium: "#44475A",
        chromeMediumLow: "#282A36",
        chromeHigh: "#6272A4",
        chromeGray: "#6272A4",
        disabledHigh: "#44475A",
        disabledLow: "#6272A4",
        error: "#FF5555",
        onAccent: "#282A36",
        addedPill: "#2A3C32",
        addedText: "#50FA7B",
        removedPill: "#4A2C35",
        removedText: "#FF5555");

    // GitHub Theme light default and dark default (primer/github-vscode-theme, Primer 7.10).
    // Accent is the theme focus and badge blue, not the green primary button.
    static PaletteColors GitHubLight() => Make(
        dark: false,
        accent: "#0969DA",
        text: "#1F2328",
        chromeLow: "#F6F8FA",
        chromeMedium: "#EAEEF2",
        chromeMediumLow: "#FFFFFF",
        chromeHigh: "#D0D7DE",
        chromeGray: "#656D76",
        disabledHigh: "#D0D7DE",
        disabledLow: "#6E7781",
        error: "#CF222E",
        onAccent: "#FFFFFF",
        addedPill: "#E6FFEC",
        addedText: "#1A7F37",
        removedPill: "#FFEBE9",
        removedText: "#CF222E");

    static PaletteColors GitHubDark() => Make(
        dark: true,
        accent: "#1F6FEB",
        text: "#E6EDF3",
        chromeLow: "#010409",
        chromeMedium: "#161B22",
        chromeMediumLow: "#0D1117",
        chromeHigh: "#30363D",
        chromeGray: "#7D8590",
        disabledHigh: "#21262D",
        disabledLow: "#6E7681",
        error: "#F85149",
        onAccent: "#FFFFFF",
        addedPill: "#1C4328",
        addedText: "#3FB950",
        removedPill: "#542426",
        removedText: "#F85149");

    // Jaakko.black is dark only. The light palette keeps the mint accent and pink error, on white.
    static PaletteColors BlackLight() => Make(
        dark: false,
        accent: "#1A6E70",
        text: "#1A3038",
        chromeLow: "#F4F8F9",
        chromeMedium: "#E4EEF0",
        chromeMediumLow: "#FFFFFF",
        chromeHigh: "#C5D5D8",
        chromeGray: "#5E727A",
        disabledHigh: "#E4EEF0",
        disabledLow: "#7A9096",
        error: "#A33D66",
        onAccent: "#FFFFFF",
        addedPill: "#E7F5D4",
        addedText: "#2C401B",
        removedPill: "#F8D6D6",
        removedText: "#8F1D1D");

    static PaletteColors BlackDark() => Make(
        dark: true,
        accent: "#A8DADC",
        text: "#BFD4E1",
        chromeLow: "#000000",
        chromeMedium: "#222222",
        chromeMediumLow: "#000000",
        chromeHigh: "#353535",
        chromeGray: "#999999",
        disabledHigh: "#222222",
        disabledLow: "#6B6B6B",
        error: "#D97397",
        onAccent: "#000000",
        addedPill: "#2C401B",
        addedText: "#CAFFBF",
        removedPill: "#410202",
        removedText: "#FFADAD");

    static PaletteColors Make(
        bool dark,
        string accent,
        string text,
        string chromeLow,
        string chromeMedium,
        string chromeMediumLow,
        string chromeHigh,
        string chromeGray,
        string disabledHigh,
        string disabledLow,
        string error,
        string onAccent,
        string addedPill,
        string addedText,
        string removedPill,
        string removedText)
    {
        var ink = Color.Parse(text);
        var alt = dark ? Colors.Black : Colors.White;
        return new PaletteColors
        {
            OnAccent = Color.Parse(onAccent),
            AddedPill = Color.Parse(addedPill),
            AddedText = Color.Parse(addedText),
            RemovedPill = Color.Parse(removedPill),
            RemovedText = Color.Parse(removedText),
            Resources = new ColorPaletteResources
            {
                Accent = Color.Parse(accent),
                AltHigh = Alpha(alt, 0xFF),
                AltLow = Alpha(alt, 0x33),
                AltMedium = Alpha(alt, 0x99),
                AltMediumHigh = Alpha(alt, 0xCC),
                AltMediumLow = Alpha(alt, 0x66),
                BaseHigh = Alpha(ink, 0xFF),
                BaseMediumHigh = Alpha(ink, 0xCC),
                BaseMedium = Alpha(ink, 0x99),
                BaseMediumLow = Alpha(ink, 0x66),
                BaseLow = Alpha(ink, 0x33),
                ChromeAltLow = Alpha(ink, 0xFF),
                ChromeBlackHigh = Alpha(Colors.Black, 0xFF),
                ChromeBlackLow = Alpha(Colors.Black, 0x33),
                ChromeBlackMediumLow = Alpha(Colors.Black, 0x66),
                ChromeBlackMedium = Alpha(Colors.Black, 0xCC),
                ChromeDisabledHigh = Color.Parse(disabledHigh),
                ChromeDisabledLow = Color.Parse(disabledLow),
                ChromeGray = Color.Parse(chromeGray),
                ChromeHigh = Color.Parse(chromeHigh),
                ChromeLow = Color.Parse(chromeLow),
                ChromeMedium = Color.Parse(chromeMedium),
                ChromeMediumLow = Color.Parse(chromeMediumLow),
                ChromeWhite = Colors.White,
                ListLow = Alpha(ink, 0x19),
                ListMedium = Alpha(ink, 0x33),
                ErrorText = Color.Parse(error),
                RegionColor = Color.Parse(chromeMediumLow),
            },
        };
    }

    static Color Alpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}
