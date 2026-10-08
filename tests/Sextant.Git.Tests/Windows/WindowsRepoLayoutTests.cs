using Sextant.Git.Workspace;

namespace Sextant.Git.Tests;

public class WindowsRepoLayoutTests
{
    [Fact]
    public void Repo_layouts_match_a_path_that_differs_only_by_case()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sextant-ws-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WorkspaceStore(directory);
            var state = new WorkspaceState();
            RepoLayouts.Remember(state, @"C:\repos\sextant", 310, 430, 190, showLocations: false);
            store.SaveWorkspace(state);
            var loaded = store.LoadWorkspace();
            Assert.Equal(310, RepoLayouts.Resolve(loaded, @"c:\repos\sextant").LocationsWidth);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
