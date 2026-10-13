namespace Sextant.Git.Workspace;

public static class DockIds
{
    public const string Locations = "locations";

    public const string History = "history";

    public const string Changes = "changes";

    public const string Staging = "staging";

    public const string Files = "files";

    public const string Commands = "commands";

    public const string Diff = "diff";

    public const string Blame = "blame";

    /// <summary>Older layouts used this id for the repository document dock. It is read as <see cref="Diff"/>.</summary>
    public const string Documents = "documents";

    public static readonly string[] Tools = [Locations, History, Changes, Staging, Files, Commands, Diff, Blame];

    public static bool IsKnown(string id) =>
        id is Locations or History or Changes or Staging or Files or Commands or Diff or Blame;
}

/// <summary>Sextant's own layout record. It is not Dock's object graph.</summary>
public sealed class DockLayout
{
    /// <summary>
    /// 1 groups an older lone Diff slot with Blame once.
    /// 2 places the staged and unstaged file list in its own tool under Changes.
    /// Later saves keep a closed tool closed.
    /// </summary>
    public int Schema { get; set; }

    public DockSplit? Root { get; set; }

    public List<DockPin> Pins { get; set; } = [];

    public List<DockFloat> Floats { get; set; } = [];
}

public sealed class DockSplit
{
    /// <summary><c>horizontal</c> or <c>vertical</c>.</summary>
    public string Orientation { get; set; } = "horizontal";

    public List<DockSlot> Slots { get; set; } = [];
}

public sealed class DockSlot
{
    /// <summary>Tool id. Empty when <see cref="Split"/> or <see cref="Tabs"/> is set.</summary>
    public string Id { get; set; } = "";

    public double Proportion { get; set; }

    public DockSplit? Split { get; set; }

    /// <summary>Tools that share one dock as tabs. Empty unless this slot is a tab group.</summary>
    public List<string>? Tabs { get; set; }

    /// <summary>Selected tab id. Empty selects the first tab.</summary>
    public string Active { get; set; } = "";
}

public sealed class DockPin
{
    public string Id { get; set; } = "";

    /// <summary><c>left</c>, <c>right</c>, <c>top</c>, or <c>bottom</c>.</summary>
    public string Side { get; set; } = "left";
}

public sealed class DockFloat
{
    public string Id { get; set; } = "";

    /// <summary>Unused. Older files stored a repository path for a floated document.</summary>
    public string? Path { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }
}

public static class DockLayouts
{
    public const int CurrentSchema = 2;

    public static DockLayout Default() => Seed(220, 520, 180, showLocations: true);

    /// <summary>A saved layout that names every pane and uses positive proportions. Otherwise the legacy pane sizes.</summary>
    public static DockLayout Resolve(WorkspaceState state)
    {
        if (state.DockLayout is not null)
            Migrate(state.DockLayout);
        if (IsUsable(state.DockLayout))
            return state.DockLayout!;
        var source = state.ActiveTab is { Length: > 0 } path
            ? RepoLayouts.Resolve(state, path)
            : RepoLayouts.Resolve(state, "");
        return Seed(source.LocationsWidth, source.GraphWidth, source.FilesHeight, source.ShowLocations);
    }

    public static bool IsUsable(DockLayout? layout)
    {
        if (layout?.Root is null)
            return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!Walk(layout.Root, ids))
            return false;
        foreach (var pin in layout.Pins)
        {
            if (!DockIds.Tools.Contains(pin.Id) || !IsSide(pin.Side) || !ids.Add(pin.Id))
                return false;
        }

        foreach (var floated in layout.Floats)
        {
            if (floated.Width <= 0 || floated.Height <= 0 || double.IsNaN(floated.X) || double.IsNaN(floated.Y))
                return false;
            if (!DockIds.Tools.Contains(floated.Id) || !ids.Add(floated.Id))
                return false;
        }

