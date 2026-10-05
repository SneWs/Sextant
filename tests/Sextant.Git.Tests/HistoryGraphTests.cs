using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Sextant.Controls;
using Sextant.Git;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

public class HistoryGraphTests
{
    [Fact]
    public async Task Search_drops_lanes_and_a_wide_graph_scrolls_sideways()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("alpha");
        repo.WriteFile("b.txt", "two\n");
        repo.CommitAll("beta");
        var branch = repo.CurrentBranch();

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new GraphHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            Assert.Contains(vm.Rows, row => row.DrawLanes && row.Subject == "beta");

            vm.Rows.Insert(1, new GraphRowViewModel
            {
                ShowLanes = true,
                DrawLanes = true,
                Subject = "wide-lane",
                Detail = "many branches",
                Lanes = new LaneGeometry
                {
                    NodeLane = 60,
                    LaneCount = 61,
                    IncomingLanes = [],
                    ThroughLanes = [],
                    Edges = [],
                },
            });

            vm.GraphWidth = 320;
            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var list = view.FindControl<ListBox>("GraphList");
            Assert.NotNull(list);
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width + 40, $"extent {scroll.Extent.Width} viewport {scroll.Viewport.Width}");
            var wide = list.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "wide-lane");
            var widePoint = wide.TranslatePoint(default, list);
            Assert.NotNull(widePoint);
            Assert.True(widePoint.Value.X > 400, $"wide row starts at {widePoint.Value.X}");

            vm.HistoryText = "b.txt";
            await vm.SearchHistoryCommand.ExecuteAsync(null);
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(vm.Rows, row => row.DrawLanes);
            Assert.Contains(vm.Rows, row => row.Subject == "beta");
            Assert.DoesNotContain(vm.Rows, row => row.Subject == "alpha");
            Assert.DoesNotContain(list.GetVisualDescendants().OfType<LaneCanvas>(), canvas => canvas.IsVisible);
            var beta = list.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "beta");
            var betaPoint = beta.TranslatePoint(default, list);
            Assert.NotNull(betaPoint);
            Assert.True(betaPoint.Value.X < 30, $"search row starts at {betaPoint.Value.X}");
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1, $"search extent {scroll.Extent.Width} viewport {scroll.Viewport.Width}");

            vm.HistoryText = "branch:" + branch;
            await vm.SearchHistoryCommand.ExecuteAsync(null);
            Assert.Contains(vm.Rows, row => row.DrawLanes);

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    private sealed class GraphHost(string git) : IWorkspaceHost
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
