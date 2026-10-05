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
}
