using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Sextant.Git.Workspace;
using Sextant.ViewModels;

namespace Sextant.Docking;

public partial class RepositoryDock : ObservableObject
{
    private readonly WorkspaceState _workspace;
    private readonly Dictionary<string, RepositoryTool> _tools;
    private readonly HashSet<RepositoryViewModel> _live = [];
    private bool _applying;

    public RepositoryDock(MainViewModel model, WorkspaceState workspace)
    {
        Model = model;
        _workspace = workspace;
        Factory = new SextantDockFactory(this);
        _tools = new Dictionary<string, RepositoryTool>(StringComparer.Ordinal)
        {
            [DockIds.Locations] = new RepositoryTool(this, DockIds.Locations, "Locations"),
            [DockIds.History] = new RepositoryTool(this, DockIds.History, "History"),
            [DockIds.Changes] = new RepositoryTool(this, DockIds.Changes, "Changes"),
            [DockIds.Files] = new RepositoryTool(this, DockIds.Files, "Repository files"),
            [DockIds.Commands] = new RepositoryTool(this, DockIds.Commands, "Command log"),
            [DockIds.Diff] = new RepositoryTool(this, DockIds.Diff, "Diff"),
        };
        var resolved = DockLayouts.Resolve(workspace);
        if (!ReferenceEquals(resolved, workspace.DockLayout))
            workspace.DockLayout = resolved;
        Apply(resolved);
    }

    public MainViewModel Model { get; }

    public SextantDockFactory Factory { get; }

    [ObservableProperty]
    public partial IRootDock? Root { get; set; }

    [ObservableProperty]
    public partial RepositoryViewModel? Focused { get; set; }

    public bool Rebuilding { get; private set; }

    public bool Silence { get; private set; }

    public Func<IReadOnlyList<(int X, int Y, int Width, int Height)>>? Screens { get; set; }

    public void OnTabsChanged(NotifyCollectionChangedEventArgs change)
    {
        if (change.Action == NotifyCollectionChangedAction.Reset)
        {
            _live.Clear();
            return;
        }

        if (change.OldItems is null)
            return;
        foreach (var tab in change.OldItems.OfType<RepositoryViewModel>())
            _live.Remove(tab);
    }

    public void Select(RepositoryViewModel? tab)
    {
        if (!ReferenceEquals(Focused, tab))
            Focused = tab;
        NoteVisibility();
    }

    public bool IsOpen(string id)
    {
        if (!_tools.TryGetValue(id, out var tool))
            return false;
        // RemoveDockable leaves Owner on the abandoned dock, so a stale owner is not open.
        return InMain(tool) || Pinned(tool) || Floated(tool);
    }

    public void Toggle(string id) => SetOpen(id, !IsOpen(id));

    public void SetOpen(string id, bool open)
    {
        if (!_tools.TryGetValue(id, out var tool) || IsOpen(id) == open || Rebuilding)
            return;
        var layout = Capture();
        if (open)
            InsertSlot(layout, id);
        else
        {
            RemoveSlot(layout.Root, id);
            layout.Pins.RemoveAll(pin => pin.Id == id);
            layout.Floats.RemoveAll(floated => floated.Id == id);
            Collapse(layout);
        }

        if (!DockLayouts.IsUsable(layout))
            layout = DockLayouts.Default();
        Apply(layout);
    }

    private void SyncFocusedTools()
    {
        if (Model.ActiveTab is not { } tab)
            return;
        var filesOpen = IsOpen(DockIds.Files);
        var locationsOpen = IsOpen(DockIds.Locations);
        var commandsOpen = IsOpen(DockIds.Commands);
        var loadFiles = filesOpen && !tab.RepositoryFilesTabOn;
        if (tab.ShowLocations == locationsOpen && tab.CommandsOpen == commandsOpen && tab.RepositoryFilesTabOn == filesOpen)
            return;
        _applying = true;
        try
        {
            tab.ShowLocations = locationsOpen;
            tab.CommandsOpen = commandsOpen;
            tab.RepositoryFilesTabOn = filesOpen;
        }
        finally
        {
            _applying = false;
        }

        if (loadFiles)
            _ = tab.RefreshRepositoryFilesCommand.ExecuteAsync(null);
    }

    public void Reset()
    {
        var layout = DockLayouts.Default();
        _workspace.DockLayout = layout;
        Apply(layout);
    }

    public void WriteLayout()
    {
        var captured = Capture();
        if (DockLayouts.IsUsable(captured))
            _workspace.DockLayout = captured;
    }

