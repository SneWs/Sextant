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
            await session.Dispatch(() =>
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

                vm.Close(tab);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Assert.False(left.IsVisible);
                Assert.False(right.IsVisible);
                Assert.Equal(0, left.Width);
                Assert.Equal(0, right.Width);

                vm.Shutdown();
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

                vm.Activate(first);
                await first.EnsureLoadedAsync();
                await WaitUntilIdle(first);
                vm.Activate(second);
                await second.EnsureLoadedAsync();
                await WaitUntilIdle(second);

                left.WriteFile("fresh.txt", "new\n");
                Assert.DoesNotContain(first.Files, row => row.Path == "fresh.txt");

                vm.Activate(first);
                await WaitUntilIdle(first);
                Assert.Contains(first.Files, row => row.Path == "fresh.txt");

                vm.Shutdown();
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task WaitUntilIdle(RepositoryViewModel tab)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (tab.IsBusy && DateTime.UtcNow < until)
            await Task.Delay(30);
        Assert.False(tab.IsBusy);
    }
}
