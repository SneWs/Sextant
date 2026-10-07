namespace Sextant.Git.Tests;

public class WorkspaceStoreTests
{
    [Fact]
    public void Workspace_round_trip_preserves_tabs_and_settings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sextant-ws-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WorkspaceStore(directory);
            var state = new WorkspaceState
            {
                ActiveTab = @"C:\repos\sextant",
                OpenTabs = [@"C:\repos\sextant", @"C:\repos\other"],
                LocationsWidth = 200,
                GraphWidth = 480,
                FilesHeight = 160,
                WindowWidth = 1440,
                WindowHeight = 900,
                WindowX = 40,
                WindowY = 20,
                WindowMaximized = true,
            };
            store.SaveWorkspace(state);
            store.SaveSettings(new AppSettings
            {
                GitExecutable = @"C:\git\git.exe",
                ReopenTabs = false,
                SideBySide = true,
                IgnoreWhitespace = true,
                Theme = ThemePreference.Dark,
                Palette = PalettePreference.Gruvbox,
                MergeTool = "meld \"$LOCAL\" \"$MERGED\" \"$REMOTE\"",
            });

            var loaded = store.LoadWorkspace();
            var settings = store.LoadSettings();
            Assert.Equal(@"C:\repos\sextant", loaded.ActiveTab);
            Assert.Equal([@"C:\repos\sextant", @"C:\repos\other"], loaded.OpenTabs);
            Assert.Equal(200, loaded.LocationsWidth);
            Assert.Equal(480, loaded.GraphWidth);
            Assert.Equal(160, loaded.FilesHeight);
            Assert.Equal(1440, loaded.WindowWidth);
            Assert.Equal(900, loaded.WindowHeight);
            Assert.Equal(40, loaded.WindowX);
            Assert.Equal(20, loaded.WindowY);
            Assert.True(loaded.WindowMaximized);
            Assert.Equal(@"C:\git\git.exe", settings.GitExecutable);
            Assert.False(settings.ReopenTabs);
            Assert.True(settings.SideBySide);
            Assert.True(settings.IgnoreWhitespace);
            Assert.Equal(ThemePreference.Dark, settings.Theme);
            Assert.Equal(PalettePreference.Gruvbox, settings.Palette);
            Assert.Equal("meld \"$LOCAL\" \"$MERGED\" \"$REMOTE\"", settings.MergeTool);
            Assert.Equal(ThemePreference.System, ThemePreference.Normalize(null));
            Assert.Equal(ThemePreference.Light, ThemePreference.Normalize(" Light "));
            Assert.Equal(ThemePreference.System, ThemePreference.Normalize("nope"));

            File.WriteAllText(Path.Combine(directory, "settings.json"), """{ "reopenTabs": true }""");
            var older = store.LoadSettings();
            Assert.Null(older.GitExecutable);
            Assert.True(older.ReopenTabs);
            Assert.False(older.SideBySide);
            Assert.False(older.IgnoreWhitespace);
            Assert.Equal(ThemePreference.System, older.Theme);
            Assert.Equal(PalettePreference.Catppuccin, older.Palette);
            Assert.Null(older.MergeTool);

