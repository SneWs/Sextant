using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;
using Sextant.Git.Models;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class FileListTests
{
    [Fact]
    public async Task Staged_files_sit_above_unstaged_and_the_section_is_a_bar()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.WriteFile("unstaged.txt", "base\n");
        repo.CommitAll("first");
        repo.WriteFile("staged.txt", "staged\n");
        repo.Run("add", "staged.txt");
        repo.WriteFile("unstaged.txt", "unstaged\n");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new FileListHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();

            Assert.Equal(
                ["Staged", "staged.txt", "Unstaged", "unstaged.txt"],
                vm.Files.Select(row => row.IsHeader ? row.Label : row.Path).ToArray());
            var stagedFile = vm.Files.First(row => row.Path == "staged.txt");
            Assert.Same(stagedFile, vm.SelectedFile);
            vm.SelectedFile = vm.Files.First(row => row.IsHeader && row.Label == "Staged");
            Assert.Same(stagedFile, vm.SelectedFile);

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var list = view.FindControl<ListBox>("FileList");
            Assert.NotNull(list);
            var heads = list.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("filehead") && border.IsEffectivelyVisible)
                .OrderBy(border => Top(border, list))
                .ToList();
            Assert.Equal(2, heads.Count);
            var stagedHead = heads[0];
            var unstagedHead = heads[1];
            Assert.Equal("Staged", HeadText(stagedHead));
            Assert.Equal("Unstaged", HeadText(unstagedHead));

            var stagedText = Text(list, "staged.txt");
            var unstagedText = Text(list, "unstaged.txt");
            var stagedTop = Top(stagedHead, list);
            var stagedFileTop = Top(stagedText, list);
            var unstagedTop = Top(unstagedHead, list);
            var unstagedFileTop = Top(unstagedText, list);
            Assert.True(stagedTop < stagedFileTop, $"staged header {stagedTop} file {stagedFileTop}");
            Assert.True(stagedFileTop < unstagedTop, $"staged file {stagedFileTop} unstaged header {unstagedTop}");
            Assert.True(unstagedTop < unstagedFileTop, $"unstaged header {unstagedTop} file {unstagedFileTop}");

            Assert.True(stagedHead.Bounds.Width > stagedText.Bounds.Width + 40);
            Assert.Equal(new CornerRadius(0), stagedHead.CornerRadius);
            Assert.Equal(new Thickness(4, 0, 0, 0), stagedHead.BorderThickness);
            Assert.Equal(40, stagedHead.MinHeight);
            Assert.False(IsClear(stagedHead.Background));
            Assert.Equal(FontWeight.Bold, HeadLabel(stagedHead).FontWeight);
            Assert.True(HeadLabel(stagedHead).FontSize >= 15);
            Assert.True(HeadLabel(stagedHead).Opacity > 0.9);

            var fileBorder = stagedText.FindAncestorOfType<Border>();
            Assert.NotNull(fileBorder);
            Assert.True(stagedHead.Bounds.Height > fileBorder.Bounds.Height, "the section bar is taller than a file row");
            Assert.Equal(["Unstage all"], VisibleButtons(stagedHead));
            Assert.Equal(["Discard all", "Stage all"], VisibleButtons(unstagedHead));
            var unstageAll = VisibleButton(stagedHead, "Unstage all");
            var stageAll = VisibleButton(unstagedHead, "Stage all");
            var discardAll = VisibleButton(unstagedHead, "Discard all");
            Assert.Same(vm.UnstageAllCommand, unstageAll.Command);
            Assert.Same(vm.StageAllCommand, stageAll.Command);
            Assert.Same(vm.Files.Single(row => row.IsHeader && row.Label == "Unstaged").DiscardAllCommand, discardAll.Command);
            Assert.True(unstageAll.Bounds.Height > 0 && unstageAll.Bounds.Height <= stagedHead.Bounds.Height);
            Assert.True(stageAll.Bounds.Height > 0 && stageAll.Bounds.Height <= unstagedHead.Bounds.Height);
            Assert.True(discardAll.Bounds.Height > 0 && discardAll.Bounds.Height <= unstagedHead.Bounds.Height);
            Assert.DoesNotContain("filehead", fileBorder.Classes);
            Assert.True(IsClear(fileBorder.Background));
            Assert.Equal(new Thickness(0), fileBorder.BorderThickness);
            Assert.NotEqual(FontWeight.SemiBold, stagedText.FontWeight);

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Untracked_files_are_their_own_section_with_add_and_delete()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("unstaged.txt", "base\n");
        repo.CommitAll("first");
        repo.WriteFile("staged.txt", "staged\n");
        repo.Run("add", "staged.txt");
        repo.WriteFile("unstaged.txt", "changed\n");
        repo.WriteFile("new.txt", "new\n");
        repo.WriteFile("extra.txt", "extra\n");
        repo.WriteFile("notes/readme.txt", "inside\n");
        var dialogs = new RecordingDialogs();

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new FileListHost(repo.Git, dialogs), repo.Directory);
            Window? window = null;
            try
            {
                await vm.EnsureLoadedAsync();
                Assert.Equal(
                    ["Staged", "staged.txt", "Unstaged", "unstaged.txt", "Untracked", "extra.txt", "new.txt", "notes/"],
                    Names(vm));
                Assert.Equal("1 staged, 1 unstaged, 3 untracked", vm.Rows.Single(row => row.IsWorkingCopy).Detail);

                var stagedFile = vm.Files.Single(row => row.Path == "staged.txt");
                var unstagedFile = vm.Files.Single(row => row.Path == "unstaged.txt");
                var extra = vm.Files.Single(row => row.Path == "extra.txt");
                var added = vm.Files.Single(row => row.Path == "new.txt");
                var header = vm.Files.Single(row => row.IsHeader && row.Label == "Untracked");
                Assert.Equal("Stage", stagedFile.StageLabel);
                Assert.Equal("Discard", stagedFile.DiscardLabel);
                Assert.False(stagedFile.ShowStage);
                Assert.True(stagedFile.ShowUnstage);
                Assert.Equal("Stage", unstagedFile.StageLabel);
                Assert.Equal("Discard", unstagedFile.DiscardLabel);
                Assert.True(unstagedFile.ShowStage);
                Assert.False(unstagedFile.ShowUnstage);
                Assert.False(unstagedFile.Untracked);
                Assert.Equal("Add", extra.StageLabel);
                Assert.Equal("Delete", extra.DiscardLabel);
                Assert.True(extra.ShowStage);
                Assert.True(extra.ShowDiscard);
                Assert.False(extra.ShowUnstage);
                Assert.True(extra.Untracked);
                Assert.Equal("?", extra.StatusText);
                Assert.True(header.ShowUntrackedActions);
                Assert.False(header.ShowBulk);
                var stagedHeader = vm.Files.Single(row => row.IsHeader && row.Label == "Staged");
                var unstagedHeader = vm.Files.Single(row => row.IsHeader && row.Label == "Unstaged");
                Assert.False(stagedHeader.ShowUntrackedActions);
                Assert.False(unstagedHeader.ShowUntrackedActions);
                Assert.Equal("Unstage all", stagedHeader.BulkLabel);
                Assert.Same(vm.UnstageAllCommand, stagedHeader.BulkCommand);
                Assert.Equal("Stage all", unstagedHeader.BulkLabel);
                Assert.Same(vm.StageAllCommand, unstagedHeader.BulkCommand);
                Assert.Equal("Discard all", unstagedHeader.DiscardAllLabel);
                Assert.True(unstagedHeader.ShowDiscardAll);
                Assert.False(stagedHeader.ShowDiscardAll);

                var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
                window = new Window { Content = view, Width = 1100, Height = 700 };
                window.Show();
                view.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var list = view.FindControl<ListBox>("FileList");
                Assert.NotNull(list);

                Show(list, vm.Files.Single(row => row.Label == "Staged"));
                Assert.Equal(["Unstage all"], VisibleButtons(Head(list, "Staged")));
                Show(list, vm.Files.Single(row => row.Label == "Unstaged"));
                Assert.Equal(["Discard all", "Stage all"], VisibleButtons(Head(list, "Unstaged")));

                Show(list, header);
                Show(list, extra);
                Show(list, added);
                var untrackedHead = Head(list, "Untracked");
                var extraBorder = FileBorder(list, "extra.txt");
                var newBorder = FileBorder(list, "new.txt");
                Assert.Equal(["Add all", "Delete all"], VisibleButtons(untrackedHead));
                Assert.Equal(["Add", "Delete", "History"], VisibleButtons(extraBorder));
                Assert.Same(header.StageCommand, VisibleButton(untrackedHead, "Add all").Command);
                Assert.Same(header.DiscardCommand, VisibleButton(untrackedHead, "Delete all").Command);
                Assert.Same(added.StageCommand, VisibleButton(newBorder, "Add").Command);
                Assert.Same(extra.DiscardCommand, VisibleButton(extraBorder, "Delete").Command);

                dialogs.Accept = false;
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(VisibleButton(extraBorder, "Delete").Command!).ExecuteAsync(null);
                Assert.Equal(["Staged", "staged.txt", "Unstaged", "unstaged.txt", "Untracked", "extra.txt", "new.txt", "notes/"], Names(vm));
                Assert.Equal("Delete", dialogs.Confirms[0].Title);
                Assert.Equal("Delete", dialogs.Confirms[0].Label);
                Assert.Equal("Delete extra.txt? This cannot be undone.", dialogs.Confirms[0].Message);
                Assert.True(File.Exists(Path.Combine(repo.Directory, "extra.txt")));

                await Assert.IsAssignableFrom<IAsyncRelayCommand>(VisibleButton(untrackedHead, "Delete all").Command!).ExecuteAsync(null);
                Assert.Equal(["Staged", "staged.txt", "Unstaged", "unstaged.txt", "Untracked", "extra.txt", "new.txt", "notes/"], Names(vm));
                Assert.Equal("Delete untracked", dialogs.Confirms[1].Title);
                Assert.Equal("Delete all", dialogs.Confirms[1].Label);
                Assert.Contains("extra.txt", dialogs.Confirms[1].Message, StringComparison.Ordinal);
                Assert.Contains("new.txt", dialogs.Confirms[1].Message, StringComparison.Ordinal);
                Assert.Contains("notes/", dialogs.Confirms[1].Message, StringComparison.Ordinal);
                Assert.True(Directory.Exists(Path.Combine(repo.Directory, "notes")));

                await Assert.IsAssignableFrom<IAsyncRelayCommand>(VisibleButton(newBorder, "Add").Command!).ExecuteAsync(null);
                Assert.Equal(
                    ["Staged", "new.txt", "staged.txt", "Unstaged", "unstaged.txt", "Untracked", "extra.txt", "notes/"],
                    Names(vm));
                Assert.Equal("2 staged, 1 unstaged, 2 untracked", vm.Rows.Single(row => row.IsWorkingCopy).Detail);
                Assert.Contains(vm.Files, row => row.Path == "unstaged.txt" && !row.FromStagedList && !row.Untracked);
                Assert.True(File.Exists(Path.Combine(repo.Directory, "extra.txt")));

                dialogs.Accept = true;
                Show(list, vm.Files.Single(row => row.Label == "Untracked"));
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(VisibleButton(Head(list, "Untracked"), "Delete all").Command!).ExecuteAsync(null);
                Assert.Equal(["Staged", "new.txt", "staged.txt", "Unstaged", "unstaged.txt"], Names(vm));
                Assert.Equal("Delete untracked", dialogs.Confirms[2].Title);
                Assert.Equal("Delete all", dialogs.Confirms[2].Label);
                Assert.Contains("extra.txt", dialogs.Confirms[2].Message, StringComparison.Ordinal);
                Assert.Contains("notes/", dialogs.Confirms[2].Message, StringComparison.Ordinal);
                Assert.DoesNotContain("new.txt", dialogs.Confirms[2].Message, StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(repo.Directory, "extra.txt")));
                Assert.False(Directory.Exists(Path.Combine(repo.Directory, "notes")));
                Assert.True(File.Exists(Path.Combine(repo.Directory, "new.txt")));
                Assert.True(File.Exists(Path.Combine(repo.Directory, "unstaged.txt")));
                Assert.Equal("2 staged, 1 unstaged", vm.Rows.Single(row => row.IsWorkingCopy).Detail);

                repo.WriteFile("gone.txt", "gone\n");
                await vm.Refresh();
                Assert.Equal(
                    ["Staged", "new.txt", "staged.txt", "Unstaged", "unstaged.txt", "Untracked", "gone.txt"],
                    Names(vm));
                Show(list, vm.Files.Single(row => row.Label == "Untracked"));
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(VisibleButton(Head(list, "Untracked"), "Add all").Command!).ExecuteAsync(null);
                Assert.Equal(
                    ["Staged", "gone.txt", "new.txt", "staged.txt", "Unstaged", "unstaged.txt"],
                    Names(vm));
                Assert.Contains(vm.Files, row => row.Path == "gone.txt" && row.FromStagedList && !row.Untracked);
                Assert.Contains(vm.Files, row => row.Path == "unstaged.txt" && !row.FromStagedList && !row.Untracked);
                Assert.Equal("3 staged, 1 unstaged", vm.Rows.Single(row => row.IsWorkingCopy).Detail);
            }
            finally
            {
                window?.Close();
                await vm.DisposeAsync();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Unstaged_header_discards_only_unstaged_changes()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("both.txt", "one\n");
        repo.WriteFile("plain.txt", "base\n");
        repo.WriteFile("staged.txt", "keep\n");
        repo.WriteFile("gone.txt", "back\n");
        repo.CommitAll("first");
        repo.WriteFile("both.txt", "staged\n");
        repo.Run("add", "both.txt");
        repo.WriteFile("both.txt", "unstaged\n");
        repo.WriteFile("plain.txt", "changed\n");
        repo.WriteFile("staged.txt", "keep staged\n");
        repo.Run("add", "staged.txt");
        repo.WriteFile("new.txt", "stay\n");
        File.Delete(Path.Combine(repo.Directory, "gone.txt"));
        var dialogs = new RecordingDialogs();

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new FileListHost(repo.Git, dialogs), repo.Directory);
            try
            {
                await vm.EnsureLoadedAsync();
                var header = vm.Files.Single(row => row.IsHeader && row.Label == "Unstaged");
                Assert.Equal("Discard all", header.DiscardAllLabel);

                dialogs.Accept = false;
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(header.DiscardAllCommand).ExecuteAsync(null);
                Assert.Equal("Discard unstaged", dialogs.Confirms[0].Title);
                Assert.Equal("Discard all", dialogs.Confirms[0].Label);
                Assert.Contains("both.txt", dialogs.Confirms[0].Message, StringComparison.Ordinal);
                Assert.Contains("plain.txt", dialogs.Confirms[0].Message, StringComparison.Ordinal);
                Assert.Contains("gone.txt", dialogs.Confirms[0].Message, StringComparison.Ordinal);
                Assert.DoesNotContain("new.txt", dialogs.Confirms[0].Message, StringComparison.Ordinal);
                Assert.DoesNotContain("staged.txt", dialogs.Confirms[0].Message, StringComparison.Ordinal);
                Assert.Equal("unstaged\n", Read(repo, "both.txt"));
                Assert.Equal("changed\n", Read(repo, "plain.txt"));
                Assert.False(File.Exists(Path.Combine(repo.Directory, "gone.txt")));

                dialogs.Accept = true;
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(header.DiscardAllCommand).ExecuteAsync(null);
                Assert.False(vm.HasBanner, vm.Banner);
                Assert.Equal("staged\n", Read(repo, "both.txt"));
                Assert.Equal("base\n", Read(repo, "plain.txt"));
                Assert.Equal("keep staged\n", Read(repo, "staged.txt"));
                Assert.Equal("back\n", Read(repo, "gone.txt"));
                Assert.Equal("stay\n", Read(repo, "new.txt"));
                Assert.Contains(vm.Files, row => row.Path == "both.txt" && row.FromStagedList && !row.Untracked);
                Assert.DoesNotContain(vm.Files, row => row.Path == "both.txt" && !row.FromStagedList);
                Assert.Contains(vm.Files, row => row.Path == "staged.txt" && row.FromStagedList);
                Assert.Contains(vm.Files, row => row.Path == "new.txt" && row.Untracked);
                Assert.DoesNotContain(vm.Files, row => row.IsHeader && row.Label == "Unstaged");
                Assert.DoesNotContain(vm.Files, row => row.Path == "plain.txt" || row.Path == "gone.txt");
            }
            finally
            {
                await vm.DisposeAsync();
            }

            return 0;
        }, CancellationToken.None);
    }

    private static string Read(TempRepo repo, string name) =>
        File.ReadAllText(Path.Combine(repo.Directory, name)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string[] Names(RepositoryViewModel vm) =>
        vm.Files.Select(row => row.IsHeader ? row.Label : row.Path).ToArray();

    private static void Show(ListBox list, object row)
    {
        list.ScrollIntoView(row);
        list.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static Border Head(ListBox list, string label) =>
        list.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("filehead") && border.IsEffectivelyVisible && HeadText(border) == label);

    private static Border FileBorder(ListBox list, string path)
    {
        var border = Text(list, path).FindAncestorOfType<Border>();
        Assert.NotNull(border);
        return border;
    }

    private static string[] VisibleButtons(Control root) =>
        root.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.IsEffectivelyVisible)
            .Select(button => button.Content?.ToString() ?? "")
            .ToArray();

    private static Button VisibleButton(Control root, string content) =>
        root.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.Content?.ToString() == content);

    private static TextBlock Text(ListBox list, string value) =>
        list.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == value && text.IsEffectivelyVisible);

    private static string HeadText(Border head) => HeadLabel(head).Text ?? "";

    private static TextBlock HeadLabel(Border head) =>
        head.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("fileheadlabel"));

    private static double Top(Control control, Visual relative)
    {
        var origin = control.TranslatePoint(default, relative);
        Assert.NotNull(origin);
        return origin.Value.Y;
    }

    private static bool IsClear(IBrush? brush) =>
        brush is null || brush is ISolidColorBrush solid && solid.Color.A == 0;

    private sealed class RecordingDialogs : IDialogService
    {
        public bool Accept { get; set; }

        public List<(string Title, string Message, string Label)> Confirms { get; } = [];

        public Task<bool> ConfirmAsync(string title, string message, string confirm = "OK")
        {
            Confirms.Add((title, message, confirm));
            return Task.FromResult(Accept);
        }

        public Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title, string? startDirectory = null) => Task.FromResult<string?>(null);
        public Task<string?> PickGitExecutableAsync() => Task.FromResult<string?>(null);
        public Task<string?> PromptSecretAsync(string title, string message) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string suggestedName) => Task.FromResult<string?>(null);
        public Task<string?> PickFileAsync(string title, string typeName, IReadOnlyList<string> patterns) => Task.FromResult<string?>(null);
        public Task<CloneRequest?> PromptCloneAsync() => Task.FromResult<CloneRequest?>(null);
        public Task<string?> PickAsync(string title, string message, IReadOnlyList<string> options) => Task.FromResult<string?>(null);
        public Task<PerformanceChoice?> ConfirmPerformanceAsync(PerformanceSuggestion suggestion) => Task.FromResult<PerformanceChoice?>(null);
        public Task<IReadOnlyList<RebaseStep>?> EditRebaseAsync(IReadOnlyList<RebaseStep> steps) => Task.FromResult<IReadOnlyList<RebaseStep>?>(null);
        public Task<SettingsDraft?> EditSettingsAsync(SettingsDraft current) => Task.FromResult<SettingsDraft?>(null);
        public Task ShowAboutAsync() => Task.CompletedTask;
        public Task CopyAsync(string text) => Task.CompletedTask;
    }

    private sealed class FileListHost(string git, IDialogService? dialogs = null) : IWorkspaceHost
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
