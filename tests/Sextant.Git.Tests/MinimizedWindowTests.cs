using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Sextant.Git;
using Sextant.Git.Workspace;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class MinimizedWindowTests
{
    [Fact]
    public void Iconic_activation_is_the_only_message_the_taskbar_restore_ignores()
    {
        const uint activate = 0x0006;
        Assert.False(MainWindow.IsIconicActivation(activate, 1));
        Assert.False(MainWindow.IsIconicActivation(activate, 2));
        Assert.False(MainWindow.IsIconicActivation(activate, 0));
        Assert.False(MainWindow.IsIconicActivation(activate, 1 << 16));
        Assert.True(MainWindow.IsIconicActivation(activate, 1 | (1 << 16)));
        Assert.True(MainWindow.IsIconicActivation(activate, 2 | (1 << 16)));
        Assert.False(MainWindow.IsIconicActivation(0x0005, 1 | (1 << 16)));
    }

    [Fact]
    public async Task Restoring_a_minimized_window_refreshes_after_it_is_visible()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");

        await session.Dispatch(async () =>
        {
            var store = new WorkspaceStore(Path.Combine(repo.Directory, ".git", "sextant-workspace"));
            var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings { GitExecutable = repo.Git, ReopenTabs = false, RefreshOnFocus = true }, new GitProcessRunner())
            {
                GitExecutable = repo.Git,
                GitReady = true,
            };
            var tab = new RepositoryViewModel(vm, repo.Directory);
            vm.Tabs.Add(tab);
            await vm.Activate(tab);
            var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
            window.Show();
            await vm.InitializeAsync();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            try
            {
                await WaitUntilIdle(tab);
                Assert.False(tab.IsDirty);

                repo.WriteFile("new.txt", "while minimized\n");
                window.WindowState = WindowState.Minimized;
                Assert.Equal(WindowState.Minimized, window.WindowState);
                await window.RefreshAfterActivationAsync();
                Assert.False(tab.IsBusy, tab.BusyText);
                Assert.False(tab.IsDirty);

                window.WindowState = WindowState.Normal;
                Dispatcher.UIThread.RunJobs();
                await WaitUntilIdle(tab);
                Assert.True(tab.IsDirty);
                Assert.False(tab.IsBusy, tab.BusyText);
            }
            finally
            {
                await vm.Shutdown();
                window.DataContext = null;
                window.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Focusing_the_window_leaves_the_repository_until_refresh_on_focus_is_on()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");

        await session.Dispatch(async () =>
        {
            var store = new WorkspaceStore(Path.Combine(repo.Directory, ".git", "sextant-workspace"));
            var settings = new AppSettings { GitExecutable = repo.Git, ReopenTabs = false };
            var vm = new MainViewModel(store, store.LoadWorkspace(), settings, new GitProcessRunner())
            {
                GitExecutable = repo.Git,
                GitReady = true,
            };
            var tab = new RepositoryViewModel(vm, repo.Directory);
            vm.Tabs.Add(tab);
            await vm.Activate(tab);
            var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
            window.Show();
            await vm.InitializeAsync();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            try
            {
                await WaitUntilIdle(tab);
                Assert.False(settings.RefreshOnFocus);
                repo.WriteFile("new.txt", "while away\n");

                await window.RefreshAfterActivationAsync();
                await WaitUntilIdle(tab);
                Assert.False(tab.IsDirty);

                window.WindowState = WindowState.Minimized;
                window.WindowState = WindowState.Normal;
                Dispatcher.UIThread.RunJobs();
                await WaitUntilIdle(tab);
                Assert.False(tab.IsDirty);

                settings.RefreshOnFocus = true;
                await window.RefreshAfterActivationAsync();
                await WaitUntilIdle(tab);
                Assert.True(tab.IsDirty);
            }
            finally
            {
                await vm.Shutdown();
                window.DataContext = null;
                window.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    private static async Task WaitUntilIdle(RepositoryViewModel tab)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (tab.IsBusy && DateTime.UtcNow < until)
            await Task.Delay(30);
        Assert.False(tab.IsBusy, tab.BusyText);
        Assert.True(tab.IsReady);
    }
}
