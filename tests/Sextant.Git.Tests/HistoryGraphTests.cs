using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Sextant.Controls;
using Sextant.Git;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
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

    [Fact]
    public async Task Escape_in_the_search_box_hides_it_and_clears_the_text()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("alpha");
        repo.WriteFile("b.txt", "two\n");
        repo.CommitAll("beta");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new GraphHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            vm.ShowHistorySearch = true;
            vm.HistoryText = "b.txt";
            await vm.SearchHistoryCommand.ExecuteAsync(null);
            Assert.Contains(vm.Rows, row => row.Subject == "beta");
            Assert.DoesNotContain(vm.Rows, row => row.Subject == "alpha");

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var search = view.FindControl<TextBox>("HistorySearchBox");
            Assert.NotNull(search);
            Assert.True(search.Focus());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(vm.ShowHistorySearch);
            Assert.Equal("", vm.HistoryText);
            var until = DateTime.UtcNow.AddSeconds(5);
            while (vm.IsBusy && DateTime.UtcNow < until)
                await Task.Delay(30);
            Assert.False(vm.IsBusy);
            Assert.False(vm.HasHistoryFilter);
            Assert.False(vm.ShowHistoryChrome);
            Assert.Contains(vm.Rows, row => row.Subject == "alpha");
            Assert.Contains(vm.Rows, row => row.Subject == "beta");
            view.UpdateLayout();
            Assert.False(search.IsEffectivelyVisible);

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task File_history_back_restores_the_graph()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("touch a");
        repo.WriteFile("b.txt", "other\n");
        repo.CommitAll("touch b");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("touch a again");
        repo.WriteFile("a.txt", "three\n");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new GraphHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            var file = vm.Files.First(row => row.Path == "a.txt");
            Assert.Contains(vm.Rows, row => row.Subject == "touch b");

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var back = view.FindControl<Button>("HistoryBackButton");
            Assert.NotNull(back);
            Assert.False(back.IsEffectivelyVisible);
            Assert.False(vm.ShowHistorySearch);

            await ((IAsyncRelayCommand)file.HistoryCommand).ExecuteAsync(null);
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.HasHistoryQuery);
            Assert.Equal("File a.txt", vm.HistoryCaption);
            Assert.True(back.IsEffectivelyVisible);
            Assert.Equal(vm.ShowAllCommitsCommand, back.Command);
            Assert.Contains(vm.Rows, row => row.Subject == "touch a");
            Assert.Contains(vm.Rows, row => row.Subject == "touch a again");
            Assert.DoesNotContain(vm.Rows, row => row.Subject == "touch b");
            var search = view.FindControl<TextBox>("HistorySearchBox");
            Assert.NotNull(search);
            Assert.False(search.IsEffectivelyVisible);

            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await WaitUntilIdle(vm);
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.False(vm.HasHistoryQuery);
            Assert.Equal("", vm.HistoryCaption);
            Assert.False(back.IsEffectivelyVisible);
            Assert.Contains(vm.Rows, row => row.Subject == "touch b");

            await ((IAsyncRelayCommand)file.HistoryCommand).ExecuteAsync(null);
            view.UpdateLayout();
            Assert.True(back.IsEffectivelyVisible);
            var graph = view.FindControl<ListBox>("GraphList");
            Assert.NotNull(graph);
            Assert.True(graph.Focus());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            await WaitUntilIdle(vm);
            view.UpdateLayout();

            Assert.False(vm.ShowHistorySearch);
            Assert.False(vm.HasHistoryQuery);
            Assert.False(back.IsEffectivelyVisible);
            Assert.Contains(vm.Rows, row => row.Subject == "touch b");

            vm.HasHistoryFilter = true;
            vm.HasHistoryQuery = false;
            vm.HistoryCaption = "Hiding side";
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.False(back.IsEffectivelyVisible);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Hiding side" && text.IsEffectivelyVisible);

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    private static async Task WaitUntilIdle(RepositoryViewModel vm)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (vm.IsBusy && DateTime.UtcNow < until)
            await Task.Delay(30);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task History_search_spans_the_window_under_the_branch()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var vm = new RepositoryViewModel(new GraphHost("git"), directory);
                vm.HasBanner = true;
                vm.Banner = "A repository message";
                vm.IsConflicted = true;
                vm.ConflictText = "A merge is in progress";
                vm.ShowHistorySearch = true;
                var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
                var window = new Window { Content = view, Width = 1100, Height = 700 };
                window.Show();
                view.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var search = view.FindControl<TextBox>("HistorySearchBox");
                Assert.NotNull(search);
                Assert.True(search.IsEffectivelyVisible);
                var searchButton = view.FindControl<Button>("HistorySearchButton");
                Assert.NotNull(searchButton);
                Assert.True(searchButton.IsEffectivelyVisible);
                Assert.Null(searchButton.Content as string);
                Assert.NotEmpty(searchButton.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>());
                Assert.True(searchButton.Bounds.Width <= 28);
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), button => button.Content as string is "Search" or "Clear");
                var banner = view.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "A repository message");
                var conflict = view.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "A merge is in progress");
                var pull = view.GetVisualDescendants().OfType<SplitButton>().First(button => button.Content as string == "Pull");
                var locations = view.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "Locations");
                var graph = view.FindControl<ListBox>("GraphList");
                Assert.NotNull(graph);

                var searchTop = Top(search, view);
                var searchBottom = searchTop + search.Bounds.Height;
                Assert.True(searchTop >= Bottom(banner, view) - 1);
                Assert.True(searchTop >= Bottom(conflict, view) - 1);
                Assert.True(searchTop >= Bottom(pull, view) - 1);
                Assert.True(searchBottom <= Top(locations, view) + 1);
                Assert.True(searchBottom <= Top(graph, view) + 1);
                Assert.True(Left(search, view) < Left(graph, view));
                Assert.True(search.Bounds.Width > graph.Bounds.Width + 40, $"search {search.Bounds.Width} graph {graph.Bounds.Width}");

                window.Close();
                await vm.DisposeAsync();
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static double Top(Control control, Visual relative)
    {
        var origin = control.TranslatePoint(default, relative);
        Assert.NotNull(origin);
        return origin.Value.Y;
    }

    private static double Left(Control control, Visual relative)
    {
        var origin = control.TranslatePoint(default, relative);
        Assert.NotNull(origin);
        return origin.Value.X;
    }

    private static double Bottom(Control control, Visual relative) => Top(control, relative) + control.Bounds.Height;

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
