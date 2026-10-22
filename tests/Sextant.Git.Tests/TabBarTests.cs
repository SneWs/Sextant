using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.Git;
using Sextant.Git.Repo;
using Sextant.Git.Workspace;
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

                Assert.DoesNotContain(VisibleTabs(window), item => item.IsEffectivelyVisible);

                var tab = new RepositoryViewModel(vm, Path.Combine(directory, "repo"));
                var other = new RepositoryViewModel(vm, Path.Combine(directory, "other"));
                vm.Tabs.Add(tab);
                vm.Tabs.Add(other);
                vm.ActiveTab = other;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var shown = VisibleTabs(window).Where(item => item.IsEffectivelyVisible).ToList();
                Assert.Equal(2, shown.Count);
                Assert.False(shown[0].IsSelected);
                Assert.True(shown[1].IsSelected);
                Assert.InRange(shown[1].Bounds.Height, 20, 32);
                Assert.Equal(window.FindResource("DockSurfaceHeaderActiveBrush"), shown[1].Background);
                Assert.Equal(window.FindResource("DockSurfacePanelBrush"), shown[0].Background);
                var close = shown[1].GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First(path => path.Data is not null);
                Assert.InRange(close.Bounds.Width, 1, 10);
                Assert.InRange(close.Bounds.Height, 1, 10);
                var dot = shown[1].GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().First(item => item.IsVisible);
                var title = shown[1].GetVisualDescendants().OfType<TextBlock>().First(item => item.Text == other.Title);
                var dotRight = dot.TranslatePoint(new Point(dot.Bounds.Width, 0), shown[1])!.Value.X;
                var titleLeft = title.TranslatePoint(new Point(0, 0), shown[1])!.Value.X;
                Assert.InRange(titleLeft - dotRight, 3, 8);

                await vm.Close(other);
                await vm.Close(tab);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Assert.DoesNotContain(VisibleTabs(window), item => item.IsEffectivelyVisible);

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
    public async Task Dragging_a_repository_tab_reorders_it_and_keeps_that_order()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-tab-drag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner());
                var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
                window.Show();
                var alpha = new RepositoryViewModel(vm, Path.Combine(directory, "alpha"));
                var beta = new RepositoryViewModel(vm, Path.Combine(directory, "beta"));
                var gamma = new RepositoryViewModel(vm, Path.Combine(directory, "gamma"));
                vm.Tabs.Add(alpha);
                vm.Tabs.Add(beta);
                vm.Tabs.Add(gamma);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var shown = VisibleTabs(window).Where(item => item.IsEffectivelyVisible).ToList();
                Assert.Equal(3, shown.Count);
                var start = Center(shown[0], window);
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(start + new Point(2, 0));
                window.MouseUp(start + new Point(2, 0), MouseButton.Left);
                Assert.Equal([alpha, beta, gamma], vm.Tabs);

                shown = VisibleTabs(window).Where(item => item.IsEffectivelyVisible).ToList();
                start = Center(shown[0], window);
                var close = shown[2].GetVisualDescendants().OfType<Button>().First();
                var closeAt = Center(close, window);
                window.MouseDown(closeAt, MouseButton.Left);
                window.MouseMove(start);
                window.MouseUp(start, MouseButton.Left);
                Assert.Equal([alpha, beta, gamma], vm.Tabs);

                shown = VisibleTabs(window).Where(item => item.IsEffectivelyVisible).ToList();
                start = Center(shown[0], window);
                var target = Center(shown[2], window);
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(target);
                window.MouseUp(target, MouseButton.Left);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal([beta, gamma, alpha], vm.Tabs);
                Assert.Equal(
                    [beta.RequestedPath, gamma.RequestedPath, alpha.RequestedPath],
                    store.LoadWorkspace().OpenTabs);

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

    private static Point Center(Control control, Visual relative)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), relative);
        Assert.NotNull(point);
        return point.Value;
    }

    private static IEnumerable<TabStripItem> VisibleTabs(Visual root) =>
        root.GetVisualDescendants().OfType<TabStripItem>();

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
    public async Task Startup_shows_saved_tabs_and_loads_only_the_active_repository()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var activeRepo = new TempRepo();
        activeRepo.WriteFile("a.txt", "one\n");
        activeRepo.CommitAll("active");
        using var idleRepo = new TempRepo();
        idleRepo.WriteFile("b.txt", "two\n");
        idleRepo.CommitAll("idle");
        var missing = Path.Combine(Path.GetTempPath(), "sextant-missing-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-startup-tabs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new WorkspaceStore(directory);
            var workspace = store.LoadWorkspace();
            workspace.OpenTabs = [activeRepo.Directory, missing, idleRepo.Directory];
            workspace.ActiveTab = activeRepo.Directory;
            store.SaveWorkspace(workspace);

            await session.Dispatch(async () =>
            {
                var vm = new MainViewModel(
                    store,
                    store.LoadWorkspace(),
                    new AppSettings { GitExecutable = activeRepo.Git, ReopenTabs = true },
                    new GitProcessRunner());
                await vm.InitializeAsync();
                try
                {
                    Assert.False(vm.ShowEmpty);
                    Assert.Equal(3, vm.Tabs.Count);
                    Assert.Same(vm.Tabs[0], vm.ActiveTab);
                    Assert.True(vm.ActiveTab!.IsReady);
                    Assert.Contains(vm.ActiveTab.Rows, row => row.Subject == "active");
                    Assert.False(vm.Tabs[1].IsReady);
                    Assert.False(vm.Tabs[2].IsReady);
                    Assert.Empty(vm.Tabs[2].Rows);
                    Assert.False(vm.Tabs[1].HasBanner);
                    Assert.False(vm.Tabs[2].HasBanner);
                }
                finally
                {
                    await vm.Shutdown();
                }
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
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
    public async Task File_menu_exit_is_visible_only_on_Windows_and_Linux_and_closes_the_window()
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
                var separator = Assert.IsType<NativeMenuItemSeparator>(items[^2]);
                var exit = Assert.IsType<NativeMenuItem>(items[^1]);
                Assert.Equal("E_xit", exit.Header);
                Assert.Same(vm.ExitCommand, exit.Command);
                var showExit = OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
                Assert.Equal(showExit, exit.IsVisible);
                Assert.Equal(showExit, separator.IsVisible);
                var lastVisible = items.OfType<NativeMenuItem>().Last(item => item.IsVisible);
                Assert.Equal(showExit ? "E_xit" : "_Init repository…", lastVisible.Header);

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
