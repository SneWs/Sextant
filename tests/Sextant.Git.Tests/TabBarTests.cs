using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
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
                Assert.False(window.FindControl<Button>("TabOverflow")!.IsVisible);
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
                    store.LoadWorkspace().OpenTabs.Select(tab => tab.Path));

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
    public async Task Overflowing_repository_tabs_open_from_a_menu()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-tab-overflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner());
                var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
                var tabs = new List<RepositoryViewModel>();
                window.Show();
                Settle(window);
                Assert.False(window.FindControl<Button>("TabOverflow")!.IsVisible);

                for (var i = 0; i < 12; i++)
                {
                    var name = i == 3 ? "repo_under_score" : $"repository-with-a-very-long-name-{i:00}";
                    var tab = new RepositoryViewModel(vm, Path.Combine(directory, name));
                    tabs.Add(tab);
                    vm.Tabs.Add(tab);
                }

                vm.ActiveTab = tabs[^1];
                Settle(window);

                var bar = window.FindControl<Grid>("RepositoryTabBar")!;
                var overflow = window.FindControl<Button>("TabOverflow")!;
                Assert.DoesNotContain(bar.GetVisualDescendants(), visual => visual is ScrollViewer || visual is ScrollBar { IsVisible: true });
                Assert.True(overflow.IsVisible);
                Assert.True(overflow.Bounds.Width > 1);

                var placed = PlacedTabs(window);
                var hidden = tabs.Except(placed.Select(item => (RepositoryViewModel)item.DataContext!)).ToList();
                Assert.NotEmpty(placed);
                Assert.NotEmpty(hidden);
                Assert.Contains(placed, item => ReferenceEquals(item.DataContext, tabs[^1]));
                foreach (var item in placed)
                {
                    var right = item.TranslatePoint(new Point(item.Bounds.Width, 0), bar)!.Value.X;
                    var buttonLeft = overflow.TranslatePoint(default, bar)!.Value.X;
                    Assert.True(right <= buttonLeft + 0.5, $"tab ends at {right}, button starts at {buttonLeft}");
                    Assert.Same(item, HitTab(window, item));
                }

                // The real app replaces the button template with a transparent content presenter. The square still has to hit.
                overflow.Background = Brushes.Transparent;
                overflow.Padding = new Thickness(0);
                overflow.Template = new FuncControlTemplate<Button>((button, _) => new ContentPresenter
                {
                    Background = Brushes.Transparent,
                    Content = button.Content,
                    Padding = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                });
                Settle(window);
                Assert.Same(overflow, HitButton(window, overflow));

                var flyout = Assert.IsType<MenuFlyout>(overflow.Flyout);
                Assert.Equal(hidden, flyout.Items.OfType<MenuItem>().Select(item => Assert.IsType<RepositoryViewModel>(item.Tag)).ToList());
                foreach (var item in flyout.Items.OfType<MenuItem>())
                {
                    var tab = Assert.IsType<RepositoryViewModel>(item.Tag);
                    var header = Assert.IsType<string>(item.Header);
                    Assert.StartsWith(tab.Title.Replace("_", "__", StringComparison.Ordinal), header, StringComparison.Ordinal);
                }
                var click = Center(overflow, window);
                window.MouseDown(click, MouseButton.Left);
                window.MouseUp(click, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var titles = hidden.Select(tab => tab.Title).ToList();
                var presenter = flyout.Popup.Child;
                var popupText = MenuText(presenter);
                Assert.True(flyout.IsOpen, $"flyout closed. items={flyout.Items.Count}");
                Assert.NotNull(presenter);
                Assert.True(presenter.Bounds.Height > 40 && presenter.Bounds.Width > 40,
                    $"presenter {presenter.Bounds} texts={string.Join("|", popupText)}");
                var menuOrigin = presenter.TranslatePoint(default, window);
                if (menuOrigin is { } at)
                    Assert.True(at.Y >= 20, $"menu origin {at} presenter {presenter.Bounds}");
                foreach (var title in titles)
                {
                    var escaped = title.Replace("_", "__", StringComparison.Ordinal);
                    Assert.Contains(popupText, text =>
                        !text.Contains("@0x", StringComparison.Ordinal)
                        && (text.StartsWith(title, StringComparison.Ordinal) || text.StartsWith(escaped, StringComparison.Ordinal)));
                }
                flyout.Hide();

                var chosen = hidden[0];
                var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(flyout.Items.OfType<MenuItem>().First().Command);
                await command.ExecuteAsync(null);
                Settle(window);

                Assert.Same(chosen, vm.ActiveTab);
                var shown = PlacedTabs(window).Single(item => ReferenceEquals(item.DataContext, chosen));
                Assert.Same(shown, HitTab(window, shown));
                Assert.True(overflow.IsVisible);

                window.Width = 4000;
                Settle(window);
                Assert.False(overflow.IsVisible);
                Assert.Equal(tabs, PlacedTabs(window).Select(item => (RepositoryViewModel)item.DataContext!).ToList());

                window.Width = 1000;
                Settle(window);
                Assert.True(overflow.IsVisible, "button hidden after the window was narrowed");
                flyout = Assert.IsType<MenuFlyout>(overflow.Flyout);
                var resized = flyout.Items.OfType<MenuItem>().Select(item => item.Tag).OfType<RepositoryViewModel>().Select(tab => tab.Title).ToList();
                Assert.NotEmpty(resized);
                click = Center(overflow, window);
                window.MouseDown(click, MouseButton.Left);
                window.MouseUp(click, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.True(flyout.IsOpen, $"flyout closed after resize. items={flyout.Items.Count} openItems={string.Join(",", flyout.Items.OfType<MenuItem>().Select(item => item.Header))}");
                presenter = flyout.Popup.Child;
                popupText = MenuText(presenter);
                Assert.True(presenter is { Bounds.Height: > 40 }, $"presenter after resize {presenter?.Bounds} {string.Join("|", popupText)}");
                Assert.Contains(popupText, text => resized.Any(title => text.StartsWith(title, StringComparison.Ordinal) && !text.Contains("@0x")));
                flyout.Hide();

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

    private static List<string> MenuText(Visual? presenter) =>
        presenter?.GetVisualDescendants().OfType<TextBlock>()
            .Select(text => $"{text.Text}@{text.Bounds.Width:0}x{text.Bounds.Height:0}")
            .ToList() ?? [];

    private static void Settle(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static List<TabStripItem> PlacedTabs(Window window) =>
        window.GetVisualDescendants().OfType<TabStripItem>().Where(item => item.Bounds.Width >= 1).ToList();

    private static TabStripItem? HitTab(Window window, Control control)
    {
        var hit = Hit(window, control);
        while (hit is not null)
        {
            if (hit is TabStripItem item)
                return item;
            hit = hit.GetVisualParent();
        }

        return null;
    }

    private static Button? HitButton(Window window, Control control)
    {
        var hit = Hit(window, control);
        while (hit is not null)
        {
            if (hit is Button button)
                return button;
            hit = hit.GetVisualParent();
        }

        return null;
    }

    private static Visual? Hit(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(point);
        return window.InputHitTest(point!.Value) as Visual;
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
            workspace.OpenTabs =
            [
                new SavedTab { Path = activeRepo.Directory },
                new SavedTab { Path = missing },
                new SavedTab { Path = idleRepo.Directory },
            ];
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
                    Assert.True(RepoPath.Same(repo.Directory, Assert.Single(store.LoadWorkspace().OpenTabs).Path));
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