    public void RehomeOffScreenFloats()
    {
        var screens = Screens?.Invoke() ?? [];
        if (screens.Count == 0)
            return;
        var layout = Capture();
        var dropped = false;
        foreach (var floated in layout.Floats.ToList())
        {
            if (DockLayouts.FloatOnScreen(floated.X, floated.Y, floated.Width, floated.Height, screens))
                continue;
            layout.Floats.Remove(floated);
            InsertSlot(layout, floated.Id);
            dropped = true;
        }

        if (!dropped)
            return;
        if (!DockLayouts.IsUsable(layout))
            layout = DockLayouts.Default();
        Apply(layout);
    }

    public IReadOnlyList<string> PaneOrder()
    {
        var order = new List<string>();
        if (Root?.VisibleDockables is null)
            return order;
        foreach (var child in Root.VisibleDockables)
            CollectPanes(child, order);
        return order;
    }

    public IReadOnlyList<RepositoryViewModel> VisibleRepositories() =>
        Model.ActiveTab is { } tab ? [tab] : [];

    public void NoteVisibility()
    {
        var now = VisibleRepositories();
        foreach (var tab in now)
        {
            if (!_live.Add(tab) || ReferenceEquals(tab, Model.ActiveTab))
                continue;
            _ = ShowAsync(tab);
        }

        _live.RemoveWhere(tab => !now.Contains(tab));
    }

    public async Task RefreshOtherVisibleAsync()
    {
        foreach (var tab in VisibleRepositories())
        {
            if (ReferenceEquals(tab, Model.ActiveTab) || !tab.IsReady)
                continue;
            await tab.RefreshFromFocusAsync();
        }
    }

    public void NoteToolClosed(RepositoryTool tool)
    {
        if (Model.ActiveTab is not { } tab)
            return;
        _applying = true;
        try
        {
            if (tool.ToolId == DockIds.Locations)
                tab.ShowLocations = false;
            else if (tool.ToolId == DockIds.Commands)
                tab.CommandsOpen = false;
            else if (tool.ToolId == DockIds.Files)
                tab.RepositoryFilesTabOn = false;
        }
        finally
        {
            _applying = false;
        }
    }

    public void Rehome(RepositoryTool tool)
    {
        var main = MainSplit();
        if (main is null || tool.Owner is not IDock source)
            return;
        var host = new ToolDock
        {
            Id = "rehome-" + tool.ToolId,
            Alignment = Alignment.Left,
            VisibleDockables = Factory.CreateList<IDockable>(),
        };
        if (main.VisibleDockables is { Count: > 0 })
            Factory.AddDockable(main, Factory.CreateProportionalDockSplitter());
        Factory.AddDockable(main, host);
        if (!ReferenceEquals(source, host))
            Factory.MoveDockable(source, host, tool, null);
    }

    private RepositoryViewModel? _focusedHook;

    partial void OnFocusedChanged(RepositoryViewModel? value)
    {
        if (_focusedHook is not null)
            _focusedHook.PropertyChanged -= OnFocusedProperty;
        _focusedHook = value;
        if (value is null)
            return;
        var loadFiles = IsOpen(DockIds.Files) && !value.RepositoryFilesTabOn;
        _applying = true;
        try
        {
            value.ShowLocations = IsOpen(DockIds.Locations);
            value.CommandsOpen = IsOpen(DockIds.Commands);
            if (loadFiles)
                value.RepositoryFilesTabOn = true;
        }
        finally
        {
            _applying = false;
        }

        value.PropertyChanged += OnFocusedProperty;
        if (loadFiles)
            _ = value.RefreshRepositoryFilesCommand.ExecuteAsync(null);
    }

    private void OnFocusedProperty(object? sender, PropertyChangedEventArgs e)
    {
        if (_applying || Rebuilding || sender is not RepositoryViewModel tab)
            return;
        if (e.PropertyName == nameof(RepositoryViewModel.ShowLocations))
            SetOpen(DockIds.Locations, tab.ShowLocations);
        else if (e.PropertyName == nameof(RepositoryViewModel.CommandsOpen))
            SetOpen(DockIds.Commands, tab.CommandsOpen);
        else if (e.PropertyName == nameof(RepositoryViewModel.RepositoryFilesTabOn) && tab.RepositoryFilesTabOn)
            SetOpen(DockIds.Files, true);
    }

