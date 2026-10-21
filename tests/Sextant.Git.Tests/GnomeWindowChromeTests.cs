using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Styling;
using Avalonia.Threading;
using Sextant;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class GnomeWindowChromeTests
{
    [Fact]
    public void Gnome_is_the_only_desktop_that_needs_a_drawn_frame()
    {
        Assert.True(GnomeSession.IsGnome("ubuntu:GNOME", "ubuntu"));
        Assert.True(GnomeSession.IsGnome("GNOME", "gnome"));
        Assert.True(GnomeSession.IsGnome("GNOME-Classic:GNOME", null));
        Assert.True(GnomeSession.IsGnome(null, "gnome"));
        Assert.True(GnomeSession.IsGnome("pop:GNOME", "pop"));
        Assert.False(GnomeSession.IsGnome("KDE", "plasma"));
        Assert.False(GnomeSession.IsGnome("X-Cinnamon", "cinnamon"));
        Assert.False(GnomeSession.IsGnome("ubuntu", "ubuntu"));
        Assert.False(GnomeSession.IsGnome("GNOMEISH", null));
        Assert.False(GnomeSession.IsGnome(null, null));
        Assert.False(GnomeSession.IsGnome("  ", " "));
    }

    [Fact]
    public void Button_layout_reads_the_gnome_setting()
    {
        var ubuntu = GnomeButtonLayout.Parse(":minimize,maximize,close");
        Assert.Empty(ubuntu.Left);
        Assert.Equal(["minimize", "maximize", "close"], ubuntu.Right);

        var fedora = GnomeButtonLayout.Parse("appmenu:close");
        Assert.Empty(fedora.Left);
        Assert.Equal(["close"], fedora.Right);

        var left = GnomeButtonLayout.Parse("close,minimize,maximize:");
        Assert.Equal(["close", "minimize", "maximize"], left.Left);
        Assert.Empty(left.Right);

        var mixed = GnomeButtonLayout.Parse("close:minimize,maximize");
        Assert.Equal(["close"], mixed.Left);
        Assert.Equal(["minimize", "maximize"], mixed.Right);

        Assert.Equal(["close"], GnomeButtonLayout.Parse(null).Right);
        Assert.Equal(["close"], GnomeButtonLayout.Parse("not a layout").Right);
        Assert.Equal(":minimize,maximize,close", GnomeButtonLayout.Normalize("':minimize,maximize,close'"));
        Assert.Equal(GnomeButtonLayout.Default, GnomeButtonLayout.Normalize("close"));
    }

    [Fact]
    public async Task Kde_and_cinnamon_keep_the_desktop_frame()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            var app = Application.Current!;
            GnomeWindowChrome.Apply(app, "KDE", "plasma", ":minimize,maximize,close");
            GnomeWindowChrome.Apply(app, "X-Cinnamon", "cinnamon", ":close");
            Assert.DoesNotContain(app.Styles, style => style is GnomeWindowChrome);

            GnomeWindowChrome.Apply(app, "ubuntu:GNOME", "ubuntu", ":minimize,maximize,close");
            Assert.Contains(app.Styles, style => style is GnomeWindowChrome);
            Assert.Equal(":minimize,maximize,close", app.Resources[GnomeWindowChrome.ButtonLayoutKey]);

            GnomeWindowChrome.Apply(app, "GNOME", "gnome", ":close");
            Assert.Single(app.Styles.OfType<GnomeWindowChrome>());
            Assert.Equal(":minimize,maximize,close", app.Resources[GnomeWindowChrome.ButtonLayoutKey]);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Caption_buttons_follow_the_gnome_layout()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            var ubuntu = Show(":minimize,maximize,close");
            try
            {
                Assert.True(At(ubuntu, "PART_MinimizeButton").Bounds.Right <= At(ubuntu, "PART_MaximizeButton").Bounds.Left);
                Assert.True(At(ubuntu, "PART_MaximizeButton").Bounds.Right <= At(ubuntu, "PART_CloseButton").Bounds.Left);
                Assert.True(At(ubuntu, "PART_MinimizeButton").Bounds.Left > ubuntu.Bounds.Width / 2);
                Assert.True(At(ubuntu, "PART_CloseButton").Bounds.Right <= ubuntu.Bounds.Width + 0.5);
                var hit = (ICustomHitTest)ubuntu;
                Assert.False(hit.HitTest(new Point(8, 20)));
                var close = At(ubuntu, "PART_CloseButton").Bounds;
                Assert.True(hit.HitTest(new Point(close.Center.X, close.Center.Y)));
            }
            finally
            {
                Close(ubuntu);
            }

            var fedora = Show("appmenu:close");
            try
            {
                Assert.False(At(fedora, "PART_MinimizeButton").IsVisible);
                Assert.False(At(fedora, "PART_MaximizeButton").IsVisible);
                Assert.True(At(fedora, "PART_CloseButton").Bounds.Left > fedora.Bounds.Width / 2);
            }
            finally
            {
                Close(fedora);
            }

            var left = Show("close,minimize,maximize:");
            try
            {
                Assert.True(At(left, "PART_CloseButton").Bounds.Left < 1);
                Assert.True(At(left, "PART_CloseButton").Bounds.Right <= At(left, "PART_MinimizeButton").Bounds.Left);
                Assert.True(At(left, "PART_MinimizeButton").Bounds.Right <= At(left, "PART_MaximizeButton").Bounds.Left);
                Assert.True(At(left, "PART_MaximizeButton").Bounds.Right < left.Bounds.Width / 2);
            }
            finally
            {
                Close(left);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Gnome_caption_theme_drops_the_windows_chrome()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            var chrome = new GnomeWindowChrome();
            var caption = (ControlTheme)chrome.Resources["GnomeCaptionButton"]!;
            var background = caption.Setters.OfType<Setter>().Single(setter => setter.Property == Avalonia.Controls.Button.BackgroundProperty);
            Assert.Equal(Brushes.Transparent, background.Value);
            var width = caption.Setters.OfType<Setter>().Single(setter => setter.Property == Avalonia.Controls.Button.WidthProperty);
            Assert.Equal(34d, width.Value);

            var decorations = (ControlTheme)chrome.Resources["GnomeWindowDecorations"]!;
            var height = decorations.Setters.OfType<Setter>().Single(setter => setter.Property == WindowDrawnDecorations.DefaultTitleBarHeightProperty);
            Assert.Equal(40d, height.Value);
            var window = Assert.Single(chrome.OfType<Style>());
            Assert.Contains("Window", window.Selector?.ToString(), StringComparison.Ordinal);

            var template = Assert.IsAssignableFrom<IWindowDrawnDecorationsTemplate>(
                decorations.Setters.OfType<Setter>().Single(setter => setter.Property == WindowDrawnDecorations.TemplateProperty).Value);
            var overlay = Assert.IsAssignableFrom<Control>(template.Build().Result.Overlay);
            var title = Walk(overlay).OfType<TextBlock>().Single(text => text.Name == "PART_TitleText");
            Assert.Equal(HorizontalAlignment.Center, title.HorizontalAlignment);
            var close = Walk(overlay).OfType<Button>().Single(button => button.Name == "PART_CloseButton");
            Assert.Same(chrome.Resources["GnomeCloseButton"], close.Theme);
        }, CancellationToken.None);
    }

    private static GnomeCaptionBar Show(string layout)
    {
        var bar = new GnomeCaptionBar
        {
            ButtonLayout = layout,
            Width = 420,
            Height = 40,
        };
        bar.Children.Add(Caption("PART_MinimizeButton"));
        bar.Children.Add(Caption("PART_MaximizeButton"));
        bar.Children.Add(Caption("PART_CloseButton"));
        var window = new Window { Content = bar, Width = 420, Height = 80 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return bar;
    }

    private static void Close(GnomeCaptionBar bar)
    {
        if (TopLevel.GetTopLevel(bar) is Window window)
            window.Close();
    }

    private static IEnumerable<Control> Walk(Control control)
    {
        yield return control;
        if (control is not Panel panel)
            yield break;
        foreach (var child in panel.Children)
        {
            foreach (var nested in Walk(child))
                yield return nested;
        }
    }

    private static Button Caption(string name) => new()
    {
        Name = name,
        Width = 34,
        Height = 34,
    };

    private static Button At(GnomeCaptionBar bar, string name)
        => Assert.IsType<Button>(bar.Children.Single(child => child.Name == name));
}
