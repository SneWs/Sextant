using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Sextant;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class ThemeXamlTests
{
    [Fact]
    public async Task User_theme_replaces_the_fluent_palettes()
    {
        var path = Path.Combine(Path.GetTempPath(), "sextant-theme-" + Guid.NewGuid().ToString("N") + ".xaml");
        File.WriteAllText(path, UserTheme);
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
            await session.Dispatch(() =>
            {
                ThemeXaml.Install(new ThemeChoice("solarized", "Solarized", path));

                var fluent = Assert.IsType<FluentTheme>(Assert.Single(Application.Current!.Styles));
                Assert.Equal(Color.Parse("#112233"), fluent.Palettes[ThemeVariant.Light].Accent);
                Assert.Equal(Color.Parse("#AABBCC"), fluent.Palettes[ThemeVariant.Dark].Accent);
                Assert.Equal(Color.Parse("#FBF1C7"), Brush(ThemeVariant.Light, ThemeXaml.OnAccentKey));
                Assert.Equal(Color.Parse("#1D2021"), Brush(ThemeVariant.Dark, ThemeXaml.OnAccentKey));
            }, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Broken_theme_file_is_refused()
    {
        var path = Path.Combine(Path.GetTempPath(), "sextant-theme-" + Guid.NewGuid().ToString("N") + ".xaml");
        File.WriteAllText(path, "<NotADictionary/>");
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
            await session.Dispatch(() =>
            {
                Assert.False(ThemeXaml.TryValidate(new ThemeChoice("solarized", "Solarized", path), out var error));
                Assert.Contains("could not be loaded", error, StringComparison.OrdinalIgnoreCase);

                var empty = Path.Combine(Path.GetTempPath(), "sextant-theme-empty-" + Guid.NewGuid().ToString("N") + ".xaml");
                File.WriteAllText(empty, "<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"/>");
                try
                {
                    Assert.False(ThemeXaml.TryValidate(new ThemeChoice("empty", "Empty", empty), out error));
                    Assert.Contains("no Light palette", error, StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    File.Delete(empty);
                }
            }, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Built_in_palette_is_installed_when_there_is_no_file()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            ThemeXaml.Install(new ThemeChoice(PalettePreference.Gruvbox, "Gruvbox", null));
            var fluent = Assert.IsType<FluentTheme>(Assert.Single(Application.Current!.Styles));
            Assert.Equal(Color.Parse("#076678"), fluent.Palettes[ThemeVariant.Light].Accent);
            Assert.Equal(Color.Parse("#83A598"), fluent.Palettes[ThemeVariant.Dark].Accent);

            ThemeXaml.Install(new ThemeChoice(PalettePreference.GitHub, "GitHub", null));
            Assert.Equal(Color.Parse("#0969DA"), fluent.Palettes[ThemeVariant.Light].Accent);
            Assert.Equal(Color.Parse("#1F6FEB"), fluent.Palettes[ThemeVariant.Dark].Accent);
            Assert.Equal(Color.Parse("#0D1117"), fluent.Palettes[ThemeVariant.Dark].ChromeMediumLow);

            ThemeXaml.Install(new ThemeChoice(PalettePreference.Black, "Black", null));
            Assert.Equal(Color.Parse("#1A6E70"), fluent.Palettes[ThemeVariant.Light].Accent);
            Assert.Equal(Color.Parse("#A8DADC"), fluent.Palettes[ThemeVariant.Dark].Accent);
            Assert.Equal(Color.Parse("#000000"), fluent.Palettes[ThemeVariant.Dark].ChromeMediumLow);
        }, CancellationToken.None);
    }

    static Color Brush(ThemeVariant variant, string key)
    {
        var dictionary = Assert.IsType<Avalonia.Controls.ResourceDictionary>(
            Application.Current!.Resources.ThemeDictionaries[variant]);
        var brush = Assert.IsType<SolidColorBrush>(dictionary[key]);
        return brush.Color;
    }

    const string UserTheme = """
        <ResourceDictionary xmlns="https://github.com/avaloniaui"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <ColorPaletteResources x:Key="Light" Accent="#112233" ErrorText="#9D0006" RegionColor="#FBF1C7"
                                   BaseHigh="#FF3C3836" ChromeLow="#F9F5D7" ChromeMedium="#EBDBB2" ChromeMediumLow="#FBF1C7"
                                   ChromeHigh="#D5C4A1" ChromeGray="#928374" ChromeWhite="#FFFFFFFF"
                                   AltHigh="#FFFFFFFF" ListLow="#193C3836" ListMedium="#333C3836"/>
            <ColorPaletteResources x:Key="Dark" Accent="#AABBCC" ErrorText="#FB4934" RegionColor="#282828"
                                   BaseHigh="#FFEBDBB2" ChromeLow="#1D2021" ChromeMedium="#3C3836" ChromeMediumLow="#282828"
                                   ChromeHigh="#504945" ChromeGray="#928374" ChromeWhite="#FFFFFFFF"
                                   AltHigh="#FF000000" ListLow="#19EBDBB2" ListMedium="#33EBDBB2"/>
            <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Light">
                    <SolidColorBrush x:Key="OnAccentBrush" Color="#FBF1C7"/>
                </ResourceDictionary>
                <ResourceDictionary x:Key="Dark">
                    <SolidColorBrush x:Key="OnAccentBrush" Color="#1D2021"/>
                </ResourceDictionary>
            </ResourceDictionary.ThemeDictionaries>
        </ResourceDictionary>
        """;
}
