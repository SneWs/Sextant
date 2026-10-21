using Avalonia.Media;
using Avalonia.Threading;

namespace Sextant.ViewModels;

/// <summary>Stroke icons for location rows that open onto other rows.</summary>
public static class LocationIcons
{
    public const string Branches = "Branches";

    public const string Remote = "Remote";

    public const string Tags = "Tags";

    public const string Remotes = "Remotes";

    public const string Stashes = "Stashes";

    public const string Submodules = "Submodules";

    public const string Worktrees = "Worktrees";

    public const string Folder = "Folder";

    // A geometry belongs to the dispatcher that created it. Headless tests replace that
    // dispatcher between sessions, so a process-wide instance throws when the next session draws it.
    private static readonly (string Name, string Markup)[] Icons =
    [
        (Branches, "M4,3 V11 M4,7 C4,10 8,10 11,10 M5.6,3 A1.6,1.6 0 1 1 2.4,3 A1.6,1.6 0 1 1 5.6,3 M5.6,11 A1.6,1.6 0 1 1 2.4,11 A1.6,1.6 0 1 1 5.6,11 M12.6,10 A1.6,1.6 0 1 1 9.4,10 A1.6,1.6 0 1 1 12.6,10"),
        (Remote, "M8,1.5 A5.2,5.2 0 1 1 7.9,1.5 M3.2,6.7 H12.8 M8,1.6 C6.2,3.6 6.2,9.6 8,11.8 M8,1.6 C9.8,3.6 9.8,9.6 8,11.8"),
        (Tags, "M1.5,6.5 L6.5,1.5 H13 V8 L6.5,14.5 Z M10.2,4.6 A1.1,1.1 0 1 1 8,4.6 A1.1,1.1 0 1 1 10.2,4.6"),
        (Stashes, "M1.5,2.5 H14 V6 H1.5 Z M2.5,7.2 H13 V10.2 H2.5 Z M3.5,11.4 H12 V14 H3.5 Z"),
        (Submodules, "M1,5 H8.5 V12.5 H1 Z M6.5,2.5 H14 V10 H6.5 Z"),
        (Worktrees, "M1,2 H9 V13 H1 Z M10,5 H15 V13 H10 Z"),
        (Folder, "M1,3 L6,3 L8,5 L15,5 L15,13 L1,13 Z"),
    ];

    private static readonly Geometry?[] Live = new Geometry?[7];

    public static string ForSection(string key)
    {
        if (key == "h:branches")
            return Branches;
        if (key.StartsWith("h:remote:", StringComparison.Ordinal))
            return Remote;
        if (key == "h:tags")
            return Tags;
        if (key == "h:remotes")
            return Remotes;
        if (key == "h:stashes")
            return Stashes;
        if (key == "h:submodules")
            return Submodules;
        if (key == "h:worktrees")
            return Worktrees;
        return Folder;
    }

    public static Geometry? GeometryFor(string name)
    {
        var index = Index(name);
        if (index < 0)
            return null;

        var cached = Live[index];
        if (cached is not null && cached.CheckAccess())
            return cached;

        // Parse on this thread. The shape remembers that dispatcher, and the next headless
        // session would throw from Shape.MeasureOverride if it drew the previous one.
        var geometry = Geometry.Parse(Icons[index].Markup);
        if (Dispatcher.UIThread.CheckAccess())
            Live[index] = geometry;
        return geometry;
    }

    private static int Index(string name)
    {
        if (name == Remotes)
            name = Remote;
        for (var i = 0; i < Icons.Length; i++)
        {
            if (Icons[i].Name == name)
                return i;
        }

        return -1;
    }
}
