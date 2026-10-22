using Avalonia.Headless;
using Sextant.Git;
using Sextant.Git.Workspace;
using Sextant.Git.Wsl;
using Sextant.ViewModels;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class WslTabTests
{
    [Fact]
    public async Task Restored_tabs_show_the_distro_before_the_repository_loads()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-wsl-tabs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var ubuntu = @"\\wsl.localhost\Ubuntu\home\user\repo";
            var debian = @"\\wsl.localhost\Debian\home\user\other";
            var alpine = @"\\wsl$\Alpine\home\user\alpine";
            var store = new WorkspaceStore(directory);
            store.SaveWorkspace(new WorkspaceState
            {
                OpenTabs =
                [
                    new SavedTab { Path = ubuntu, Wsl = "Ubuntu" },
                    new SavedTab { Path = debian, Wsl = "Debian" },
                    new SavedTab { Path = alpine },
                ],
                ActiveTab = ubuntu,
            });

            await session.Dispatch(() =>
            {
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings { ReopenTabs = true }, new GitProcessRunner());
                var active = vm.RestoreSavedTabs();
                Assert.Same(vm.Tabs[0], active);
                Assert.Equal(["repo (Ubuntu)", "other (Debian)", "alpine (Alpine)"], vm.Tabs.Select(tab => tab.Title));
                Assert.Null(vm.Tabs[0].Wsl);
                Assert.Equal(["Ubuntu", "Debian", "Alpine"], vm.Tabs.Select(tab => tab.WslDistribution));
                Assert.False(vm.Tabs[0].IsReady);
                Assert.False(vm.Tabs[1].IsReady);
                Assert.False(vm.Tabs[2].IsReady);

                vm.Save();
                var again = store.LoadWorkspace();
                Assert.Equal("Ubuntu", again.OpenTabs[0].Wsl);
                Assert.Equal("Debian", again.OpenTabs[1].Wsl);
                Assert.Equal(WslPath.CanonicalWindows(alpine), again.OpenTabs[2].Path);
                Assert.Equal("Alpine", again.OpenTabs[2].Wsl);
                return Task.FromResult(0);
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