    private async Task ShowAsync(RepositoryViewModel tab)
    {
        var refresh = tab.IsReady;
        await tab.EnsureLoadedAsync();
        if (refresh && _live.Contains(tab) && !ReferenceEquals(tab, Model.ActiveTab))
            await tab.RefreshFromFocusAsync();
    }

    private void Apply(DockLayout layout)
    {
        Rebuilding = true;
        try
        {
            ReleaseFloats();
            var panes = new Dictionary<string, IDockable>(StringComparer.Ordinal);
            foreach (var tool in _tools.Values)
            {
                if (!LayoutUses(layout, tool.ToolId))
                {
                    Detach(tool);
                    continue;
                }

                Detach(tool);
                var host = new ToolDock
                {
                    Id = "tool-" + tool.ToolId,
                    Alignment = Alignment.Unset,
                    VisibleDockables = Factory.CreateList<IDockable>(tool),
                    ActiveDockable = tool,
                };
                panes[tool.ToolId] = host;
            }

            var tree = Build(layout.Root!, panes);
            var root = Factory.CreateRootDock();
            root.VisibleDockables = Factory.CreateList<IDockable>(tree);
            root.ActiveDockable = tree;
            root.DefaultDockable = tree;
            Factory.InitLayout(root);
            Root = root;
            ApplyPins(layout);
            ApplyFloats(layout);
        }
        finally
        {
            Rebuilding = false;
        }

        NoteVisibility();
        SyncFocusedTools();
    }

    private void Detach(RepositoryTool tool)
    {
        if (tool.Owner is not null)
            Factory.RemoveDockable(tool, false);
        tool.Owner = null;
    }

    private void ReleaseFloats()
    {
        if (Root?.Windows is null)
            return;
        foreach (var window in Root.Windows.ToList())
        {
            if (window.Layout is not null)
            {
                foreach (var tool in SextantDockFactory.Collect(window.Layout).OfType<RepositoryTool>().ToList())
                {
                    if (tool.Owner is not null)
                        Factory.RemoveDockable(tool, false);
                }
            }

            window.Host?.Exit();
        }
    }

    private void ApplyPins(DockLayout layout)
    {
        foreach (var pin in layout.Pins)
        {
            if (!_tools.TryGetValue(pin.Id, out var tool))
                continue;
            var host = EnsureHost(tool);
            host.Alignment = pin.Side switch
            {
                "right" => Alignment.Right,
                "top" => Alignment.Top,
                "bottom" => Alignment.Bottom,
                _ => Alignment.Left,
            };
            Factory.PinDockable(tool);
        }
    }

    private void ApplyFloats(DockLayout layout)
    {
        var screens = Screens?.Invoke() ?? [];
        foreach (var floated in layout.Floats)
        {
            var onScreen = DockLayouts.FloatOnScreen(floated.X, floated.Y, floated.Width, floated.Height, screens);
            if (!_tools.TryGetValue(floated.Id, out var tool))
                continue;
            EnsureHost(tool);
            if (!onScreen)
                continue;
            Factory.FloatDockable(tool);
            Place(tool, floated);
        }
    }

    private ToolDock EnsureHost(RepositoryTool tool)
    {
        if (tool.Owner is ToolDock hosted && InMain(hosted))
            return hosted;
        if (tool.Owner is not null)
            Factory.RemoveDockable(tool, false);
        var dock = new ToolDock
        {
            Id = "tool-" + tool.ToolId,
            Alignment = Alignment.Unset,
            VisibleDockables = Factory.CreateList<IDockable>(tool),
            ActiveDockable = tool,
            Proportion = 0.2,
        };
        var main = MainSplit();
        if (main is not null)
        {
            if (main.VisibleDockables is { Count: > 0 })
                Factory.AddDockable(main, Factory.CreateProportionalDockSplitter());
            Factory.AddDockable(main, dock);
        }

        return dock;
    }

    private void Place(IDockable dockable, DockFloat floated)
    {
        var window = Root?.Windows?.LastOrDefault(item => item.Layout is not null && SextantDockFactory.Collect(item.Layout).Contains(dockable));
        if (window is null)
            return;
        window.X = floated.X;
        window.Y = floated.Y;
        window.Width = floated.Width;
        window.Height = floated.Height;
        if (window.Host is Avalonia.Controls.Window host)
        {
            host.Position = new Avalonia.PixelPoint((int)Math.Round(floated.X), (int)Math.Round(floated.Y));
            host.Width = floated.Width;
            host.Height = floated.Height;
        }
    }

