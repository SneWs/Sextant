using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Themes;
using Dock.Avalonia.Themes.Fluent;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace Sextant.Git.Tests;

/// <summary>
/// Throwaway check that Dock 12.1.0.7 can host a Visual Studio-style layout on Avalonia 12.1.4.
/// </summary>
[Collection(HeadlessCollection.Name)]
public class DockSpikeTests
{
    [Fact]
    public async Task Dock_paints_tabs_splits_floats_and_pins()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(Sextant.App));
        await session.Dispatch(() =>
        {
            SpikeBody.Attached.Clear();
            var dockTheme = new DockFluentTheme
            {
                DensityStyle = DockDensityStyle.Compact,
                CacheDocumentTabContent = false,
            };
            var tabsStyle = new Style(selector => selector.OfType<DocumentControl>());
            tabsStyle.Add(new Setter(DocumentControl.HeaderTemplateProperty, new FuncDataTemplate<IDockable>((dockable, _) =>
            {
                var border = new Border
                {
                    Classes = { "tab" },
                    BorderThickness = new Thickness(1, 1, 1, 0),
                    CornerRadius = new CornerRadius(6, 6, 0, 0),
                    Padding = new Thickness(16, 10),
                };
                var panel = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                };
                panel.Children.Add(new Ellipse
                {
                    Classes = { "spikestatus" },
                    Width = 6,
                    Height = 6,
                    Fill = Brushes.OrangeRed,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                });
                panel.Children.Add(new TextBlock { Text = dockable?.Title, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
                border.Child = panel;
                return border;
            })));
            Application.Current!.Styles.Add(dockTheme);
            Application.Current.Styles.Add(tabsStyle);
            var factory = new ReturningFactory();
            var idle = new SpikeDocument
            {
                Id = "idle",
                Title = "Idle",
                CanClose = true,
            };
            var live = new SpikeDocument
            {
                Id = "live",
                Title = "Live",
                CanClose = true,
            };
            var tool = new Tool
            {
                Id = "locations",
                Title = "Locations",
                CanClose = true,
                CanPin = true,
            };
            var documents = new DocumentDock
            {
                Id = "documents",
                CanCreateDocument = false,
                VisibleDockables = factory.CreateList<IDockable>(live, idle),
                ActiveDockable = live,
            };
            var tools = new ToolDock
            {
                Id = "tools",
                Alignment = Alignment.Left,
                VisibleDockables = factory.CreateList<IDockable>(tool),
                ActiveDockable = tool,
            };
            var split = new ProportionalDock
            {
                Id = "split",
                Orientation = Orientation.Horizontal,
                VisibleDockables = factory.CreateList<IDockable>(
                    tools,
                    new ProportionalDockSplitter(),
                    documents),
            };
            tools.Proportion = 0.25;
            documents.Proportion = 0.75;
            factory.Home = documents;
            var root = factory.CreateRootDock();
            root.VisibleDockables = factory.CreateList<IDockable>(split);
            root.ActiveDockable = split;
            root.DefaultDockable = split;
            factory.InitLayout(root);

            var window = new Window
            {
                Width = 900,
                Height = 600,
                Content = new DockControl { Layout = root },
            };
            window.DataTemplates.Add(new FuncDataTemplate<SpikeDocument>((doc, _) =>
                new SpikeBody { Key = doc!.Id ?? "", Text = doc.Title }));
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var dock = (DockControl)window.Content!;
            var variant = dock.ActualThemeVariant ?? ThemeVariant.Light;
            Assert.True(Application.Current!.TryGetResource("SystemAccentColor", variant, out var accent), variant.ToString());
            var accentColor = Assert.IsType<Color>(accent);
            Assert.True(accentColor == Color.Parse("#1E66F5") || accentColor == Color.Parse("#89B4FA"), accentColor.ToString());
            var toolChrome = Assert.Single(window.GetVisualDescendants().OfType<ToolChromeControl>());
            Assert.True(toolChrome.Background is IBrush);

            var tabs = window.GetVisualDescendants().OfType<DocumentControl>().ToList();
            Assert.NotEmpty(tabs);
            Assert.Contains(window.GetVisualDescendants().OfType<Ellipse>(), dot => dot.Classes.Contains("spikestatus"));
            Assert.Contains(window.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("tab"));
            Assert.Equal(new[] { "live" }, SpikeBody.Attached);

            var guides = new DockTarget();
            var guideHost = new Window { Width = 240, Height = 240, Content = guides };
            guideHost.Show();
            guideHost.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            foreach (var part in new[] { "PART_CenterSelector", "PART_LeftSelector", "PART_RightSelector", "PART_TopSelector", "PART_BottomSelector" })
            {
                var selector = guideHost.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == part);
                Assert.True(selector is { IsVisible: true }, part);
            }
            guideHost.Close();

            factory.DefaultHostWindowLocator = () => new HostWindow();
            factory.HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
            {
                [nameof(IDockWindow)] = factory.DefaultHostWindowLocator,
            };

            factory.SplitToDock(documents, idle, DockOperation.Right);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(documents, idle.Owner);
            Assert.Contains(idle, ((IDock)idle.Owner!).VisibleDockables!);

            factory.FloatDockable(idle);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(idle.DockingState.HasFlag(DockingWindowState.Floating));
            var floating = Assert.Single(root.Windows!);
            Assert.NotNull(floating.Host);

            // CloseWindow removes the window's dockables. A host close can dock them back first.
            floating.Host!.Exit();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(idle, documents.VisibleDockables!);
            Assert.False(idle.DockingState.HasFlag(DockingWindowState.Floating));
            Assert.DoesNotContain(root.Windows ?? [], candidate => ReferenceEquals(candidate, floating));

            factory.PinDockable(tool);
            Assert.True(tool.DockingState.HasFlag(DockingWindowState.Pinned));
            Assert.Contains(tool, root.LeftPinnedDockables!);
            factory.UnpinDockable(tool);
            Assert.False(tool.DockingState.HasFlag(DockingWindowState.Pinned));
            Assert.Contains(tool, ((IDock)tool.Owner!).VisibleDockables!);

            window.Close();
            Application.Current!.Styles.Remove(dockTheme);
            Application.Current.Styles.Remove(tabsStyle);
            return Task.CompletedTask;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Dock 12.1.0.7 closes a floating window's dockables. Sextant docks documents back before that runs.
    /// </summary>
    private sealed class ReturningFactory : Factory
    {
        public DocumentDock? Home { get; set; }

        public int Closes { get; private set; }

        public override bool OnWindowClosing(IDockWindow? window)
        {
            Closes++;
            if (Home is not null && window?.Layout is IDock layout)
            {
                foreach (var document in Collect(layout).ToList())
                {
                    if (document.Owner is IDock source && !ReferenceEquals(source, Home))
                        MoveDockable(source, Home, document, null);
                }
            }

            return base.OnWindowClosing(window);
        }

        private static IEnumerable<IDockable> Collect(IDock dock)
        {
            if (dock.VisibleDockables is null)
                yield break;
            foreach (var child in dock.VisibleDockables)
            {
                if (child is IDocument)
                    yield return child;
                if (child is IDock nested)
                {
                    foreach (var document in Collect(nested))
                        yield return document;
                }
            }
        }
    }

    private static IDockable? FindDockable(IDockable node, string id)
    {
        if (node.Id == id)
            return node;
        if (node is not IDock dock || dock.VisibleDockables is null)
            return null;
        foreach (var child in dock.VisibleDockables)
        {
            var found = FindDockable(child, id);
            if (found is not null)
                return found;
        }

        return null;
    }

    private sealed class SpikeDocument : Document;

    private sealed class SpikeBody : TextBlock
    {
        public static readonly List<string> Attached = [];

        public required string Key { get; init; }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            Attached.Add(Key);
            base.OnAttachedToVisualTree(e);
        }
    }
}
