using Avalonia;
using Avalonia.Controls;
using Sextant;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Sextant.Git.Diff;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class DiffFontPreferenceTests
{
    [Fact]
    public void Empty_and_unsafe_names_use_the_built_in_stack()
    {
        Assert.Equal("", DiffFontPreference.Normalize(null));
        Assert.Equal("", DiffFontPreference.Normalize("  "));
        Assert.Equal("", DiffFontPreference.Normalize("nope\"font"));
        Assert.Equal(DiffFontPreference.Fallback, DiffFontPreference.Family(null));
        Assert.Equal(DiffFontPreference.Fallback, DiffFontPreference.Family("  "));
    }

    [Fact]
    public void A_chosen_face_is_tried_before_the_built_in_stack()
    {
        Assert.Equal("JetBrains Mono", DiffFontPreference.Normalize(" JetBrains Mono "));
        Assert.Equal(
            "JetBrains Mono, " + DiffFontPreference.Fallback,
            DiffFontPreference.Family("JetBrains Mono"));
        Assert.Equal(DiffFontPreference.Fallback, DiffFontPreference.Family(DiffFontPreference.Fallback));
    }

    [Fact]
    public void Size_clamps_to_the_supported_range()
    {
        Assert.Equal(DiffFontPreference.DefaultSize, DiffFontPreference.NormalizeSize(0));
        Assert.Equal(DiffFontPreference.MinSize, DiffFontPreference.NormalizeSize(1));
        Assert.Equal(14, DiffFontPreference.NormalizeSize(14.4));
        Assert.Equal(DiffFontPreference.MaxSize, DiffFontPreference.NormalizeSize(90));
    }

    [Fact]
    public async Task Applying_a_font_updates_an_open_text_block()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            var app = Application.Current!;
            app.Resources[DiffFont.ResourceKey] = new FontFamily(DiffFontPreference.Fallback);
            app.Resources[DiffFont.SizeKey] = DiffFontPreference.DefaultSize;
            var text = new TextBlock { Text = "diff", Classes = { "diff" } };
            text.Bind(TextBlock.FontFamilyProperty, new DynamicResourceExtension(DiffFont.ResourceKey));
            text.Bind(TextBlock.FontSizeProperty, new DynamicResourceExtension(DiffFont.SizeKey));
            var window = new Window { Content = text, Width = 240, Height = 80 };
            window.Show();

            DiffFont.Apply("Consolas", 18);

            Assert.Contains("Consolas", text.FontFamily?.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Consolas", DiffFont.Current.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(18, text.FontSize);
            Assert.Equal(18, DiffFont.Size);
            window.Close();
        }, CancellationToken.None);
    }
}
