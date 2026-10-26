using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.Git;
using Sextant.Git.Models;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class TagHistoryTests
{
    [Fact]
    public async Task Tag_labels_sit_on_the_right_and_a_location_click_selects_that_commit()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var first = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("tag", "v1");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("second");
        var second = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("tag", "-a", "v2", "-m", "note");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new TagHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();

            var older = vm.Rows.Single(row => row.Subject == "first");
            var newer = vm.Rows.Single(row => row.Subject == "second");
            Assert.Equal(["v1"], older.Tags);
            Assert.Equal(["v2"], newer.Tags);
            Assert.Equal(first, older.Sha, ignoreCase: true);
            Assert.Equal(second, newer.Sha, ignoreCase: true);
            Assert.DoesNotContain("v1", older.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("v2", newer.Detail, StringComparison.Ordinal);
            var v1 = vm.Locations.Single(item => item.ShowTag && item.Label == "v1");
            var v2 = vm.Locations.Single(item => item.ShowTag && item.Label == "v2");
            Assert.Equal(first, v1.Oid, ignoreCase: true);
            Assert.Equal(second, v2.Oid, ignoreCase: true);

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            for (var attempt = 0; attempt < 100 && (older.FileCountText.Length == 0 || newer.FileCountText.Length == 0); attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20);
            }
            Assert.Equal("1", older.FileCountText);
            Assert.Equal("1", newer.FileCountText);
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var graph = view.FindControl<ListBox>("GraphList");
            Assert.NotNull(graph);
            graph.ScrollIntoView(newer);
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var commit = graph.ContainerFromItem(newer) as ListBoxItem;
            Assert.NotNull(commit);
            var chip = commit.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("taglabel") && border.IsEffectivelyVisible);
            var chipText = chip.GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal("v2", chipText.Text);
            Assert.Equal(FontWeight.SemiBold, chipText.FontWeight);
            Assert.True(chip.CornerRadius.TopLeft > 0);
            Assert.True(chip.BorderThickness.Left > 0);
            Assert.False(IsClear(chip.Background));
            var subject = commit.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "second");
            var textColumn = subject.Parent as Control;
            Assert.NotNull(textColumn);
            Assert.True(Right(textColumn, commit) > Left(chip, commit), $"text {Right(textColumn, commit)} chip {Left(chip, commit)}");
            var count = commit.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "RowFileCountChip");
            Assert.True(Right(chip, commit) < Left(count, commit), $"tag right {Right(chip, commit)} count left {Left(count, commit)}");
            var scroll = graph.GetVisualDescendants().OfType<ScrollViewer>().First();
            AssertNearViewportRight(count, scroll);
            var refs = commit.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("reflabel")).ToList();
            Assert.NotEmpty(refs);
            Assert.True(Right(refs[^1], commit) < Left(chip, commit), "Branch labels should precede the tag and file count.");
            var olderCommit = graph.ContainerFromItem(older) as ListBoxItem;
            Assert.NotNull(olderCommit);
            var olderCount = olderCommit.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "RowFileCountChip");
            Assert.InRange(Math.Abs(Right(count, commit) - Right(olderCount, olderCommit)), 0, 1);
            Assert.DoesNotContain(
                commit.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text?.Contains("v2", StringComparison.Ordinal) == true && text != chipText);

            var locations = view.FindControl<ListBox>("LocationList");
            Assert.NotNull(locations);
            locations.ScrollIntoView(v1);
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Click(window, locations, v1);
            Assert.Equal(older.Sha, vm.SelectedGraphRow?.Sha, ignoreCase: true);

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Branch_tags_and_file_count_stay_on_the_visible_right()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("alpha");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new TagHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            vm.GraphWidth = 240;
            var wide = new GraphRowViewModel
            {
                ShowLanes = true,
                DrawLanes = true,
                Subject = new string('s', 80),
                Detail = "detail",
                Refs = ["release/this-branch-name-is-intentionally-long"],
                Tags = ["v9"],
                FileCountText = "12",
                Lanes = new LaneGeometry
                {
                    NodeLane = 39,
                    LaneCount = 40,
                    IncomingLanes = [],
                    ThroughLanes = [],
                    Edges = [],
                },
            };
            var other = new GraphRowViewModel
            {
                ShowLanes = true,
                DrawLanes = true,
                Subject = "short",
                Refs = ["main"],
                Tags = ["v1"],
                FileCountText = "1",
                Lanes = new LaneGeometry
                {
                    NodeLane = 0,
                    LaneCount = 1,
                    IncomingLanes = [],
                    ThroughLanes = [],
                    Edges = [],
                },
            };
            vm.Rows.Insert(0, wide);
            vm.Rows.Insert(1, other);

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var list = view.FindControl<ListBox>("GraphList");
            Assert.NotNull(list);
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width + 40, $"extent {scroll.Extent.Width} viewport {scroll.Viewport.Width}");
            var wideItem = list.ContainerFromItem(wide) as ListBoxItem;
            var otherItem = list.ContainerFromItem(other) as ListBoxItem;
            Assert.NotNull(wideItem);
            Assert.NotNull(otherItem);

            var wideCount = Chip(wideItem, "RowFileCountChip");
            var otherCount = Chip(otherItem, "RowFileCountChip");
            AssertNearViewportRight(wideCount, scroll);
            AssertNearViewportRight(otherCount, scroll);
            Assert.InRange(Math.Abs(Right(wideCount, scroll) - Right(otherCount, scroll)), 0, 1);
            var branch = wideItem.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("reflabel"));
            var tag = wideItem.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("taglabel"));
            Assert.True(Right(branch, wideItem) < Left(tag, wideItem));
            Assert.True(Right(tag, wideItem) < Left(wideCount, wideItem));
            Assert.True(Left(branch, scroll) < 0, $"branch left {Left(branch, scroll)}");
            var subject = wideItem.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == wide.Subject);
            var textColumn = subject.Parent as Control;
            Assert.NotNull(textColumn);
            Assert.True(Right(textColumn, wideItem) > Left(tag, wideItem), $"text {Right(textColumn, wideItem)} tag {Left(tag, wideItem)}");

            var before = Right(wideCount, scroll);
            scroll.Offset = new Vector(180, scroll.Offset.Y);
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            view.UpdateLayout();
            Assert.True(scroll.Offset.X > 100, $"offset {scroll.Offset.X}");
            AssertNearViewportRight(wideCount, scroll);
            Assert.InRange(Math.Abs(Right(wideCount, scroll) - before), 0, 2);
            Assert.True(Right(textColumn, wideItem) > Left(tag, wideItem));

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    private static Border Chip(ListBoxItem row, string name) =>
        row.GetVisualDescendants().OfType<Border>().Single(border => border.Name == name);

    private static void AssertNearViewportRight(Control control, ScrollViewer scroll)
    {
        var gap = scroll.Viewport.Width - Right(control, scroll);
        Assert.InRange(gap, -1, 28);
    }

    private static void Click(Window window, ListBox list, object item)
    {
        var row = list.ContainerFromItem(item) as ListBoxItem;
        Assert.NotNull(row);
        var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static double Left(Control control, Visual relative)
    {
        var origin = control.TranslatePoint(default, relative);
        Assert.NotNull(origin);
        return origin.Value.X;
    }

    private static double Right(Control control, Visual relative) => Left(control, relative) + control.Bounds.Width;

    private static bool IsClear(IBrush? brush) =>
        brush is null || brush is ISolidColorBrush solid && solid.Color.A == 0;

    private sealed class TagHost(string git) : IWorkspaceHost
    {
        public IDialogService? Dialogs => null;

        public GitProcessRunner Runner { get; } = new();

        public string? GitExecutable { get; } = git;

        public string? MergeTool => null;

        public bool GitReady => true;

        public Task Activate(RepositoryViewModel tab) => Task.CompletedTask;

        public Task Close(RepositoryViewModel tab) => Task.CompletedTask;

        public void NoteLoaded(RepositoryViewModel tab)
        {
        }

        public void Save()
        {
        }

        public Task OpenRepositoryAsync(string path) => Task.CompletedTask;
    }
}
