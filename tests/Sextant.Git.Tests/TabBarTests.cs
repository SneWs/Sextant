using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.Git;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class TabBarTests
{
    [Fact]
    public async Task Empty_window_draws_no_tab_rule_and_an_open_tab_draws_one()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-tabs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner());
                var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var left = window.FindControl<Border>("PaneRuleLeft");
                var right = window.FindControl<Border>("PaneRuleRight");
                Assert.NotNull(left);
                Assert.NotNull(right);
                Assert.False(left.IsVisible);
                Assert.False(right.IsVisible);
                Assert.Equal(0, left.Width);
                Assert.Equal(0, right.Width);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("tab") && border.IsEffectivelyVisible);

                var tab = new RepositoryViewModel(vm, Path.Combine(directory, "repo"));
                vm.Tabs.Add(tab);
                vm.ActiveTab = tab;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Assert.True(left.IsVisible || right.IsVisible);
                Assert.True(left.Width + right.Width > 1);
                Assert.Contains(window.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("tab") && border.IsEffectivelyVisible);

                await vm.Close(tab);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Assert.False(left.IsVisible);
                Assert.False(right.IsVisible);
                Assert.Equal(0, left.Width);
                Assert.Equal(0, right.Width);

                await vm.Shutdown();
                window.DataContext = null;
                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Switching_to_a_tab_refreshes_that_repository()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var left = new TempRepo();
        using var right = new TempRepo();
        left.WriteFile("a.txt", "one\n");
        left.CommitAll("left");
        right.WriteFile("b.txt", "one\n");
        right.CommitAll("right");
        var directory = Path.Combine(Path.GetTempPath(), "sextant-tab-refresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner());
                vm.GitExecutable = left.Git;
                vm.GitReady = true;
                var first = new RepositoryViewModel(vm, left.Directory);
                var second = new RepositoryViewModel(vm, right.Directory);
                vm.Tabs.Add(first);
                vm.Tabs.Add(second);

                await vm.Activate(first);
                Assert.True(first.IsReady);
                await vm.Activate(second);
                Assert.True(second.IsReady);

                left.WriteFile("fresh.txt", "new\n");
                Assert.DoesNotContain(first.Files, row => row.Path == "fresh.txt");

                await vm.Activate(first);
                Assert.Contains(first.Files, row => row.Path == "fresh.txt");

                await vm.Shutdown();
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Next_tab_and_close_commands_wait_for_loading_and_refresh()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var left = new TempRepo();
        using var right = new TempRepo();
        left.WriteFile("a.txt", "one\n");
        left.CommitAll("left");
        right.WriteFile("b.txt", "one\n");
        right.CommitAll("right");
        right.WriteFile("b.txt", "changed\n");
        var directory = Path.Combine(Path.GetTempPath(), "sextant-tab-commands-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner())
                {
                    GitExecutable = left.Git,
                    GitReady = true,
                };
                var first = new RepositoryViewModel(vm, left.Directory);
                var second = new RepositoryViewModel(vm, right.Directory);
                vm.Tabs.Add(first);
                vm.Tabs.Add(second);
                try
                {
                    await vm.Activate(first);
                    await vm.NextTabCommand.ExecuteAsync(null);
                    Assert.Same(second, vm.ActiveTab);
                    Assert.True(second.IsReady);
                    Assert.Contains(second.Files, row => row.Path == "b.txt");

                    right.WriteFile("refresh.txt", "new\n");
                    await vm.RefreshActiveCommand.ExecuteAsync(null);
                    Assert.Contains(second.Files, row => row.Path == "refresh.txt");

                    left.WriteFile("after-close.txt", "new\n");
                    await second.CloseTabCommand.ExecuteAsync(null);
                    Assert.DoesNotContain(second, vm.Tabs);
                    Assert.Same(first, vm.ActiveTab);
                    Assert.Contains(first.Files, row => row.Path == "after-close.txt");

                    await vm.CloseActiveCommand.ExecuteAsync(null);
                    Assert.Empty(vm.Tabs);
                    Assert.Null(vm.ActiveTab);
                }
                finally
                {
                    await vm.Shutdown();
                }
            }, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Shutdown_shares_completion_and_window_close_waits_for_it()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("initial");
        var directory = Path.Combine(Path.GetTempPath(), "sextant-tab-shutdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner())
                {
                    GitExecutable = repo.Git,
                    GitReady = true,
                };
                var tab = new RepositoryViewModel(vm, repo.Directory);
                vm.Tabs.Add(tab);
                await vm.Activate(tab);
                var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
                var closed = false;
                window.Closed += (_, _) => closed = true;
                window.Show();
                await vm.InitializeAsync();
                try
                {
                    window.Close();
                    Assert.False(closed);
                    var shutdown = vm.Shutdown();
                    Assert.Same(shutdown, vm.Shutdown());
                    await shutdown;
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(closed);
                    Assert.True(RepoPath.Same(repo.Directory, Assert.Single(store.LoadWorkspace().OpenTabs)));
                }
                finally
                {
                    await vm.Shutdown();
                    window.DataContext = null;
                    window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task File_menu_exit_is_last_and_closes_the_window()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-exit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner());
                var window = new MainWindow { DataContext = vm, Width = 800, Height = 600 };
                var closed = false;
                window.Closed += (_, _) => closed = true;
                window.Show();

                var menu = NativeMenu.GetMenu(window);
                Assert.NotNull(menu);
                var file = Assert.IsType<NativeMenuItem>(Assert.Single(menu.Items, item => item is NativeMenuItem { Header: "_File" }));
                var items = file.Menu!.Items;
                Assert.IsType<NativeMenuItemSeparator>(items[^2]);
                var exit = Assert.IsType<NativeMenuItem>(items[^1]);
                Assert.Equal("E_xit", exit.Header);
                Assert.Same(vm.ExitCommand, exit.Command);

                exit.Command!.Execute(null);
                Assert.False(closed);
                await vm.Shutdown();
                Dispatcher.UIThread.RunJobs();
                Assert.True(closed);
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
