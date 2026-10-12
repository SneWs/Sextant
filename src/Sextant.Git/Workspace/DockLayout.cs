namespace Sextant.Git.Workspace;

public static class DockIds
{
    public const string Locations = "locations";

    public const string History = "history";

    public const string Changes = "changes";

    public const string Files = "files";

    public const string Commands = "commands";

    public const string Diff = "diff";

    /// <summary>Older layouts used this id for the repository document dock. It is read as <see cref="Diff"/>.</summary>
    public const string Documents = "documents";

    public static readonly string[] Tools = [Locations, History, Changes, Files, Commands, Diff];

    public static bool IsKnown(string id) =>
        id is Locations or History or Changes or Files or Commands or Diff;
}

/// <summary>Sextant's own layout record. It is not Dock's object graph.</summary>
public sealed class DockLayout
{
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
    /// <summary>Tool or document id. Empty when <see cref="Split"/> is set.</summary>
    public string Id { get; set; } = "";

    public double Proportion { get; set; }

    public DockSplit? Split { get; set; }
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
        slots.Add(new DockSlot { Id = DockIds.History, Proportion = history / sum });
        slots.Add(new DockSlot
        {
            Proportion = details / sum,
            Split = new DockSplit
            {
                Orientation = "vertical",
                Slots =
                [
                    new DockSlot { Id = DockIds.Changes, Proportion = fileShare },
                    new DockSlot { Id = DockIds.Diff, Proportion = 1 - fileShare },
                ],
            },
        });
        return new DockLayout
        {
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
                if (slot.Id.Length > 0 || !Walk(slot.Split, ids))
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
