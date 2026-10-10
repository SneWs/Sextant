using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.Git;
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
            Assert.True(Left(chip, commit) > Right(subject, commit), $"chip {Left(chip, commit)} subject {Right(subject, commit)}");
            var count = commit.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "RowFileCountChip");
            Assert.True(Right(chip, commit) < Left(count, commit), $"tag right {Right(chip, commit)} count left {Left(count, commit)}");
            Assert.True(Right(count, commit) > commit.Bounds.Width - 20, $"count right {Right(count, commit)} row {commit.Bounds.Width}");
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
