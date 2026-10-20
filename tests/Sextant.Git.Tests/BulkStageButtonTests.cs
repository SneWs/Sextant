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
                button => button is CommandBarButton labeled && labeled.Label is "Stage all" or "Unstage all" or "Discard all"
                    or "Stage selected" or "Unstage selected" or "Stash selected");
            Assert.DoesNotContain(
                diff.GetVisualDescendants().OfType<CommandBarButton>(),
                button => button.Label is "Stage selected" or "Unstage selected" or "Stash selected");

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

            var staging = view.GetVisualDescendants().OfType<StagingToolView>().Single();
            var stagingStage = BarButton(staging, "Stage all");
            var stagingUnstage = BarButton(staging, "Unstage all");
            var stagingDiscard = BarButton(staging, "Discard all");
            Assert.DoesNotContain(
                staging.GetVisualDescendants().OfType<CommandBarButton>(),
                button => button.Label is "Expand all" or "Collapse all");
            Assert.True(stagingStage.IsEffectivelyVisible);
            Assert.True(stagingUnstage.IsEffectivelyVisible);
            Assert.True(stagingDiscard.IsEffectivelyVisible);
            Assert.True(stagingStage.IsEnabled);
            Assert.True(stagingUnstage.IsEnabled);
            Assert.True(stagingDiscard.IsEnabled);
            Assert.Same(vm.StageAllCommand, stagingStage.Command);
            Assert.Same(vm.UnstageAllCommand, stagingUnstage.Command);
            Assert.Same(vm.DiscardAllCommand, stagingDiscard.Command);
            var stagingStageSelected = BarButton(staging, "Stage selected");
            var stagingUnstageSelected = BarButton(staging, "Unstage selected");
            var stagingStashSelected = BarButton(staging, "Stash selected");
            Assert.True(stagingStageSelected.IsEffectivelyVisible);
            Assert.True(stagingUnstageSelected.IsEffectivelyVisible);
            Assert.True(stagingStashSelected.IsEffectivelyVisible);
            Assert.False(stagingStageSelected.IsEnabled);
            Assert.True(stagingUnstageSelected.IsEnabled);
            Assert.True(stagingStashSelected.IsEnabled);
            Assert.False(vm.CanStageSelected);
            Assert.True(vm.CanUnstageSelected);
            Assert.True(vm.CanStashSelected);
            Assert.Same(vm.StageSelectedCommand, stagingStageSelected.Command);
            Assert.Same(vm.UnstageSelectedCommand, stagingUnstageSelected.Command);
            Assert.Same(vm.StashSelectedCommand, stagingStashSelected.Command);
            var stagingBar = staging.FindControl<CommandBar>("StagingCommandBar");
            Assert.NotNull(stagingBar);
            Assert.NotSame(bar, stagingBar);
            Assert.Same(stagingBar, stagingStage.FindAncestorOfType<CommandBar>());
            Assert.Same(stagingBar, stagingUnstage.FindAncestorOfType<CommandBar>());
            Assert.Same(stagingBar, stagingDiscard.FindAncestorOfType<CommandBar>());
            Assert.Same(stagingBar, stagingStashSelected.FindAncestorOfType<CommandBar>());
            var files = staging.FindControl<ListBox>("FileList");
            Assert.NotNull(files);
            Assert.Equal(SelectionMode.Multiple, files.SelectionMode);
            Assert.True(Bottom(stagingBar, staging) <= Top(files, staging), "staging bar sits above the file list");
            Assert.True(Math.Abs(Top(stagingStage, stagingBar) - Top(stagingDiscard, stagingBar)) < 8);
            Assert.True(Math.Abs(Top(stagingStage, stagingBar) - Top(stagingStashSelected, stagingBar)) < 8);
            Assert.True(Left(stagingStage, stagingBar) < Left(stagingUnstage, stagingBar));
            Assert.True(Right(stagingUnstage, stagingBar) <= Left(stagingDiscard, stagingBar));
            Assert.True(Right(stagingDiscard, stagingBar) <= Left(stagingStageSelected, stagingBar));
            Assert.True(Right(stagingStageSelected, stagingBar) <= Left(stagingUnstageSelected, stagingBar));
            Assert.True(Right(stagingUnstageSelected, stagingBar) <= Left(stagingStashSelected, stagingBar));
            Assert.True(Left(stagingStage, stagingBar) < 40, "Stage all starts on the left of the staging bar");
            Assert.True(Right(stagingStashSelected, stagingBar) < stagingBar.Bounds.Width - 8, "selected actions stay on the Changes bar");

            vm.ShowingBlame = true;
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.False(stage.IsEffectivelyVisible);
            Assert.False(stagingStage.IsEffectivelyVisible);

            vm.ShowingBlame = false;
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(stage.IsEffectivelyVisible);
            Assert.True(stagingStage.IsEffectivelyVisible);

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
            Assert.False(stagingStage.IsEffectivelyVisible);
            Assert.False(stagingUnstage.IsEffectivelyVisible);
            Assert.False(stagingDiscard.IsEffectivelyVisible);
            Assert.False(BarButton(staging, "Stage selected").IsEffectivelyVisible);
            Assert.False(BarButton(staging, "Unstage selected").IsEffectivelyVisible);
            Assert.False(BarButton(staging, "Stash selected").IsEffectivelyVisible);

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Stage_unstage_and_discard_all_hide_when_the_staging_list_is_empty()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new BulkHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            Assert.True(vm.ShowingWorkingCopy);
            Assert.False(vm.IsDirty);
            Assert.False(vm.ShowBulkActions);
            Assert.DoesNotContain(vm.Files, row => !row.IsHeader);

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var diff = view.GetVisualDescendants().OfType<DiffDocumentView>().Single();
            var staging = view.GetVisualDescendants().OfType<StagingToolView>().Single();
            var stage = BarButton(diff, "Stage all");
            var unstage = BarButton(diff, "Unstage all");
            var discard = BarButton(diff, "Discard all");
            var stagingStage = BarButton(staging, "Stage all");
            var stagingUnstage = BarButton(staging, "Unstage all");
            var stagingDiscard = BarButton(staging, "Discard all");
            var stagingStageSelected = BarButton(staging, "Stage selected");
            var stagingUnstageSelected = BarButton(staging, "Unstage selected");
            var stagingStashSelected = BarButton(staging, "Stash selected");
            Assert.False(stage.IsVisible);
            Assert.False(unstage.IsVisible);
            Assert.False(discard.IsVisible);
            Assert.False(stagingStage.IsVisible);
            Assert.False(stagingUnstage.IsVisible);
            Assert.False(stagingDiscard.IsVisible);
            Assert.False(stagingStageSelected.IsVisible);
            Assert.False(stagingUnstageSelected.IsVisible);
            Assert.False(stagingStashSelected.IsVisible);

            repo.WriteFile("unstaged.txt", "unstaged\n");
            await vm.Refresh();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.ShowBulkActions);
            Assert.Contains(vm.Files, row => !row.IsHeader && row.Path == "unstaged.txt");
            Assert.True(stage.IsEffectivelyVisible);
            Assert.True(unstage.IsEffectivelyVisible);
            Assert.True(discard.IsEffectivelyVisible);
            Assert.True(stage.IsEnabled);
            Assert.False(unstage.IsEnabled);
            Assert.True(discard.IsEnabled);
            Assert.True(stagingStage.IsEffectivelyVisible);
            Assert.True(stagingUnstage.IsEffectivelyVisible);
            Assert.True(stagingDiscard.IsEffectivelyVisible);
            Assert.True(stagingStage.IsEnabled);
            Assert.False(stagingUnstage.IsEnabled);
            Assert.True(stagingDiscard.IsEnabled);
            Assert.True(stagingStageSelected.IsEffectivelyVisible);
            Assert.True(stagingUnstageSelected.IsEffectivelyVisible);
            Assert.True(stagingStashSelected.IsEffectivelyVisible);
            Assert.True(stagingStageSelected.IsEnabled);
            Assert.False(stagingUnstageSelected.IsEnabled);
            Assert.True(stagingStashSelected.IsEnabled);
            Assert.True(vm.CanStageSelected);
            Assert.False(vm.CanUnstageSelected);
            Assert.True(vm.CanStashSelected);

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    private static CommandBarButton BarButton(Visual root, string label) =>
        root.GetVisualDescendants().OfType<CommandBarButton>().Single(button => button.Label == label);

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
