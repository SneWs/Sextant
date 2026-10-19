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
    /// <summary>
    /// The Changes slot stays at least this tall. The commit message is 64, the committer row and
    /// commit button sit under it, and the tool grip is drawn above that view.
    /// </summary>
    public const double ChangesPaneMinHeight = 172;

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
            [DockIds.Changes] = new RepositoryTool(this, DockIds.Changes, "Commit"),
            [DockIds.Staging] = new RepositoryTool(this, DockIds.Staging, "Changes"),
            [DockIds.Files] = new RepositoryTool(this, DockIds.Files, "Repository files"),
            [DockIds.Locks] = new RepositoryTool(this, DockIds.Locks, "LFS locks"),
            [DockIds.Commands] = new RepositoryTool(this, DockIds.Commands, "Command log"),
            [DockIds.Diff] = new RepositoryTool(this, DockIds.Diff, "Diff"),
            [DockIds.Blame] = new RepositoryTool(this, DockIds.Blame, "Blame"),
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

    /// <summary>Selects an open tool that shares its dock with another tool. A closed tool stays closed.</summary>
    public void SelectGrouped(string id)
    {
        if (!_tools.TryGetValue(id, out var tool) || !IsOpen(id))
            return;
        if (tool.Owner is not IDock owner || owner.VisibleDockables is not { Count: > 1 })
            return;
        BringForward(id);
    }

    /// <summary>
    /// Selects the first tab of the Diff, Blame, and LFS locks group. In the default layout that tab is Diff.
    /// A tool that sits alone stays as it is.
    /// </summary>
    public void SelectFirstReviewTab()
    {
        var dock = ReviewGroup();
        if (dock?.VisibleDockables is not { Count: > 1 } items || items[0] is not RepositoryTool first)
            return;
        BringForward(first.ToolId);
    }

    private ToolDock? ReviewGroup()
    {
        foreach (var id in new[] { DockIds.Diff, DockIds.Blame, DockIds.Locks })
        {
            if (!_tools.TryGetValue(id, out var tool) || !IsOpen(id) || tool.Owner is not ToolDock dock)
                continue;
            var count = dock.VisibleDockables?.OfType<RepositoryTool>()
                .Count(item => item.ToolId is DockIds.Diff or DockIds.Blame or DockIds.Locks) ?? 0;
            if (count >= 2)
                return dock;
        }

        return null;
    }

    /// <summary>Selects an open tool. A closed tool stays closed, and split sizes stay as they are.</summary>
    public void Activate(string id) => BringForward(id);

    /// <summary>Opens a tool and selects it. An open tool stays open. A tool squeezed to nothing gets a share of its split.</summary>
    public void Show(string id)
    {
        if (!_tools.TryGetValue(id, out var tool))
            return;
        if (!IsOpen(id))
            SetOpen(id, true);
        else
            GiveRoom(tool);
        BringForward(id);
    }

    /// <summary>
    /// A proportion of 0 is a real value, so the split gives that pane no space. NaN means the split
    /// has not assigned one yet. Closing a floating window used to dock the pane back at 0.
    /// </summary>
    private static void GiveRoom(RepositoryTool tool)
    {
        for (IDockable? node = tool.Owner; node is not null and not IRootDock; node = node.Owner)
        {
            if (!double.IsNaN(node.Proportion) && node.Proportion <= 0.001)
                node.Proportion = 0.22;
        }
    }

    public bool IsSelected(string id) =>
        _tools.TryGetValue(id, out var tool)
        && tool.Owner is IDock owner
        && ReferenceEquals(owner.ActiveDockable, tool);

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

    /// <summary>Shows a layout file. An unusable record leaves the current arrangement.</summary>
    public bool TryRestore(DockLayout layout)
    {
        if (!DockLayouts.TryAccept(layout))
            return false;
        _workspace.DockLayout = layout;
        Apply(layout);
        RehomeOffScreenFloats();
        return true;
    }

    public DockLayout CaptureLayout()
    {
        var layout = Capture();
        if (DockLayouts.IsUsable(layout))
            _workspace.DockLayout = layout;
        return layout;
    }

    public void WriteLayout() => CaptureLayout();

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
            else if (tool.ToolId == DockIds.Locks)
                tab.LfsLocksVisible = false;
        }
        finally
        {
            _applying = false;
        }

        if (tool.ToolId == DockIds.Blame && IsOpen(DockIds.Diff))
            Present(DockIds.Diff);
        else if (tool.ToolId == DockIds.Diff && IsOpen(DockIds.Blame))
            Present(DockIds.Blame);
    }

    public void NoteReview(IDockable? dockable)
    {
        if (dockable is RepositoryTool tool && tool.ToolId is DockIds.Diff or DockIds.Blame)
            Present(tool.ToolId);
    }

    /// <summary>Selecting the locks tool asks the server. Selecting another tool leaves that list alone.</summary>
    public void NoteLocks(IDockable? dockable)
    {
        if (Rebuilding || dockable is not RepositoryTool tool || Model.ActiveTab is not { } tab)
            return;
        var selected = tool.ToolId == DockIds.Locks;
        tab.LfsLocksVisible = selected;
        if (selected)
            _ = tab.RefreshLfsLocksCommand.ExecuteAsync(null);
    }

    public bool SharesHost(string left, string right)
    {
        if (!_tools.TryGetValue(left, out var first) || !_tools.TryGetValue(right, out var second))
            return false;
        return first.Owner is not null && ReferenceEquals(first.Owner, second.Owner);
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
            Proportion = 0.22,
        };
        if (tool.ToolId == DockIds.Changes)
            host.MinHeight = ChangesPaneMinHeight;
        // An empty dock is collapsible. Adding it first lays it out at no size and stores that as
        // proportion 0, which then keeps the pane invisible after the tool moves in.
        if (!ReferenceEquals(source, host))
            Factory.MoveDockable(source, host, tool, null);
        host.Proportion = 0.22;
        if (main.VisibleDockables is { Count: > 0 })
            Factory.AddDockable(main, Factory.CreateProportionalDockSplitter());
        Factory.AddDockable(main, host);
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
        var locksSelected = IsSelected(DockIds.Locks);
        value.LfsLocksVisible = locksSelected;
        if (locksSelected)
            _ = value.RefreshLfsLocksCommand.ExecuteAsync(null);
        Present(ActiveReview());
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
            var panes = new Dictionary<string, RepositoryTool>(StringComparer.Ordinal);
            foreach (var tool in _tools.Values)
            {
                Detach(tool);
                if (LayoutUses(layout, tool.ToolId))
                    panes[tool.ToolId] = tool;
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
        Present(ActiveReview());
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
            MinHeight = tool.ToolId == DockIds.Changes ? ChangesPaneMinHeight : 0,
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

        layout.Schema = DockLayouts.CurrentSchema;
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
                if (tools.Count == 1)
                {
                    split.Slots.Add(new DockSlot { Id = tools[0].ToolId, Proportion = Share(child.Proportion) });
                    continue;
                }

                split.Slots.Add(new DockSlot
                {
                    Proportion = Share(child.Proportion),
                    Tabs = tools.Select(tool => tool.ToolId).ToList(),
                    Active = (toolDock.ActiveDockable as RepositoryTool)?.ToolId ?? tools[0].ToolId,
                });
                continue;
            }

            if (child is IDocumentDock)
                continue;
        }

        return split;
    }

    private IDockable Build(DockSplit split, IReadOnlyDictionary<string, RepositoryTool> panes)
    {
        var dock = new ProportionalDock
        {
            Id = "split-" + Guid.NewGuid().ToString("N"),
            Orientation = split.Orientation == "vertical" ? Orientation.Vertical : Orientation.Horizontal,
        };
        var children = new List<IDockable>();
        foreach (var slot in split.Slots)
        {
            IDockable? child = slot.Split is not null ? Build(slot.Split, panes) : Host(slot, panes);
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

        if (id is DockIds.Diff or DockIds.Blame && TryJoinReview(layout.Root, id))
            return;
        if (id is DockIds.Diff or DockIds.Blame && TryAttachReview(layout.Root, id))
            return;
        if (id == DockIds.Files && TryJoinHistory(layout.Root))
            return;
        if (id == DockIds.Locks && TryJoinLocks(layout.Root))
            return;
        if (id == DockIds.Staging && DockLayouts.TryInsertStaging(layout.Root))
            return;
        if (id == DockIds.Changes && DockLayouts.TryInsertChanges(layout.Root))
            return;
        if (id == DockIds.Changes && TryWrapReview(layout.Root, id))
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

    private static bool TryJoinHistory(DockSplit split)
    {
        for (var i = 0; i < split.Slots.Count; i++)
        {
            var slot = split.Slots[i];
            if (slot.Split is not null)
            {
                if (TryJoinHistory(slot.Split))
                    return true;
                continue;
            }

            if (slot.Id == DockIds.History && slot.Tabs is not { Count: > 0 })
            {
                slot.Id = "";
                slot.Tabs = [DockIds.History, DockIds.Files];
                slot.Active = DockIds.Files;
                return true;
            }

            if (slot.Tabs is { } tabs && tabs.Contains(DockIds.History) && !tabs.Contains(DockIds.Files))
            {
                tabs.Add(DockIds.Files);
                slot.Active = DockIds.Files;
                return true;
            }
        }

        return false;
    }

    /// <summary>Puts the locks list on the diff and blame tab group and selects it.</summary>
    private static bool TryJoinLocks(DockSplit split)
    {
        for (var i = 0; i < split.Slots.Count; i++)
        {
            var slot = split.Slots[i];
            if (slot.Split is not null)
            {
                if (TryJoinLocks(slot.Split))
                    return true;
                continue;
            }

            var ids = Members(slot);
            if (ids.Count == 0 || ids.Contains(DockIds.Locks))
                continue;
            if (!ids.Contains(DockIds.Diff) && !ids.Contains(DockIds.Blame))
                continue;
            if (ids.Any(item => item is not (DockIds.Diff or DockIds.Blame)))
                continue;

            var tabs = new List<string>();
            if (ids.Contains(DockIds.Diff))
                tabs.Add(DockIds.Diff);
            if (ids.Contains(DockIds.Blame))
                tabs.Add(DockIds.Blame);
            tabs.Add(DockIds.Locks);
            slot.Id = "";
            slot.Tabs = tabs;
            slot.Active = DockIds.Locks;
            return true;
        }

        return false;
    }

    private static bool TryJoinReview(DockSplit split, string id)
    {
        var partner = id == DockIds.Diff ? DockIds.Blame : DockIds.Diff;
        for (var i = 0; i < split.Slots.Count; i++)
        {
            var slot = split.Slots[i];
            if (slot.Split is not null)
            {
                if (TryJoinReview(slot.Split, id))
                    return true;
                continue;
            }

            var ids = Members(slot);
            if (!ids.Contains(partner) || ids.Any(item => item is not (DockIds.Diff or DockIds.Blame or DockIds.Locks)))
                continue;
            slot.Id = "";
            slot.Split = null;
            var tabs = new List<string> { DockIds.Diff, DockIds.Blame };
            if (ids.Contains(DockIds.Locks))
                tabs.Add(DockIds.Locks);
            slot.Tabs = tabs;
            slot.Active = id;
            return true;
        }

        return false;
    }

    private static bool TryAttachReview(DockSplit split, string id)
    {
        for (var i = 0; i < split.Slots.Count; i++)
        {
            var slot = split.Slots[i];
            if (slot.Id == DockIds.Changes)
            {
                if (split.Orientation == "vertical")
                {
                    var insertAt = i + 1;
                    if (insertAt < split.Slots.Count && split.Slots[insertAt].Id == DockIds.Staging)
                        insertAt++;
                    slot.Proportion = Math.Max(0.2, slot.Proportion * 0.36 / 0.64);
                    split.Slots.Insert(insertAt, new DockSlot { Id = id, Proportion = 0.64 });
                    return true;
                }

                slot.Id = "";
                slot.Split = new DockSplit
                {
                    Orientation = "vertical",
                    Slots =
                    [
                        new DockSlot { Id = DockIds.Changes, Proportion = 0.36 },
                        new DockSlot { Id = id, Proportion = 0.64 },
                    ],
                };
                return true;
            }

            if (slot.Split is not null && TryAttachReview(slot.Split, id))
                return true;
        }

        return false;
    }

    private static bool TryWrapReview(DockSplit split, string id)
    {
        for (var i = 0; i < split.Slots.Count; i++)
        {
            var slot = split.Slots[i];
            if (IsReview(slot))
            {
                var review = new DockSlot
                {
                    Id = slot.Id,
                    Proportion = 0.64,
                    Tabs = slot.Tabs,
                    Active = slot.Active,
                };
                slot.Id = "";
                slot.Tabs = null;
                slot.Active = "";
                slot.Split = new DockSplit
                {
                    Orientation = "vertical",
                    Slots =
                    [
                        new DockSlot { Id = id, Proportion = 0.36 },
                        review,
                    ],
                };
                return true;
            }

            if (slot.Split is not null && TryWrapReview(slot.Split, id))
                return true;
        }

        return false;
    }

    private static void RemoveSlot(DockSplit? split, string id)
    {
        if (split is null)
            return;
        for (var i = split.Slots.Count - 1; i >= 0; i--)
        {
            var slot = split.Slots[i];
            if (slot.Split is not null)
            {
                RemoveSlot(slot.Split, id);
                if (slot.Split.Slots.Count == 0)
                    split.Slots.RemoveAt(i);
                continue;
            }

            if (slot.Tabs is { Count: > 0 } tabs)
            {
                if (!tabs.Remove(id))
                    continue;
                if (slot.Active == id)
                    slot.Active = tabs.FirstOrDefault() ?? "";
                if (tabs.Count == 0)
                    split.Slots.RemoveAt(i);
                else if (tabs.Count == 1)
                {
                    slot.Id = tabs[0];
                    slot.Tabs = null;
                    slot.Active = "";
                }

                continue;
            }

            if (slot.Id == id)
                split.Slots.RemoveAt(i);
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
            if (slot.Id == id || slot.Tabs?.Contains(id) == true)
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

    private ToolDock? Host(DockSlot slot, IReadOnlyDictionary<string, RepositoryTool> panes)
    {
        var members = new List<RepositoryTool>();
        foreach (var id in Members(slot))
        {
            if (panes.TryGetValue(id, out var tool))
                members.Add(tool);
        }

        if (members.Count == 0)
            return null;
        var active = members.FirstOrDefault(tool => tool.ToolId == slot.Active) ?? members[0];
        return new ToolDock
        {
            Id = "tool-" + string.Join('-', members.Select(tool => tool.ToolId)),
            Alignment = Alignment.Unset,
            VisibleDockables = Factory.CreateList<IDockable>(members.Cast<IDockable>().ToArray()),
            ActiveDockable = active,
            MinHeight = members.Any(tool => tool.ToolId == DockIds.Changes) ? ChangesPaneMinHeight : 0,
        };
    }

    /// <summary>
    /// Keeps the floor on whichever tool dock currently holds Changes. Dragging the tool builds a new dock.
    /// </summary>
    internal void FitChangesHost(IDockable? dockable)
    {
        if (dockable is ToolDock dock)
            ApplyChangesFloor(dock);
        if (dockable is RepositoryTool { ToolId: DockIds.Changes } tool && tool.Owner is ToolDock owner)
            ApplyChangesFloor(owner);
    }

    private static void ApplyChangesFloor(ToolDock dock)
    {
        var holds = dock.VisibleDockables?.OfType<RepositoryTool>().Any(tool => tool.ToolId == DockIds.Changes) == true;
        if (holds)
            dock.MinHeight = ChangesPaneMinHeight;
        else if (dock.MinHeight == ChangesPaneMinHeight)
            dock.MinHeight = 0;
    }

    private void BringForward(string id)
    {
        if (!_tools.TryGetValue(id, out var tool) || !IsOpen(id))
            return;
        if (Floated(tool))
        {
            SelectInOwner(tool);
            var window = Root?.Windows?.LastOrDefault(item =>
                item.Layout is not null && SextantDockFactory.Collect(item.Layout).Contains(tool));
            window?.Host?.SetActive();
        }
        else if (Pinned(tool))
        {
            Factory.PreviewPinnedDockable(tool);
        }
        else
        {
            SelectInOwner(tool);
        }

        if (id is DockIds.Diff or DockIds.Blame)
            Present(id);
    }

    private static void SelectInOwner(RepositoryTool tool)
    {
        if (tool.Owner is IDock owner && !ReferenceEquals(owner.ActiveDockable, tool))
            owner.ActiveDockable = tool;
    }

    private void Present(string? id)
    {
        if (Rebuilding || id is not (DockIds.Diff or DockIds.Blame) || Focused is not { } tab)
            return;
        if (id == DockIds.Blame && !tab.ShowingBlame)
            _ = tab.ShowBlameCommand.ExecuteAsync(null);
        else if (id == DockIds.Diff && tab.ShowingBlame)
            _ = tab.ShowDiffCommand.ExecuteAsync(null);
    }

    private string? ActiveReview()
    {
        var diff = OpenReview(DockIds.Diff);
        var blame = OpenReview(DockIds.Blame);
        var diffActive = diff?.Owner is IDock diffDock && ReferenceEquals(diffDock.ActiveDockable, diff);
        var blameActive = blame?.Owner is IDock blameDock && ReferenceEquals(blameDock.ActiveDockable, blame);
        if (blameActive && !diffActive)
            return DockIds.Blame;
        if (diffActive && !blameActive)
            return DockIds.Diff;
        if (blame is not null && diff is null)
            return DockIds.Blame;
        if (diff is not null)
            return DockIds.Diff;
        return null;
    }

    private RepositoryTool? OpenReview(string id) =>
        _tools.TryGetValue(id, out var tool) && IsOpen(id) ? tool : null;

    private static IReadOnlyList<string> Members(DockSlot slot)
    {
        if (slot.Tabs is { Count: > 0 } tabs)
            return tabs;
        return slot.Id.Length > 0 ? [slot.Id] : [];
    }

    private static bool IsReview(DockSlot slot)
    {
        var ids = Members(slot);
        return ids.Contains(DockIds.Diff) || ids.Contains(DockIds.Blame);
    }

    private static double Share(double proportion) =>
        proportion > 0 && !double.IsNaN(proportion) && !double.IsInfinity(proportion) ? proportion : 1;
}