    private DockLayout Capture()
    {
        var layout = new DockLayout();
        if (MainSplit() is IProportionalDock split)
            layout.Root = CaptureSplit(split);
        if (Root is not null)
        {
            AddPins(layout, Root.LeftPinnedDockables, "left");
            AddPins(layout, Root.RightPinnedDockables, "right");
            AddPins(layout, Root.TopPinnedDockables, "top");
            AddPins(layout, Root.BottomPinnedDockables, "bottom");
            if (Root.Windows is not null)
            {
                foreach (var window in Root.Windows)
                {
                    if (window.Layout is null)
                        continue;
                    foreach (var tool in SextantDockFactory.Collect(window.Layout).OfType<RepositoryTool>())
                    {
                        layout.Floats.Add(new DockFloat
                        {
                            Id = tool.ToolId,
                            X = window.X,
                            Y = window.Y,
                            Width = window.Width,
                            Height = window.Height,
                        });
                    }
                }
            }
        }

        if (layout.Root is null || !DockLayouts.IsUsable(layout))
            return DockLayouts.Default();
        return layout;
    }

    private static void AddPins(DockLayout layout, IList<IDockable>? pinned, string side)
    {
        if (pinned is null)
            return;
        foreach (var tool in pinned.OfType<RepositoryTool>())
            layout.Pins.Add(new DockPin { Id = tool.ToolId, Side = side });
    }

    private static DockSplit CaptureSplit(IProportionalDock dock)
    {
        var split = new DockSplit
        {
            Orientation = dock.Orientation == Orientation.Vertical ? "vertical" : "horizontal",
        };
        foreach (var child in dock.VisibleDockables ?? [])
        {
            if (child is IProportionalDockSplitter)
                continue;
            if (child is IProportionalDock nested)
            {
                var captured = CaptureSplit(nested);
                if (captured.Slots.Count == 0)
                    continue;
                split.Slots.Add(new DockSlot { Proportion = Share(child.Proportion), Split = captured });
                continue;
            }

            if (child is IToolDock toolDock)
            {
                var tools = toolDock.VisibleDockables?.OfType<RepositoryTool>().ToList() ?? [];
                if (tools.Count == 0)
                    continue;
                var share = Share(child.Proportion) / tools.Count;
                foreach (var tool in tools)
                    split.Slots.Add(new DockSlot { Id = tool.ToolId, Proportion = share });
                continue;
            }

            if (child is IDocumentDock)
                continue;
        }

        return split;
    }

    private IDockable Build(DockSplit split, IReadOnlyDictionary<string, IDockable> panes)
    {
        var dock = new ProportionalDock
        {
            Id = "split-" + Guid.NewGuid().ToString("N"),
            Orientation = split.Orientation == "vertical" ? Orientation.Vertical : Orientation.Horizontal,
        };
        var children = new List<IDockable>();
        foreach (var slot in split.Slots)
        {
            IDockable? child = slot.Split is not null ? Build(slot.Split, panes) : panes.GetValueOrDefault(slot.Id);
            if (child is null)
                continue;
            if (children.Count > 0)
                children.Add(Factory.CreateProportionalDockSplitter());
            child.Proportion = slot.Proportion;
            children.Add(child);
        }

        dock.VisibleDockables = Factory.CreateList<IDockable>(children.ToArray());
        if (children.Count > 0)
            dock.ActiveDockable = children[0];
        return dock;
    }

    private static void InsertSlot(DockLayout layout, string id)
    {
        if (layout.Root is null || Contains(layout.Root, id))
            return;
        if (id == DockIds.Commands)
        {
            layout.Root = new DockSplit
            {
                Orientation = "vertical",
                Slots =
                [
                    new DockSlot { Proportion = 0.82, Split = layout.Root },
                    new DockSlot { Id = id, Proportion = 0.18 },
                ],
            };
            return;
        }

        if (id == DockIds.Diff && TryAttachDiff(layout.Root))
            return;
        if (id == DockIds.Changes && TryWrapDiff(layout.Root, id))
            return;
        foreach (var slot in layout.Root.Slots)
            slot.Proportion *= 0.82;
        var index = id switch
        {
            DockIds.Locations => 0,
            DockIds.Files => layout.Root.Slots.FindIndex(slot => slot.Id == DockIds.History) + 1,
            _ => layout.Root.Slots.Count,
        };
        if (index < 0)
            index = layout.Root.Slots.Count;
        layout.Root.Slots.Insert(Math.Clamp(index, 0, layout.Root.Slots.Count), new DockSlot { Id = id, Proportion = 0.18 });
    }

