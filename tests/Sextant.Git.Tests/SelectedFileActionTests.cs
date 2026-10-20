using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant;
using Sextant.Git;
using Sextant.Git.Models;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class SelectedFileActionTests
{
    [Fact]
    public async Task Selecting_files_stages_and_stashes_only_those()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.WriteFile("b.txt", "one\n");
        repo.WriteFile("c.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "A\n");
        repo.WriteFile("b.txt", "B\n");
        repo.WriteFile("c.txt", "C\n");
        var dialogs = new SelectedDialogs("picked");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new SelectedHost(repo.Git, dialogs), repo.Directory);
            await vm.EnsureLoadedAsync();
            var view = new RepositoryView { DataContext = vm, Width = 1600, Height = 800 };
            var window = new Window { Content = view, Width = 1600, Height = 800 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var staging = view.GetVisualDescendants().OfType<StagingToolView>().Single();
            var list = staging.FindControl<ListBox>("FileList");
            Assert.NotNull(list);
            Assert.Equal(SelectionMode.Multiple, list.SelectionMode);
            var hotkeys = Application.Current?.PlatformSettings?.HotkeyConfiguration;
            Assert.NotNull(hotkeys);

            var a = Row(vm, "a.txt");
            var b = Row(vm, "b.txt");
            var c = Row(vm, "c.txt");
            var header = vm.Files.Single(row => row.IsHeader && row.Label == "Unstaged");
            Click(window, list, header, RawInputModifiers.None);
            Assert.Same(a, vm.SelectedFile);
            Assert.Equal(["a.txt"], SelectedPaths(list));

            Click(window, list, a, RawInputModifiers.None);
            Click(window, list, c, ToRaw(hotkeys.SelectionModifiers));
            Assert.Equal(["a.txt", "b.txt", "c.txt"], SelectedPaths(list));

            Click(window, list, b, RawInputModifiers.None);
            Assert.Equal(["b.txt"], SelectedPaths(list));
            Click(window, list, c, ToRaw(hotkeys.CommandModifiers));
            Assert.Equal(["b.txt", "c.txt"], SelectedPaths(list));
            Assert.True(vm.CanStageSelected);
            Assert.False(vm.CanUnstageSelected);
            Assert.True(vm.CanStashSelected);

            var stage = BarButton(staging, "Stage selected");
            Assert.True(stage.IsEnabled);
            Click(window, stage);
            if (vm.StageSelectedCommand.ExecutionTask is { } stagingTask)
                await stagingTask;
            await Until(() => !vm.IsBusy
                && vm.Files.Any(row => row.Path == "b.txt" && row.FromStagedList)
                && vm.Files.Any(row => row.Path == "c.txt" && row.FromStagedList)
                && vm.Files.Any(row => row.Path == "a.txt" && !row.FromStagedList));
            Assert.Contains(vm.Files, row => row.Path == "a.txt" && !row.FromStagedList);
            Assert.Contains(vm.Files, row => row.Path == "b.txt" && row.FromStagedList);
            Assert.Contains(vm.Files, row => row.Path == "c.txt" && row.FromStagedList);
            Assert.False(vm.HasBanner, vm.Banner);

            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Click(window, list, Row(vm, "b.txt", staged: true), RawInputModifiers.None);
            Click(window, list, Row(vm, "c.txt", staged: true), ToRaw(hotkeys.CommandModifiers));
            Assert.Equal(["b.txt", "c.txt"], SelectedPaths(list));
            Assert.True(vm.CanStashSelected);
            var stash = BarButton(staging, "Stash selected");
            Assert.True(stash.IsEnabled);
            Click(window, stash);
            if (vm.StashSelectedCommand.ExecutionTask is { } stashTask)
                await stashTask;
            await Until(() => !vm.IsBusy && vm.Files.All(row => row.Path is not ("b.txt" or "c.txt")));
            Assert.Equal(1, dialogs.Prompts);
            Assert.Contains(vm.Files, row => row.Path == "a.txt" && !row.IsHeader);
            Assert.DoesNotContain(vm.Files, row => row.Path is "b.txt" or "c.txt");
            Assert.False(vm.HasBanner, vm.Banner);
            Assert.Equal("A\n", System.IO.File.ReadAllText(Path.Combine(repo.Directory, "a.txt")));
            Assert.Equal("one\n", System.IO.File.ReadAllText(Path.Combine(repo.Directory, "b.txt")));
            Assert.Equal("one\n", System.IO.File.ReadAllText(Path.Combine(repo.Directory, "c.txt")));
            Assert.Contains("picked", repo.RunCapture("stash", "list"), StringComparison.Ordinal);

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    private static FileRowViewModel Row(RepositoryViewModel vm, string path, bool staged = false) =>
        vm.Files.Single(row => row.Path == path && row.FromStagedList == staged);

    private static string[] SelectedPaths(ListBox list) =>
        list.SelectedItems?.OfType<FileRowViewModel>().Where(row => !row.IsHeader).Select(row => row.Path).Order(StringComparer.Ordinal).ToArray()
        ?? [];

    private static CommandBarButton BarButton(Visual root, string label) =>
        root.GetVisualDescendants().OfType<CommandBarButton>().Single(button => button.Label == label);

    private static async Task Until(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (!done() && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    private static void Click(Window window, ListBox list, object item, RawInputModifiers modifiers)
    {
        list.ScrollIntoView(item);
        Dispatcher.UIThread.RunJobs();
        var row = list.ContainerFromItem(item) as ListBoxItem;
        Assert.NotNull(row);
        var point = row.TranslatePoint(new Point(36, row.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseDown(point.Value, MouseButton.Left, modifiers);
        window.MouseUp(point.Value, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static RawInputModifiers ToRaw(KeyModifiers modifiers)
    {
        var raw = RawInputModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Alt))
            raw |= RawInputModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Control))
            raw |= RawInputModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            raw |= RawInputModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Meta))
            raw |= RawInputModifiers.Meta;
        return raw;
    }

    private sealed class SelectedDialogs(string message) : IDialogService
    {
        public int Prompts { get; private set; }

        public Task<string?> PromptAsync(string title, string prompt, string initial = "", bool allowEmpty = false)
        {
            Prompts++;
            return Task.FromResult<string?>(message);
        }

        public Task<bool> ConfirmAsync(string title, string prompt, string confirm = "OK") => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string title, string? startDirectory = null) => Task.FromResult<string?>(null);
        public Task<string?> PickGitExecutableAsync() => Task.FromResult<string?>(null);
        public Task<string?> PromptSecretAsync(string title, string prompt) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string suggestedName) => Task.FromResult<string?>(null);
        public Task<string?> PickFileAsync(string title, string typeName, IReadOnlyList<string> patterns) => Task.FromResult<string?>(null);
        public Task<CloneRequest?> PromptCloneAsync() => Task.FromResult<CloneRequest?>(null);
        public Task<string?> PickAsync(string title, string prompt, IReadOnlyList<string> options) => Task.FromResult<string?>(null);
        public Task<PerformanceChoice?> ConfirmPerformanceAsync(PerformanceSuggestion suggestion) => Task.FromResult<PerformanceChoice?>(null);
        public Task<IReadOnlyList<RebaseStep>?> EditRebaseAsync(IReadOnlyList<RebaseStep> steps) => Task.FromResult<IReadOnlyList<RebaseStep>?>(null);
        public Task<SettingsDraft?> EditSettingsAsync(SettingsDraft current) => Task.FromResult<SettingsDraft?>(null);
        public Task ShowAboutAsync() => Task.CompletedTask;
        public Task CopyAsync(string text) => Task.CompletedTask;
    }

    private sealed class SelectedHost(string git, IDialogService dialogs) : IWorkspaceHost
    {
        public IDialogService? Dialogs { get; } = dialogs;

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
