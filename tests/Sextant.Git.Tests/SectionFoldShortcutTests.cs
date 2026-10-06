using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant;
using Sextant.Git;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class SectionFoldShortcutTests
{
    [Fact]
    public async Task Expand_and_collapse_all_follow_the_keyboard()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.WriteFile("b.txt", "two\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "one changed\n");
        repo.WriteFile("b.txt", "two changed\n");

        // Returning a value makes Dispatch wait for the async body. A bare async lambda does not.
        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new FoldHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            Assert.True(vm.ShowSectionFolds);
            Assert.True(FoldCount(vm) >= 2, "files " + string.Join(", ", vm.DiffRows.OfType<DiffFileRow>().Select(row => row.Path)));

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var expand = Button(view, "Expand all");
            var collapse = Button(view, "Collapse all");
            Assert.True(expand.IsEffectivelyVisible);
            Assert.True(collapse.IsEffectivelyVisible);
            Assert.Equal(AppGestures.CommandKey(Key.E, KeyModifiers.Shift), expand.HotKey);
            Assert.Equal(AppGestures.CommandKey(Key.C, KeyModifiers.Shift), collapse.HotKey);
            Assert.True(vm.ExpandAllSectionsCommand.CanExecute(null));
            Assert.True(vm.CollapseAllSectionsCommand.CanExecute(null));

            Press(window, expand.HotKey!);
            Assert.Equal(FoldCount(vm), OpenCount(vm));
            Assert.Contains(vm.DiffRows, row => row is DiffEditorRow);

            Press(window, collapse.HotKey!);
            Assert.Equal(0, OpenCount(vm));
            Assert.DoesNotContain(vm.DiffRows, row => row is DiffEditorRow);

            window.KeyPress(Key.E, CommandOnly(), PhysicalKey.E, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, OpenCount(vm));

            vm.AllFiles = false;
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.ShowSectionFolds);
            Assert.False(expand.IsEffectivelyVisible);
            Assert.False(vm.ExpandAllSectionsCommand.CanExecute(null));
            Press(window, expand.HotKey!);
            Assert.Equal(0, OpenCount(vm));

            vm.AllFiles = true;
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.ShowSectionFolds);
            Assert.True(expand.IsEffectivelyVisible);
            Press(window, expand.HotKey!);
            Assert.Equal(FoldCount(vm), OpenCount(vm));

            window.Close();
            await vm.DisposeAsync();
            return 0;
        }, CancellationToken.None);
    }

    private static int FoldCount(RepositoryViewModel vm) =>
        vm.DiffRows.OfType<DiffFileRow>().Count(row => row.CanFold);

    private static int OpenCount(RepositoryViewModel vm) =>
        vm.DiffRows.OfType<DiffFileRow>().Count(row => row.CanFold && row.Expanded);

    private static CommandBarButton Button(RepositoryView view, string label) =>
        view.GetVisualDescendants().OfType<CommandBarButton>().Single(button => button.Label == label);

    private static RawInputModifiers CommandOnly() =>
        AppGestures.Command == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Press(Window window, KeyGesture gesture)
    {
        var raw = RawInputModifiers.None;
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt))
            raw |= RawInputModifiers.Alt;
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
            raw |= RawInputModifiers.Control;
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift))
            raw |= RawInputModifiers.Shift;
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta))
            raw |= RawInputModifiers.Meta;
        var physical = gesture.Key == Key.C ? PhysicalKey.C : PhysicalKey.E;
        window.KeyPress(gesture.Key, raw, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class FoldHost(string git) : IWorkspaceHost
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