    private static bool TryAttachDiff(DockSplit split)
    {
        for (var i = 0; i < split.Slots.Count; i++)
        {
            var slot = split.Slots[i];
            if (slot.Id == DockIds.Changes)
            {
                if (split.Orientation == "vertical")
                {
                    slot.Proportion = Math.Max(0.2, slot.Proportion * 0.36 / 0.64);
                    split.Slots.Insert(i + 1, new DockSlot { Id = DockIds.Diff, Proportion = 0.64 });
                    return true;
                }

                slot.Id = "";
                slot.Split = new DockSplit
                {
                    Orientation = "vertical",
                    Slots =
                    [
                        new DockSlot { Id = DockIds.Changes, Proportion = 0.36 },
                        new DockSlot { Id = DockIds.Diff, Proportion = 0.64 },
                    ],
                };
                return true;
            }

            if (slot.Split is not null && TryAttachDiff(slot.Split))
                return true;
        }

        return false;
    }

    private static bool TryWrapDiff(DockSplit split, string id)
    {
        for (var i = 0; i < split.Slots.Count; i++)
        {
            var slot = split.Slots[i];
            if (slot.Id == DockIds.Diff)
            {
                slot.Id = "";
                slot.Split = new DockSplit
                {
                    Orientation = "vertical",
                    Slots =
                    [
                        new DockSlot { Id = id, Proportion = 0.36 },
                        new DockSlot { Id = DockIds.Diff, Proportion = 0.64 },
                    ],
                };
                return true;
            }

            if (slot.Split is not null && TryWrapDiff(slot.Split, id))
                return true;
        }

        return false;
    }

    private static void RemoveSlot(DockSplit? split, string id)
    {
        if (split is null)
            return;
        split.Slots.RemoveAll(slot => slot.Id == id);
        foreach (var slot in split.Slots.ToList())
        {
            if (slot.Split is null)
                continue;
            RemoveSlot(slot.Split, id);
            if (slot.Split.Slots.Count == 0)
                split.Slots.Remove(slot);
        }
    }

    private static void Collapse(DockLayout layout)
    {
        if (layout.Root is null)
            return;
        while (layout.Root.Slots.Count == 1 && layout.Root.Slots[0].Split is { } only)
            layout.Root = only;
    }

    private static bool LayoutUses(DockLayout layout, string id) =>
        (layout.Root is not null && Contains(layout.Root, id))
        || layout.Pins.Any(pin => pin.Id == id)
        || layout.Floats.Any(floated => floated.Id == id);

    private static bool Contains(DockSplit split, string id)
    {
        foreach (var slot in split.Slots)
        {
            if (slot.Id == id)
                return true;
            if (slot.Split is not null && Contains(slot.Split, id))
                return true;
        }

        return false;
    }

    private IProportionalDock? MainSplit() =>
        Root?.VisibleDockables?.OfType<IProportionalDock>().FirstOrDefault();

    private bool InMain(IDockable dockable)
    {
        var main = MainSplit();
        return main is not null && SextantDockFactory.Collect(main).Contains(dockable);
    }

    private bool Pinned(RepositoryTool tool)
    {
        if (Root is null)
            return false;
        return Lists(Root.LeftPinnedDockables, Root.RightPinnedDockables, Root.TopPinnedDockables, Root.BottomPinnedDockables)
            .Any(list => list?.Contains(tool) == true);
    }

    private bool Floated(RepositoryTool tool)
    {
        if (Root?.Windows is null)
            return false;
        return Root.Windows.Any(window => window.Layout is not null && SextantDockFactory.Collect(window.Layout).Contains(tool));
    }

    private static IEnumerable<IList<IDockable>?> Lists(params IList<IDockable>?[] lists) => lists;

    private static void CollectPanes(IDockable node, List<string> order)
    {
        if (node is IProportionalDockSplitter)
            return;
        if (node is IToolDock toolDock)
        {
            if (toolDock.VisibleDockables is null)
                return;
            foreach (var tool in toolDock.VisibleDockables.OfType<RepositoryTool>())
                order.Add(tool.ToolId);
            return;
        }

        if (node is IDock dock && dock.VisibleDockables is not null)
        {
            foreach (var child in dock.VisibleDockables)
                CollectPanes(child, order);
        }
    }

    private static double Share(double proportion) =>
        proportion > 0 && !double.IsNaN(proportion) && !double.IsInfinity(proportion) ? proportion : 1;
}
