using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Themes;
using Dock.Avalonia.Themes.Fluent;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Docking;

public static class DockChrome
{
    public static void Ensure(Application app)
    {
        if (app.Styles.OfType<DockFluentTheme>().Any())
            return;
        app.Styles.Add(new DockFluentTheme
        {
            DensityStyle = DockDensityStyle.Compact,
            CacheDocumentTabContent = false,
        });
        var tabs = new Style(selector => selector.OfType<DocumentControl>());
        tabs.Add(new Setter(DocumentControl.HeaderTemplateProperty, Header()));
        app.Styles.Add(tabs);
        // The tool dock's minimum is also applied to the grip and the tool body. Leaving those set
        // counts the grip twice and clips the commit box, so only the dock slot keeps the floor.
        app.Styles.Add(ZeroMinHeight<ToolChromeControl>());
        app.Styles.Add(ZeroMinHeight<ToolControl>());
        app.DataTemplates.Insert(0, new FuncDataTemplate<RepositoryDocument>((document, _) =>
            new DiffDocumentView { DataContext = document?.Repository }));
        app.DataTemplates.Insert(0, new FuncDataTemplate<RepositoryTool>((tool, _) =>
            tool is null ? new Panel() : new ToolViewHost(tool)));
    }

    private static Style ZeroMinHeight<TControl>() where TControl : Control
    {
        var style = new Style(selector => selector.OfType<TControl>());
        style.Add(new Setter(Layoutable.MinHeightProperty, 0d));
        return style;
    }

    private static FuncDataTemplate<RepositoryDocument> Header() => new((document, _) =>
    {
        var border = new Border
        {
            BorderThickness = new Thickness(1, 1, 1, 0),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Padding = new Thickness(16, 10),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundChromeMediumLowBrush"),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumLowBrush"),
        };
        border.Classes.Add("tab");
        if (document is not null)
            ToolTip.SetTip(border, new Binding(nameof(RepositoryDocument.Repository) + "." + nameof(RepositoryViewModel.ShortcutHint)) { Source = document });
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        panel.Children.Add(Dot("#D04040", nameof(RepositoryViewModel.IsConflicted)));
        panel.Children.Add(Dot("#E0A100", nameof(RepositoryViewModel.ShowDirtyDot)));
        panel.Children.Add(Dot("#3CB44B", nameof(RepositoryViewModel.ShowCleanDot)));
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(RepositoryDocument.Title)));
        panel.Children.Add(title);
        border.Child = panel;
        return border;

        static Ellipse Dot(string fill, string property)
        {
            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = SolidColorBrush.Parse(fill),
                VerticalAlignment = VerticalAlignment.Center,
            };
            dot.Bind(Visual.IsVisibleProperty, new Binding(nameof(RepositoryDocument.Repository) + "." + property));
            return dot;
        }
    });
}
