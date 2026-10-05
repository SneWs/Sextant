using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sextant.Git;

public sealed class WorkspaceState
{
    public List<string> OpenTabs { get; set; } = [];

    public string? ActiveTab { get; set; }

    public double LocationsWidth { get; set; } = 220;

    public double GraphWidth { get; set; } = 520;

    public double FilesHeight { get; set; } = 180;

    public Dictionary<string, RepoLayout> RepoLayouts { get; set; } = [];

    public double WindowWidth { get; set; }

    public double WindowHeight { get; set; }

    public int? WindowX { get; set; }

    public int? WindowY { get; set; }

    public bool WindowMaximized { get; set; }
}

public sealed class RepoLayout
{
    public double LocationsWidth { get; set; } = 220;

    public double GraphWidth { get; set; } = 520;

    public double FilesHeight { get; set; } = 180;

    /// <summary>False hides the locations column. Missing values in older files stay visible.</summary>
    public bool ShowLocations { get; set; } = true;

    /// <summary>Branch refs and stash tokens left out of the commit graph. Missing values in older files show every branch and stash.</summary>
    public List<string> HiddenBranches { get; set; } = [];
}

public static class RepoLayouts
{
    public static RepoLayout Resolve(WorkspaceState state, string path)
    {
        var own = TryGet(state, path);
        return own ?? FromLegacy(state);
    }

    public static RepoLayout? TryGet(WorkspaceState state, string path)
    {
        foreach (var pair in state.RepoLayouts)
        {
            if (pair.Value is null || string.IsNullOrWhiteSpace(pair.Key))
                continue;
            if (!Same(pair.Key, path))
                continue;
            return new RepoLayout
            {
                LocationsWidth = pair.Value.LocationsWidth,
                GraphWidth = pair.Value.GraphWidth,
                FilesHeight = pair.Value.FilesHeight,
                ShowLocations = pair.Value.ShowLocations,
                HiddenBranches = CopyHidden(pair.Value.HiddenBranches),
            };
        }

        return null;
    }

    public static void Remember(
        WorkspaceState state,
        string path,
        double locations,
        double graph,
        double files,
        bool showLocations = true,
        IReadOnlyList<string>? hiddenBranches = null)
    {
        var match = FindKey(state, path);
        var hidden = hiddenBranches is null
            ? CopyHidden(match is null ? null : state.RepoLayouts[match].HiddenBranches)
            : CopyHidden(hiddenBranches);
        if (match is not null && !string.Equals(match, path, StringComparison.Ordinal))
            state.RepoLayouts.Remove(match);
        state.RepoLayouts[path] = new RepoLayout
        {
            LocationsWidth = locations,
            GraphWidth = graph,
            FilesHeight = files,
            ShowLocations = showLocations,
            HiddenBranches = hidden,
        };
    }

    private static List<string> CopyHidden(IEnumerable<string>? names)
    {
        if (names is null)
            return [];
        return names.Where(BranchVisibility.IsRemembered)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    public static void Forget(WorkspaceState state, string path)
    {
        var match = FindKey(state, path);
        if (match is not null)
            state.RepoLayouts.Remove(match);
    }

    private static string? FindKey(WorkspaceState state, string path)
    {
        foreach (var key in state.RepoLayouts.Keys)
        {
            if (!string.IsNullOrWhiteSpace(key) && Same(key, path))
                return key;
        }

        return null;
    }

    private static bool Same(string left, string right)
    {
        try
        {
            return RepoPath.Same(left, right);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static RepoLayout FromLegacy(WorkspaceState state) => new()
    {
        LocationsWidth = state.LocationsWidth >= 140 ? state.LocationsWidth : 220,
        GraphWidth = state.GraphWidth >= 240 ? state.GraphWidth : 520,
        FilesHeight = state.FilesHeight >= 80 ? state.FilesHeight : 180,
    };
}

public sealed class AppSettings
{
    /// <summary>Absolute path of the git executable. Empty means the git on PATH.</summary>
    public string? GitExecutable { get; set; }

    public bool ReopenTabs { get; set; } = true;

    public bool SideBySide { get; set; }

    public bool IgnoreWhitespace { get; set; }

    /// <summary><see cref="ThemePreference.System"/>, <see cref="ThemePreference.Light"/>, or <see cref="ThemePreference.Dark"/>.</summary>
    public string Theme { get; set; } = ThemePreference.System;

    /// <summary>Command git mergetool runs for a conflict. Empty uses the in-app editor unless git has merge.tool.</summary>
    public string? MergeTool { get; set; }
}

public static class AppPaths
{
    public static string ConfigDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sextant");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sextant");

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var root = string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            : xdg;
        return Path.Combine(root, "sextant");
    }
}

public sealed class WorkspaceStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory;

    public WorkspaceStore(string directory) => _directory = directory;

    public WorkspaceState LoadWorkspace() => Load("workspace.json", new WorkspaceState());

    public AppSettings LoadSettings() => Load("settings.json", new AppSettings());

    public void SaveWorkspace(WorkspaceState state) => Save("workspace.json", state);

    public void SaveSettings(AppSettings settings) => Save("settings.json", settings);

    private T Load<T>(string fileName, T fallback)
    {
        var path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
            return fallback;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
        catch (IOException)
        {
            return fallback;
        }
    }

    private void Save<T>(string fileName, T value)
    {
        Directory.CreateDirectory(_directory);
        var destination = Path.Combine(_directory, fileName);
        var temporary = destination + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
        File.Move(temporary, destination, overwrite: true);
    }
}
