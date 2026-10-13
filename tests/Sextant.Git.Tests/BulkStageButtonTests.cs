using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.Git;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class BulkStageButtonTests
{
    [Fact]
    public async Task Stage_unstage_and_discard_all_sit_on_the_working_copy_diff()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("staged.txt", "staged\n");
        repo.Run("add", "staged.txt");
        repo.WriteFile("unstaged.txt", "unstaged\n");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new BulkHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            Assert.True(vm.ShowingWorkingCopy);
            Assert.True(vm.CanStageAll);
            Assert.True(vm.CanUnstageAll);
            Assert.True(vm.CanDiscardAll);

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var diff = view.GetVisualDescendants().OfType<DiffDocumentView>().Single();
            var changes = view.GetVisualDescendants().OfType<ChangesToolView>().Single();
            var expand = BarButton(diff, "Expand all");
            var collapse = BarButton(diff, "Collapse all");
            var stage = BarButton(diff, "Stage all");
            var unstage = BarButton(diff, "Unstage all");
            var discard = BarButton(diff, "Discard all");
            Assert.True(vm.ShowSectionFolds);
            Assert.True(expand.IsEffectivelyVisible);
            Assert.True(collapse.IsEffectivelyVisible);
            Assert.True(stage.IsEffectivelyVisible);
            Assert.True(unstage.IsEffectivelyVisible);
            Assert.True(discard.IsEffectivelyVisible);
            Assert.True(stage.IsEnabled);
            Assert.True(unstage.IsEnabled);
            Assert.True(discard.IsEnabled);
            Assert.Same(vm.StageAllCommand, stage.Command);
            Assert.Same(vm.UnstageAllCommand, unstage.Command);
            Assert.Same(vm.DiscardAllCommand, discard.Command);
            var bar = expand.FindAncestorOfType<CommandBar>();
            Assert.NotNull(bar);
            Assert.Same(bar, collapse.FindAncestorOfType<CommandBar>());
            Assert.Same(bar, stage.FindAncestorOfType<CommandBar>());
            Assert.Same(bar, unstage.FindAncestorOfType<CommandBar>());
            Assert.Same(bar, discard.FindAncestorOfType<CommandBar>());
            Assert.DoesNotContain(
                changes.GetVisualDescendants().OfType<Button>(),
                button => button is CommandBarButton labeled && labeled.Label is "Stage all" or "Unstage all" or "Discard all");

            var list = diff.FindControl<CopyListBox>("DiffList");
            Assert.NotNull(list);
            Assert.True(Bottom(bar, diff) <= Top(list, diff), $"bar {Bottom(bar, diff)} diff {Top(list, diff)}");
            Assert.True(Math.Abs(Top(stage, bar) - Top(expand, bar)) < 8, "Stage all shares the Expand all row");
            Assert.True(Left(stage, bar) < Left(unstage, bar));
            Assert.True(Right(unstage, bar) <= Left(discard, bar));
            Assert.True(Right(discard, bar) < Left(expand, bar), "Expand all sits to the right of Discard all");
            Assert.True(Right(expand, bar) <= Left(collapse, bar));
            Assert.True(Left(expand, bar) - Right(discard, bar) > 16, "fold buttons stay on the right side of the bar");
            Assert.True(Right(collapse, bar) > bar.Bounds.Width - 24, "Collapse all is the last button");

            vm.ShowingBlame = true;
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.False(stage.IsEffectivelyVisible);

            vm.ShowingBlame = false;
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(stage.IsEffectivelyVisible);

            var commit = vm.Rows.Single(row => row.Subject == "first");
            vm.SelectedGraphRow = commit;
            var until = DateTime.UtcNow.AddSeconds(5);
            while (vm.ShowingWorkingCopy && DateTime.UtcNow < until)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20);
            }

            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.ShowingWorkingCopy);
            Assert.False(stage.IsEffectivelyVisible);
            Assert.False(unstage.IsEffectivelyVisible);
            Assert.False(discard.IsEffectivelyVisible);

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    private static CommandBarButton BarButton(DiffDocumentView diff, string label) =>
        diff.GetVisualDescendants().OfType<CommandBarButton>().Single(button => button.Label == label);

    private static double Top(Control control, Visual relative)
    {
        var origin = control.TranslatePoint(default, relative);
        Assert.NotNull(origin);
        return origin.Value.Y;
    }

    private static double Bottom(Control control, Visual relative) => Top(control, relative) + control.Bounds.Height;

    private static double Left(Control control, Visual relative)
    {
        var origin = control.TranslatePoint(default, relative);
        Assert.NotNull(origin);
        return origin.Value.X;
    }

    private static double Right(Control control, Visual relative) => Left(control, relative) + control.Bounds.Width;

    private sealed class BulkHost(string git) : IWorkspaceHost
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
