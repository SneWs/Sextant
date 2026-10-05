using Avalonia.Headless;
using Sextant.Git;
using Sextant.Services;
using Sextant.ViewModels;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class LocationSectionTests
{
    [Fact]
    public async Task Stashes_and_submodules_stay_visible_when_empty()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new LocationsHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            Assert.Equal("Stashes (0)", Header(vm, "h:stashes").Label);
            Assert.False(Header(vm, "h:stashes").HasChildren);
            Assert.Equal("Submodules (0)", Header(vm, "h:submodules").Label);
            Assert.False(Header(vm, "h:submodules").HasChildren);

            repo.WriteFile("a.txt", "two\n");
            repo.Run("stash", "push", "-m", "wip note");
            await vm.Refresh();

            var stashes = Header(vm, "h:stashes");
            Assert.Equal("Stashes (1)", stashes.Label);
            Assert.True(stashes.HasChildren);
            Assert.Contains(vm.Locations, item => item.Key.StartsWith("s:", StringComparison.Ordinal) && item.Label.Contains("wip note", StringComparison.Ordinal));
            Assert.Equal("Submodules (0)", Header(vm, "h:submodules").Label);
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task A_submodule_is_a_row_under_the_submodules_root()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var child = new TempRepo();
        child.WriteFile("lib.txt", "lib");
        child.CommitAll("lib");
        using var parent = new TempRepo();
        parent.WriteFile("readme.txt", "parent");
        parent.CommitAll("parent");
        parent.Run("config", "protocol.file.allow", "always");
        parent.Run("-c", "protocol.file.allow=always", "submodule", "add", child.Directory, "vendor/lib");
        parent.CommitAll("add submodule");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new LocationsHost(parent.Git), parent.Directory);
            await vm.EnsureLoadedAsync();
            Assert.Equal("Submodules (1)", Header(vm, "h:submodules").Label);
            Assert.Contains(vm.Locations, item => item.Key.Replace('\\', '/') == "u:vendor/lib");
            Assert.Equal("Stashes (0)", Header(vm, "h:stashes").Label);
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    private static LocationItem Header(RepositoryViewModel vm, string key) =>
        vm.Locations.Single(item => item.Key == key);

    private sealed class LocationsHost(string git) : IWorkspaceHost
    {
        public IDialogService? Dialogs => null;

        public GitProcessRunner Runner { get; } = new();

        public string? GitExecutable { get; } = git;

        public string? MergeTool => null;

        public bool GitReady => true;

        public void Activate(RepositoryViewModel tab)
        {
        }

        public void Close(RepositoryViewModel tab)
        {
        }

        public void NoteLoaded(RepositoryViewModel tab)
        {
        }

        public void Save()
        {
        }

        public Task OpenRepositoryAsync(string path) => Task.CompletedTask;
    }
}
