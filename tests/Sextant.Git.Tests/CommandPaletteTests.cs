using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

                var command = vm.PaletteMatches.Single(item => item.Title == "Toggle command log");
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
                Assert.Equal("Toggle command log", vm.SelectedPalette?.Title);

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
                        "Toggle blame",
                        "Toggle command log",
                        "Toggle ignore whitespace",
                        "Toggle locations",
                        "Toggle side-by-side diff",
                    ],
                    vm.PaletteMatches.Select(item => item.Title).ToList());

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
}
