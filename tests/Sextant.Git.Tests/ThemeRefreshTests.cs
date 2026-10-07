using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Sextant;
using Sextant.Git;
using Sextant.Services;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class ThemeRefreshTests
{
    [Fact]
    public async Task Open_window_repaints_when_the_palette_changes()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            var app = Application.Current!;
            app.RequestedThemeVariant = ThemeVariant.Dark;
            var text = new TextBlock { Text = "Theme" };
            text.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SystemControlForegroundBaseHighBrush"));
            var page = new Border();
            page.Bind(Border.BackgroundProperty, new DynamicResourceExtension("SystemControlBackgroundChromeMediumLowBrush"));
            var window = new Window
            {
                Width = 320,
                Height = 180,
                Content = new StackPanel { Children = { text, page } },
            };
            window.Show();

            var beforeInk = ColorOf(text.Foreground);
            var beforePage = ColorOf(page.Background);
            AppTheme.Apply(ThemePreference.Dark, PalettePreference.Gruvbox);

            Assert.Equal(ThemeVariant.Dark, app.ActualThemeVariant);
            Assert.Equal(Color.Parse("#EBDBB2"), ColorOf(text.Foreground));
            Assert.Equal(Color.Parse("#282828"), ColorOf(page.Background));
            Assert.NotEqual(beforeInk, ColorOf(text.Foreground));
            Assert.NotEqual(beforePage, ColorOf(page.Background));

            var fluent = Assert.IsType<FluentTheme>(Assert.Single(app.Styles));
            Assert.Equal(Color.Parse("#FBF1C7"), fluent.Palettes[ThemeVariant.Light].ChromeMediumLow);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Selecting_a_theme_repaints_before_ok_and_cancel_restores_it()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            var app = Application.Current!;
            var window = new SettingsWindow(new SettingsDraft("", false, false, "dark", "", null, PalettePreference.Catppuccin));
            window.Show();
            var combo = window.GetLogicalDescendants().OfType<ComboBox>().Single(box => box.Items.OfType<ThemeChoice>().Any());
            combo.SelectedItem = combo.Items.OfType<ThemeChoice>().Single(choice => choice.Id == PalettePreference.Gruvbox);

            Assert.Equal(Color.Parse("#EBDBB2"), Ink(app));
            window.Close();
            Assert.Equal(Color.Parse("#CDD6F4"), Ink(app));
        }, CancellationToken.None);
    }

    static Color Ink(Application app)
    {
        Assert.True(app.TryFindResource("SystemControlForegroundBaseHighBrush", ThemeVariant.Dark, out var value));
        return ColorOf(value as IBrush);
    }

    static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
