using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant;
using Sextant.Git;
using Sextant.Git.Workspace;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class CommandPaletteTests
{
    [Fact]
    public async Task Command_palette_has_a_shadow_a_border_and_padded_rows()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-palette-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner());
                var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
                window.Show();
                vm.TogglePalette();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var card = window.FindControl<Border>("CommandPalette");
                Assert.NotNull(card);
                Assert.True(card.IsEffectivelyVisible);
                Assert.Equal(new Thickness(1), card.BorderThickness);
                Assert.NotNull(card.BorderBrush);
                Assert.True(card.BoxShadow.Count >= 2);
                Assert.True(card.BoxShadow[0].OffsetY > 0);
                Assert.True(card.BoxShadow[0].Blur > 0);
                Assert.True(card.BoxShadow[1].OffsetY > card.BoxShadow[0].OffsetY);
                Assert.True(card.BoxShadow[1].Blur > card.BoxShadow[0].Blur);

                var item = window.GetVisualDescendants().OfType<ListBoxItem>().First(row => row.IsEffectivelyVisible);
                Assert.Equal(new Thickness(12, 6, 12, 6), item.Padding);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Open repository");

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
    public async Task Command_palette_focuses_its_input_when_opened_from_the_keyboard()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-palette-focus-" + Guid.NewGuid().ToString("N"));
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

                var raw = AppGestures.Command == KeyModifiers.Meta
                    ? RawInputModifiers.Meta
                    : RawInputModifiers.Control;
                window.KeyPress(Key.P, raw, PhysicalKey.P, null);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var box = window.FindControl<TextBox>("PaletteBox");
                Assert.NotNull(box);
                Assert.True(vm.PaletteOpen);
                Assert.True(box.IsEffectivelyVisible);
                Assert.Same(box, window.FocusManager?.GetFocusedElement());

                window.KeyTextInput("toggle");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("toggle", vm.PaletteQuery);
                Assert.Equal("toggle", box.Text);

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
    public async Task Clicking_a_command_after_scrolling_runs_it_and_closes_the_palette()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-palette-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner());
                var tab = new RepositoryViewModel(vm, Path.Combine(directory, "repo"));
                Directory.CreateDirectory(tab.RequestedPath);
                vm.Tabs.Add(tab);
                await vm.Activate(tab);
                var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
                window.Show();
                vm.TogglePalette();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var list = window.FindControl<ListBox>("PaletteList");
                Assert.NotNull(list);
                var listPoint = list.TranslatePoint(new Point(30, 40), window);
                Assert.NotNull(listPoint);
                window.MouseWheel(listPoint.Value, new Vector(0, -240));
                Dispatcher.UIThread.RunJobs();
                Assert.True(vm.PaletteOpen);

                var command = vm.PaletteMatches.Single(item => item.Title == "Command log");
                list.ScrollIntoView(command);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var row = list.ContainerFromItem(command) as ListBoxItem;
                Assert.NotNull(row);
                var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window);
                Assert.NotNull(point);
                window.MouseDown(point.Value, MouseButton.Left);
                window.MouseUp(point.Value, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                Assert.False(vm.PaletteOpen);
                Assert.True(tab.CommandsOpen);
                Assert.Equal("Command log", vm.SelectedPalette?.Title);
                Assert.True(vm.Dock.IsOpen(DockIds.Commands));
                vm.ShowCommandsCommand.Execute(null);
                Assert.True(tab.CommandsOpen);
                Assert.True(vm.Dock.IsOpen(DockIds.Commands));

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
    public async Task Command_palette_lists_commands_from_a_to_z()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-palette-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner());
                var tab = new RepositoryViewModel(vm, Path.Combine(directory, "repo"));
                vm.Tabs.Add(tab);
                vm.ActiveTab = tab;
                var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
                window.Show();
                vm.TogglePalette();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var titles = vm.PaletteMatches.Select(item => item.Title).ToList();
                Assert.Equal(titles.OrderBy(title => title, StringComparer.OrdinalIgnoreCase), titles);
                Assert.Equal("Add remote", titles[0]);
                var first = window.GetVisualDescendants().OfType<ListBoxItem>().First(row => row.IsEffectivelyVisible);
                Assert.Equal("Add remote", first.GetVisualDescendants().OfType<TextBlock>().First().Text);

                vm.PaletteQuery = "toggle";
                Assert.Equal(
                    [
                        "Toggle ignore whitespace",
                        "Toggle side-by-side diff",
                    ],
                    vm.PaletteMatches.Select(item => item.Title).ToList());
                vm.PaletteQuery = "";
                Assert.Contains(vm.PaletteMatches, item => item.Title == "Locations");
                Assert.Contains(vm.PaletteMatches, item => item.Title == "Blame");
                Assert.Contains(vm.PaletteMatches, item => item.Title == "Changes");
                Assert.Contains(vm.PaletteMatches, item => item.Title == "Command log");
                Assert.Contains(vm.PaletteMatches, item => item.Title == "Save layout");
                Assert.Contains(vm.PaletteMatches, item => item.Title == "Restore layout");
                Assert.Contains(vm.PaletteMatches, item => item.Title == "Git GC");
                Assert.Contains(vm.PaletteMatches, item => item.Title == "Git GC aggressively");
                Assert.Contains(vm.PaletteMatches, item => item.Title == "Delete merged branches");
                Assert.Contains(vm.PaletteMatches, item => item.Title == "Fetch and prune");
                Assert.Contains(vm.PaletteMatches, item => item.Title == "Clean untracked files");

                vm.ClosePalette();
                tab.ShowingWorkingCopy = false;
                vm.TogglePalette();
                Assert.Contains(vm.PaletteMatches, item => item.Title == "Toggle all files");

                vm.ClosePalette();
                tab.ShowingWorkingCopy = true;
                tab.ShowingBlame = true;
                vm.TogglePalette();
                Assert.Contains(vm.PaletteMatches, item => item.Title == "Toggle all files");

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
    public async Task A_palette_tab_command_waits_for_repository_loading()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var left = new TempRepo();
        using var right = new TempRepo();
        left.WriteFile("a.txt", "one\n");
        left.CommitAll("left");
        right.WriteFile("b.txt", "one\n");
        right.CommitAll("right");
        right.WriteFile("new.txt", "untracked\n");
        var directory = Path.Combine(Path.GetTempPath(), "sextant-palette-async-" + Guid.NewGuid().ToString("N"));
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
                    vm.TogglePalette();
                    vm.SelectedPalette = vm.PaletteMatches.Single(item => item.Title == "Next tab");
                    await vm.RunPaletteCommand.ExecuteAsync(null);

                    Assert.False(vm.PaletteOpen);
                    Assert.Same(second, vm.ActiveTab);
                    Assert.True(second.IsReady);
                    Assert.Contains(second.Files, row => row.Path == "new.txt");
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
    public async Task Enter_on_create_branch_keeps_the_prompt_open()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");

        await session.Dispatch(async () =>
        {
            var (vm, tab, window) = await Open(repo);
            try
            {
                await WaitUntilIdle(tab);
                vm.TogglePalette();
                vm.PaletteQuery = "create branch";
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Create branch", vm.SelectedPalette?.Title);
                var box = window.FindControl<TextBox>("PaletteBox");
                Assert.NotNull(box);
                Assert.True(box.Focus());

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                // A repeat of the key that chose the command must not confirm the empty prompt.
                if (window.OwnedWindows.Count == 1)
                    window.OwnedWindows[0].KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Dispatcher.UIThread.RunJobs();

                var dialog = Assert.Single(window.OwnedWindows);
                Assert.Equal("Create branch", dialog.Title);
                Assert.False(vm.PaletteOpen);
                Assert.False(tab.IsBusy, tab.BusyText);
                Assert.DoesNotContain("topic", repo.RunCapture("branch", "--list"), StringComparison.Ordinal);
                var name = Assert.IsType<TextBox>(dialog.FocusManager?.GetFocusedElement());

                window.Activate();
                Dispatcher.UIThread.RunJobs();
                Assert.Same(dialog, Assert.Single(window.OwnedWindows));
                Assert.False(tab.IsBusy, tab.BusyText);

                name.Text = "topic";
                dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.NotNull(vm.RunPaletteCommand.ExecutionTask);
                await vm.RunPaletteCommand.ExecutionTask;
                Dispatcher.UIThread.RunJobs();

                Assert.Empty(window.OwnedWindows);
                Assert.Contains("topic", repo.RunCapture("branch", "--list"), StringComparison.Ordinal);
                Assert.False(tab.HasBanner, tab.Banner);
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
    public async Task Clicking_create_branch_keeps_the_prompt_open()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");

        await session.Dispatch(async () =>
        {
            var (vm, tab, window) = await Open(repo);
            try
            {
                await WaitUntilIdle(tab);
                vm.TogglePalette();
                vm.PaletteQuery = "create branch";
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var list = window.FindControl<ListBox>("PaletteList");
                Assert.NotNull(list);
                var command = vm.PaletteMatches.Single(item => item.Title == "Create branch");
                list.ScrollIntoView(command);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var row = list.ContainerFromItem(command) as ListBoxItem;
                Assert.NotNull(row);
                var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window);
                Assert.NotNull(point);
                window.MouseDown(point.Value, MouseButton.Left);
                window.MouseUp(point.Value, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                var dialog = Assert.Single(window.OwnedWindows);
                Assert.Equal("Create branch", dialog.Title);
                Assert.False(tab.IsBusy, tab.BusyText);

                window.Activate();
                Dispatcher.UIThread.RunJobs();
                Assert.Same(dialog, Assert.Single(window.OwnedWindows));
                Assert.False(tab.IsBusy, tab.BusyText);
                dialog.Close();
                if (vm.RunPaletteCommand.ExecutionTask is { } task)
                    await task;
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

    private static async Task<(MainViewModel Vm, RepositoryViewModel Tab, MainWindow Window)> Open(TempRepo repo)
    {
        var store = new WorkspaceStore(Path.Combine(repo.Directory, ".git", "sextant-workspace"));
        var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings { GitExecutable = repo.Git, ReopenTabs = false }, new GitProcessRunner())
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
        return (vm, tab, window);
    }

    private static async Task FinishCreateBranch(MainViewModel vm, RepositoryViewModel tab)
    {
        if (vm.CreateBranchCommand.ExecutionTask is { } menu)
            await menu;
        if (tab.CreateBranchCommand.ExecutionTask is { } created)
            await created;
        Dispatcher.UIThread.RunJobs();
        await WaitUntilIdle(tab);
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