        return true;
    }

    public static DockLayout Seed(double locations, double graph, double files, bool showLocations)
    {
        var loc = showLocations ? Math.Max(locations, 140) : 0;
        var history = Math.Max(graph, 240);
        var details = 640d;
        var sum = loc + history + details;
        var fileShare = Math.Clamp(Math.Max(files, 80) / (Math.Max(files, 80) + 420d), 0.2, 0.55);
        var slots = new List<DockSlot>();
        if (showLocations)
            slots.Add(new DockSlot { Id = DockIds.Locations, Proportion = loc / sum });
        slots.Add(new DockSlot
        {
            Proportion = history / sum,
            Tabs = [DockIds.History, DockIds.Files],
            Active = DockIds.History,
        });
        slots.Add(new DockSlot
        {
            Proportion = details / sum,
            Split = new DockSplit
            {
                Orientation = "vertical",
                Slots =
                [
                    new DockSlot { Id = DockIds.Changes, Proportion = 0.18 },
                    new DockSlot { Id = DockIds.Staging, Proportion = Math.Max(0.16, fileShare * 0.82) },
                    new DockSlot
                    {
                        Proportion = Math.Max(0.34, (1 - fileShare) * 0.82),
                        Tabs = [DockIds.Diff, DockIds.Blame],
                        Active = DockIds.Diff,
                    },
                ],
            },
        });
        return new DockLayout
        {
            Schema = CurrentSchema,
            Root = new DockSplit { Orientation = "horizontal", Slots = slots },
        };
    }

    /// <summary>A float that misses every work area goes back to the main window. No screens means keep it.</summary>
    public static bool FloatOnScreen(double x, double y, double width, double height, IReadOnlyList<(int X, int Y, int Width, int Height)> screens)
    {
        if (width <= 0 || height <= 0 || double.IsNaN(x) || double.IsNaN(y))
            return false;
        if (screens.Count == 0)
            return true;
        var left = (int)Math.Round(x);
        var top = (int)Math.Round(y);
        var right = left + Math.Max(1, (int)Math.Round(width));
        var bottom = top + Math.Max(1, (int)Math.Round(height));
        foreach (var screen in screens)
        {
            var overlapW = Math.Min(screen.X + screen.Width, right) - Math.Max(screen.X, left);
            var overlapH = Math.Min(screen.Y + screen.Height, bottom) - Math.Max(screen.Y, top);
            if (overlapW >= 48 && overlapH >= 48)
                return true;
        }

        return false;
    }

    private static void Migrate(DockLayout layout)
    {
        Rewrite(layout.Root);
        layout.Pins.RemoveAll(pin => pin.Id == DockIds.Documents);
        layout.Floats.RemoveAll(floated => floated.Id == DockIds.Documents);
        if (layout.Schema >= CurrentSchema)
            return;
        if (layout.Schema < 1 && layout.Root is not null && !Holds(layout.Root, DockIds.Blame) && Holds(layout.Root, DockIds.Diff))
            GroupBlameWithDiff(layout.Root);
        if (layout.Schema < 2)
            EnsureStaging(layout);
        layout.Schema = CurrentSchema;
    }

    /// <summary>
    /// A Changes tool that is still open, including one that is pinned or floating, keeps its file list.
    /// The list becomes the Staging tool under that commit box, or above the diff when Changes is not in the main split.
    /// </summary>
    private static void EnsureStaging(DockLayout layout)
    {
        if (layout.Root is null || HoldsTool(layout, DockIds.Staging))
            return;
        if (TryInsertStaging(layout.Root) || !HoldsTool(layout, DockIds.Changes))
            return;
        if (!PlaceBeforeReview(layout.Root))
            layout.Root.Slots.Add(new DockSlot { Id = DockIds.Staging, Proportion = 0.22 });
    }

    private static bool HoldsTool(DockLayout layout, string id) =>
        (layout.Root is not null && Holds(layout.Root, id))
        || layout.Pins.Any(pin => pin.Id == id)
        || layout.Floats.Any(floated => floated.Id == id);

    private static bool PlaceBeforeReview(DockSplit split)
    {
        for (var i = 0; i < split.Slots.Count; i++)
        {
            var slot = split.Slots[i];
            if (IsReviewSlot(slot))
            {
                var given = Math.Max(0.16, slot.Proportion * 0.28);
                slot.Proportion = Math.Max(0.2, slot.Proportion - given);
                split.Slots.Insert(i, new DockSlot { Id = DockIds.Staging, Proportion = given });
                return true;
            }

            if (slot.Split is not null && PlaceBeforeReview(slot.Split))
                return true;
        }

        return false;
    }

    private static bool IsReviewSlot(DockSlot slot) =>
        slot.Id is DockIds.Diff or DockIds.Blame
        || slot.Tabs?.Any(tab => tab is DockIds.Diff or DockIds.Blame) == true;

    /// <summary>Splits a Changes slot so the file list sits under the commit box. A layout that already closed Changes is left alone.</summary>
    public static bool TryInsertStaging(DockSplit split) =>
        !Holds(split, DockIds.Staging) && PlaceBeside(split, DockIds.Changes, DockIds.Staging, before: false, keep: 0.42);

    /// <summary>Puts Changes back above an open Staging tool.</summary>
    public static bool TryInsertChanges(DockSplit split) =>
        !Holds(split, DockIds.Changes) && PlaceBeside(split, DockIds.Staging, DockIds.Changes, before: true, keep: 0.62);

    private static bool PlaceBeside(DockSplit split, string anchor, string id, bool before, double keep)
    {
        for (var i = 0; i < split.Slots.Count; i++)
        {
            var slot = split.Slots[i];
            if (slot.Id == anchor && slot.Split is null && slot.Tabs is not { Count: > 0 })
            {
                var given = Math.Max(0.12, slot.Proportion * (1 - keep));
                slot.Proportion = Math.Max(0.12, slot.Proportion * keep);
                split.Slots.Insert(before ? i : i + 1, new DockSlot { Id = id, Proportion = given });
                return true;
            }

            if (slot.Split is not null && PlaceBeside(slot.Split, anchor, id, before, keep))
                return true;
        }

        return false;
    }

    private static bool GroupBlameWithDiff(DockSplit split)
    {
        foreach (var slot in split.Slots)
        {
            if (slot.Id == DockIds.Diff && slot.Split is null && slot.Tabs is not { Count: > 0 })
            {
                slot.Id = "";
                slot.Tabs = [DockIds.Diff, DockIds.Blame];
                slot.Active = DockIds.Diff;
                return true;
            }

            if (slot.Split is not null && GroupBlameWithDiff(slot.Split))
                return true;
        }

        return false;
    }

    private static bool Holds(DockSplit split, string id)
    {
        foreach (var slot in split.Slots)
        {
            if (slot.Id == id || slot.Tabs?.Contains(id) == true)
                return true;
            if (slot.Split is not null && Holds(slot.Split, id))
                return true;
        }

        return false;
    }

    private static void Rewrite(DockSplit? split)
    {
        if (split is null)
            return;
        foreach (var slot in split.Slots)
        {
            if (slot.Id == DockIds.Documents)
                slot.Id = DockIds.Diff;
            Rewrite(slot.Split);
        }
    }

    private static bool Walk(DockSplit split, HashSet<string> ids)
    {
        if (split.Orientation is not ("horizontal" or "vertical") || split.Slots is not { Count: > 0 })
            return false;
        foreach (var slot in split.Slots)
        {
            if (slot.Proportion <= 0 || double.IsNaN(slot.Proportion) || double.IsInfinity(slot.Proportion))
                return false;
            if (slot.Split is not null)
            {
                if (slot.Id.Length > 0 || slot.Tabs is { Count: > 0 } || !Walk(slot.Split, ids))
                    return false;
                continue;
            }

            if (slot.Tabs is { Count: > 0 } tabs)
            {
                if (slot.Id.Length > 0)
                    return false;
                foreach (var tab in tabs)
                {
                    if (!DockIds.IsKnown(tab) || !ids.Add(tab))
                        return false;
                }

                if (slot.Active.Length > 0 && !tabs.Contains(slot.Active))
                    return false;
                continue;
            }

            if (!DockIds.IsKnown(slot.Id) || !ids.Add(slot.Id))
                return false;
        }

        return true;
    }

    private static bool IsSide(string side) => side is "left" or "right" or "top" or "bottom";
}