            File.WriteAllText(Path.Combine(directory, "workspace.json"), """
                {
                  "pins": [{ "path": "C:\\old", "name": "old" }],
                  "pinsWidth": 240,
                  "openTabs": ["C:\\repos\\kept"],
                  "activeTab": "C:\\repos\\kept"
                }
                """);
            var legacy = store.LoadWorkspace();
            Assert.Equal(@"C:\repos\kept", legacy.ActiveTab);
            Assert.Equal([@"C:\repos\kept"], legacy.OpenTabs);
            Assert.Equal(0, legacy.WindowWidth);
            Assert.Equal(0, legacy.WindowHeight);
            Assert.Null(legacy.WindowX);
            Assert.Null(legacy.WindowY);
            Assert.False(legacy.WindowMaximized);
            var migrated = RepoLayouts.Resolve(legacy, @"C:\repos\kept");
            Assert.Equal(220, migrated.LocationsWidth);
            Assert.Equal(520, migrated.GraphWidth);
            Assert.Equal(180, migrated.FilesHeight);
            Assert.True(migrated.ShowLocations);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Pane_sizes_are_stored_per_repository()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sextant-ws-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WorkspaceStore(directory);
            var state = new WorkspaceState
            {
                LocationsWidth = 200,
                GraphWidth = 480,
                FilesHeight = 160,
            };
            var untouched = RepoLayouts.Resolve(state, @"C:\repos\new");
            Assert.Equal(200, untouched.LocationsWidth);
            Assert.Equal(480, untouched.GraphWidth);
            Assert.Equal(160, untouched.FilesHeight);
            Assert.True(untouched.ShowLocations);

            RepoLayouts.Remember(state, @"C:\repos\sextant", 310, 430, 190, showLocations: false);
            RepoLayouts.Remember(state, @"C:\repos\other", 180, 640, 220);
            store.SaveWorkspace(state);
            var loaded = store.LoadWorkspace();
            var sextant = RepoLayouts.Resolve(loaded, @"C:\repos\sextant");
            var other = RepoLayouts.Resolve(loaded, @"C:\repos\other");
            var third = RepoLayouts.Resolve(loaded, @"C:\repos\third");
            Assert.Equal(310, sextant.LocationsWidth);
            Assert.Equal(430, sextant.GraphWidth);
            Assert.Equal(190, sextant.FilesHeight);
            Assert.False(sextant.ShowLocations);
            Assert.Equal(180, other.LocationsWidth);
            Assert.True(other.ShowLocations);
            Assert.Equal(640, other.GraphWidth);
            Assert.Equal(220, other.FilesHeight);
            Assert.Equal(200, third.LocationsWidth);
            Assert.Equal(480, third.GraphWidth);
            Assert.Equal(160, third.FilesHeight);
            Assert.True(third.ShowLocations);

            File.WriteAllText(Path.Combine(directory, "workspace.json"), """
                {
                  "repoLayouts": {
                    "C:\\repos\\old": { "locationsWidth": 250, "graphWidth": 400, "filesHeight": 100 }
                  }
                }
                """);
            var older = RepoLayouts.Resolve(store.LoadWorkspace(), @"C:\repos\old");
            Assert.Equal(250, older.LocationsWidth);
            Assert.True(older.ShowLocations);
            Assert.Empty(older.HiddenBranches);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Hidden_branches_are_stored_per_repository()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sextant-ws-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WorkspaceStore(directory);
            var state = store.LoadWorkspace();
            RepoLayouts.Remember(
                state,
                @"C:\repos\sextant",
                220,
                520,
                180,
                hiddenBranches: ["refs/tags/v1", "refs/heads/feature", "refs/remotes/origin/HEAD", "refs/remotes/origin/side", "stash:abc"]);
            RepoLayouts.Remember(state, @"C:\repos\sextant", 310, 520, 180);
            RepoLayouts.Remember(state, @"C:\repos\other", 180, 640, 220, hiddenBranches: []);
            store.SaveWorkspace(state);

            var loaded = store.LoadWorkspace();
            var sextant = RepoLayouts.Resolve(loaded, @"C:\repos\sextant");
            var other = RepoLayouts.Resolve(loaded, @"C:\repos\other");
            Assert.Equal(310, sextant.LocationsWidth);
            Assert.Equal(["refs/heads/feature", "refs/remotes/origin/side", "stash:abc"], sextant.HiddenBranches);
            Assert.Empty(other.HiddenBranches);

            RepoLayouts.Remember(loaded, @"C:\repos\sextant", 310, 520, 180, hiddenBranches: []);
            Assert.Empty(RepoLayouts.Resolve(loaded, @"C:\repos\sextant").HiddenBranches);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
