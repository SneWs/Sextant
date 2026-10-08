using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git.Models;
using Sextant.Git.Repo;
using Sextant.Services;
using Sextant.ViewModels;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class RepositoryAsyncCommandTests
{
    [Fact]
    public async Task Tab_and_location_commands_wait_for_their_actions()
    {
        var activation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new AsyncHost
        {
            ActivateAction = () => activation.Task,
            CloseAction = () => closing.Task,
        };
        var vm = new RepositoryViewModel(host, "repository");
        var activating = vm.ActivateTabCommand.ExecuteAsync(null);
        Assert.False(activating.IsCompleted);
        Assert.True(vm.ActivateTabCommand.IsRunning);
        Assert.True(vm.ActivateTabCommand.CanExecute(null));
        var activatingAgain = vm.ActivateTabCommand.ExecuteAsync(null);
        activation.SetResult();
        await Task.WhenAll(activating, activatingAgain);
        Assert.False(vm.ActivateTabCommand.IsRunning);

        var close = vm.CloseTabCommand.ExecuteAsync(null);
        Assert.False(close.IsCompleted);
        Assert.True(vm.CloseTabCommand.IsRunning);
        closing.SetResult();
        await close;
        Assert.False(vm.CloseTabCommand.IsRunning);

        var opening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(() => opening.Task);
        var location = new LocationItem { ShowOpen = true, OpenCommand = command };
        var opened = vm.ActivateLocation(location);
        Assert.False(opened.IsCompleted);
        Assert.Same(command.ExecutionTask, opened);
        opening.SetResult();
        await opened;
    }

    [Fact]
    public async Task File_clipboard_commands_track_completion_and_propagate_failures()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("docs/readme.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("docs/readme.txt", "two\n");

        await session.Dispatch(async () =>
        {
            var dialogs = new ClipboardDialogs();
            var vm = new RepositoryViewModel(new AsyncHost { GitExecutable = repo.Git, Dialogs = dialogs }, repo.Directory);
            try
            {
                await vm.EnsureLoadedAsync();
                Assert.NotNull(vm.Toplevel);
                var menu = Assert.Single(vm.DiffRows.OfType<DiffFileRow>()).FileMenu!;
                var copies = new[]
                {
                    (menu.CopyFileNameCommand, "readme.txt"),
                    (menu.CopyPathCommand, "docs/readme.txt"),
                    (menu.CopyFullPathCommand, Path.Combine(vm.Toplevel, "docs", "readme.txt")),
                };
                foreach (var (copy, expected) in copies)
                {
                    dialogs.Pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(copy);
                    var copying = command.ExecuteAsync(null);
                    Assert.Equal(expected, dialogs.Text);
                    Assert.False(copying.IsCompleted);
                    Assert.True(command.IsRunning);
                    Assert.Same(copying, command.ExecutionTask);
                    dialogs.Pending.SetResult();
                    await copying;
                    Assert.False(command.IsRunning);
                }

                dialogs.Pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var failing = Assert.IsAssignableFrom<IAsyncRelayCommand>(menu.CopyPathCommand).ExecuteAsync(null);
                var error = new InvalidOperationException("Clipboard unavailable");
                dialogs.Pending.SetException(error);
                Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => failing));
            }
            finally
            {
                await vm.DisposeAsync();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Working_diff_keeps_both_staging_states_when_selection_or_file_mode_changes()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("staged.txt", "one\n");
        repo.WriteFile("unstaged.txt", "one\n");
        repo.WriteFile("partial.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("staged.txt", "two\n");
        repo.WriteFile("partial.txt", "two\n");
        repo.Run("add", "staged.txt", "partial.txt");
        repo.WriteFile("unstaged.txt", "two\n");
        repo.WriteFile("partial.txt", "three\n");
        repo.WriteFile("new.txt", "new\n");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new AsyncHost { GitExecutable = repo.Git }, repo.Directory);
            try
            {
                await vm.EnsureLoadedAsync();
                var headers = vm.DiffRows.OfType<DiffFileRow>().ToArray();
                Assert.Equal(5, headers.Length);
                Assert.Equal(2, headers.Count(row => row.StagingFile?.FromStagedList == true));
                Assert.Equal(3, headers.Count(row => row.StagingFile?.FromStagedList == false));
                foreach (var file in vm.Files.Where(row => !row.IsHeader))
                {
                    vm.SelectedFile = file;
                    await vm.RevealSelectedFile();
                    Assert.Equal(headers, vm.DiffRows.OfType<DiffFileRow>());
                    var selected = headers.Single(row => row.Path == file.Path && row.StagingFile?.FromStagedList == file.FromStagedList);
                    Assert.True(selected.Expanded);
                    Assert.Same(file.FromStagedList ? file.UnstageCommand : file.StageCommand, selected.ActionCommand);
                }

                vm.CollapseAllSectionsCommand.Execute(null);
                var unstagedPartial = vm.Files.Single(row => row.Path == "partial.txt" && !row.FromStagedList);
                vm.SelectedFile = unstagedPartial;
                await vm.RevealSelectedFile();
                Assert.True(headers.Single(row => row.Path == "partial.txt" && row.StagingFile?.FromStagedList == false).Expanded);
                Assert.False(headers.Single(row => row.Path == "partial.txt" && row.StagingFile?.FromStagedList == true).Expanded);

                await vm.ToggleAllFilesCommand.ExecuteAsync(null);
                Assert.False(vm.AllFiles);
                Assert.Equal(5, vm.DiffRows.OfType<DiffFileRow>().Count());
                Assert.True(vm.ShowSectionFolds);
                await vm.Refresh();
                Assert.Equal(5, vm.DiffRows.OfType<DiffFileRow>().Count());
                Assert.False(vm.HasBanner, vm.Banner);
            }
            finally
            {
                await vm.DisposeAsync();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Combined_diff_actions_keep_their_staging_direction_after_selection_changes(bool staged, bool lineAction)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "base\nstaged\n");
        repo.Run("add", "a.txt");
        repo.WriteFile("a.txt", "base\nstaged\nunstaged\n");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new AsyncHost { GitExecutable = repo.Git }, repo.Directory);
            try
            {
                await vm.EnsureLoadedAsync();
                await vm.ExpandAllSectionsCommand.ExecuteAsync(null);
                var header = vm.DiffRows.OfType<DiffFileRow>().Single(row => row.StagingFile?.FromStagedList == staged);
                var body = vm.DiffRows.Skip(vm.DiffRows.IndexOf(header) + 1).TakeWhile(row => row is not DiffFileRow).ToArray();
                var action = lineAction
                    ? body.OfType<DiffEditorRow>().SelectMany(row => row.Lines).Single(row => row.Kind == EditorLineKind.Added && row.ShowAction).ActionCommand
                    : Assert.Single(body.OfType<DiffHunkRow>()).ActionCommand;
                vm.SelectedFile = vm.Files.Single(row => !row.IsHeader && row.FromStagedList != staged);
                await vm.RevealSelectedFile();
                Assert.Contains(header, vm.DiffRows);
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(action).ExecuteAsync(null);
                Assert.False(vm.HasBanner, vm.Banner);
                var index = repo.RunCapture("show", ":a.txt").Replace("\r\n", "\n", StringComparison.Ordinal);
                Assert.Equal(staged ? "base\n" : "base\nstaged\nunstaged\n", index);
                Assert.Equal("base\nstaged\nunstaged\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")));
            }
            finally
            {
                await vm.DisposeAsync();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Working_diff_in_an_unborn_repository_includes_staged_and_untracked_files()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("staged.txt", "staged\n");
        repo.Run("add", "staged.txt");
        repo.WriteFile("untracked.txt", "untracked\n");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new AsyncHost { GitExecutable = repo.Git }, repo.Directory);
            try
            {
                await vm.EnsureLoadedAsync();
                var headers = vm.DiffRows.OfType<DiffFileRow>().ToArray();
                Assert.Equal(2, headers.Length);
                Assert.Contains(headers, row => row.Path == "staged.txt" && row.ActionLabel == "Unstage file");
                Assert.Contains(headers, row => row.Path == "untracked.txt" && row.ActionLabel == "Stage file");
                Assert.False(vm.HasBanner, vm.Banner);
            }
            finally
            {
                await vm.DisposeAsync();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Partially_staged_image_has_separate_previews_and_folds()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DiffPreviewApp));
        using var repo = new TempRepo();
        static string Svg(string color) =>
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect width=\"8\" height=\"8\" fill=\"{color}\"/></svg>\n";
        repo.WriteFile("image.svg", Svg("#ff0000"));
        repo.CommitAll("first");
        repo.WriteFile("image.svg", Svg("#00ff00"));
        repo.Run("add", "image.svg");
        repo.WriteFile("image.svg", Svg("#0000ff"));

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new AsyncHost { GitExecutable = repo.Git }, repo.Directory);
            try
            {
                await vm.EnsureLoadedAsync();
                Assert.Equal(2, vm.ImageCompares.Count);
                var staged = vm.ImageCompares.Single(row => row.StagingFile?.FromStagedList == true);
                var unstaged = vm.ImageCompares.Single(row => row.StagingFile?.FromStagedList == false);
                Assert.NotNull(staged.Before);
                Assert.NotNull(staged.After);
                Assert.NotNull(unstaged.Before);
                Assert.NotNull(unstaged.After);
                static byte[] Png(Bitmap bitmap)
                {
                    using var stream = new MemoryStream();
                    bitmap.Save(stream, PngBitmapEncoderOptions.Default);
                    return stream.ToArray();
                }
                Assert.Equal(Png(staged.After), Png(unstaged.Before));
                Assert.False(Png(staged.Before).SequenceEqual(Png(staged.After)));
                Assert.False(Png(unstaged.Before).SequenceEqual(Png(unstaged.After)));

                await vm.ExpandAllSectionsCommand.ExecuteAsync(null);
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(staged.ToggleCommand).ExecuteAsync(null);
                Assert.False(staged.IsOpen);
                Assert.True(unstaged.IsOpen);
                Assert.DoesNotContain(vm.DiffRows.OfType<DiffImageRow>(), row => ReferenceEquals(row.Image, staged));
                Assert.Contains(vm.DiffRows.OfType<DiffImageRow>(), row => ReferenceEquals(row.Image, unstaged));
                vm.SelectedFile = vm.Files.Single(row => row.Path == "image.svg" && !row.FromStagedList);
                await vm.RevealSelectedFile();
                Assert.Same(staged, vm.ImageCompares.Single(row => row.StagingFile?.FromStagedList == true));
                Assert.Same(unstaged, vm.ImageCompares.Single(row => row.StagingFile?.FromStagedList == false));
                Assert.False(vm.HasBanner, vm.Banner);
            }

            finally
            {
                await vm.DisposeAsync();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Diff_file_actions_are_hidden_for_blame_commits_and_ranges()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("second");
        repo.WriteFile("a.txt", "three\n");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new AsyncHost { GitExecutable = repo.Git }, repo.Directory);
            try
            {
                await vm.EnsureLoadedAsync();
                Assert.True(Assert.Single(vm.DiffRows.OfType<DiffFileRow>()).ShowAction);
                await vm.ShowBlameCommand.ExecuteAsync(null);
                var blame = Assert.Single(vm.DiffRows.OfType<DiffFileRow>());
                Assert.False(blame.ShowAction);
                Assert.False(blame.ActionCommand.CanExecute(null));

                await vm.ShowDiffCommand.ExecuteAsync(null);
                var newest = vm.Rows.Single(row => row.Subject == "second");
                var oldest = vm.Rows.Single(row => row.Subject == "first");
                vm.SelectedGraphRow = newest;
                Dispatcher.UIThread.RunJobs();
                await vm.LoadDetailsCommand.ExecutionTask!;
                var commit = Assert.Single(vm.DiffRows.OfType<DiffFileRow>());
                Assert.False(commit.ShowAction);
                Assert.False(commit.ActionCommand.CanExecute(null));

                await vm.NoteGraphSelection([newest, oldest]);
                var range = Assert.Single(vm.DiffRows.OfType<DiffFileRow>());
                Assert.False(range.ShowAction);
                Assert.False(range.ActionCommand.CanExecute(null));
                Assert.False(vm.HasBanner, vm.Banner);
            }
            finally
            {
                await vm.DisposeAsync();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("new.txt", "new\n", true)]
    [InlineData("empty.txt", "", true)]
    [InlineData("binary.bin", "changed\0bytes", false)]
    [InlineData("image.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect width=\"8\" height=\"8\" fill=\"#00ff00\"/></svg>\n", true)]
    public async Task Diff_file_header_can_stage_new_and_binary_files(string path, string contents, bool untracked)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("existing.txt", "one\n");
        if (!untracked)
            repo.WriteFile(path, "original\0bytes");
        repo.CommitAll("first");
        repo.WriteFile(path, contents);

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new AsyncHost { GitExecutable = repo.Git }, repo.Directory);
            try
            {
                await vm.EnsureLoadedAsync();
                var header = Assert.Single(vm.DiffRows.OfType<DiffFileRow>());
                Assert.Equal(path, header.Path);
                Assert.True(header.ShowAction);
                Assert.Equal("Stage file", header.ActionLabel);
                Assert.Same(vm.Files.Single(row => row.Path == path).StageCommand, header.ActionCommand);
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(header.ActionCommand).ExecuteAsync(null);
                Assert.Equal(path, repo.RunCapture("diff", "--cached", "--name-only").Trim());
                Assert.Contains(vm.Files, row => row.Path == path && row.FromStagedList);
                Assert.False(vm.HasBanner, vm.Banner);
            }
            finally
            {
                await vm.DisposeAsync();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Blame_commands_wait_for_loading_and_can_collapse_while_loading()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.WriteFile("b.txt", "two\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "one changed\n");
        repo.WriteFile("b.txt", "two changed\n");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new AsyncHost { GitExecutable = repo.Git }, repo.Directory);
            try
            {
                await vm.EnsureLoadedAsync();
                await vm.ShowBlameCommand.ExecuteAsync(null);
                Assert.True(vm.ShowingBlame);
                Assert.Contains(vm.DiffRows.OfType<DiffEditorRow>(), row => row.Path == "a.txt" && row.Blame);
                Assert.DoesNotContain(vm.DiffRows.OfType<BlameRow>(), row => row.Text == "Loading…");

                var header = vm.DiffRows.OfType<DiffFileRow>().Single(row => row.Path == "b.txt");
                var toggle = Assert.IsAssignableFrom<IAsyncRelayCommand>(header.ToggleCommand);
                var loading = toggle.ExecuteAsync(null);
                Assert.True(header.Expanded);
                Assert.True(toggle.CanExecute(null));
                await toggle.ExecuteAsync(null);
                Assert.False(header.Expanded);
                await loading;
                Assert.DoesNotContain(vm.DiffRows.OfType<DiffEditorRow>(), row => row.Path == "b.txt");

                await toggle.ExecuteAsync(null);
                Assert.True(header.Expanded);
                Assert.Contains(vm.DiffRows.OfType<DiffEditorRow>(), row => row.Path == "b.txt" && row.Blame);
                Assert.DoesNotContain(vm.DiffRows.OfType<BlameRow>(), row => row.Text == "Loading…");

                await vm.ShowDiffCommand.ExecuteAsync(null);
                await vm.ShowBlameCommand.ExecuteAsync(null);
                Assert.Contains(vm.DiffRows.OfType<DiffEditorRow>(), row => row.Path == "b.txt" && row.Blame);
                Assert.DoesNotContain(vm.DiffRows.OfType<BlameRow>(), row => row.Text == "Loading…");
                Assert.False(vm.HasBanner, vm.Banner);
            }
            finally
            {
                await vm.DisposeAsync();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Range_selection_and_diff_preferences_finish_loading_before_returning()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("second");
        repo.WriteFile("a.txt", "three\n");
        repo.CommitAll("third");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new AsyncHost { GitExecutable = repo.Git }, repo.Directory);
            try
            {
                await vm.EnsureLoadedAsync();
                var oldest = vm.Rows.Single(row => row.Subject == "first");
                var newest = vm.Rows.Single(row => row.Subject == "third");
                await vm.NoteGraphSelection([newest, oldest]);
                Assert.Equal(oldest.Sha + ".." + newest.Sha, vm.CommitShaText);
                Assert.True(vm.ShowCommitStats);
                Assert.Equal("a.txt", Assert.Single(vm.DiffRows.OfType<DiffFileRow>()).Path);

                await vm.ApplyDiffPreferences(sideBySide: true, ignoreWhitespace: true);
                Assert.True(vm.SideBySide);
                Assert.True(vm.IgnoreWhitespace);
                Assert.Equal("a.txt", Assert.Single(vm.DiffRows.OfType<DiffFileRow>()).Path);

                await vm.ShowBlameCommand.ExecuteAsync(null);
                Assert.True(vm.ShowingBlame);
                await vm.ApplyDiffFormats([]);
                Assert.False(vm.ShowingBlame);
                Assert.Equal("a.txt", Assert.Single(vm.DiffRows.OfType<DiffFileRow>()).Path);
                Assert.False(vm.HasBanner, vm.Banner);
            }
            finally
            {
                await vm.DisposeAsync();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Merge_command_restores_working_files_and_waits_for_the_editor()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.Run("config", "merge.tool", "");
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("a.txt", "other\n");
        repo.CommitAll("other");
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");
        await session.Dispatch(async () =>
        {
            await using (var repository = await RepositorySession.OpenAsync(
                new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None))
            {
                await Assert.ThrowsAsync<GitCommandFailedException>(() => repository.MergeAsync("other", CancellationToken.None));
            }
            var vm = new RepositoryViewModel(new AsyncHost { GitExecutable = repo.Git }, repo.Directory);
            try
            {
                await vm.EnsureLoadedAsync();
                var merge = Assert.IsAssignableFrom<IAsyncRelayCommand>(
                    vm.Files.Single(row => row.Path == "a.txt").MergetoolCommand);
                vm.SelectedGraphRow = vm.Rows.Single(row => row.Subject == "main");
                Dispatcher.UIThread.RunJobs();
                await vm.LoadDetailsCommand.ExecutionTask!;
                Assert.True(vm.ShowingCommit);
                Assert.False(vm.ShowingMerge);

                await merge.ExecuteAsync(null);
                Assert.True(vm.ShowingWorkingCopy);
                Assert.True(vm.SelectedGraphRow!.IsWorkingCopy);
                Assert.True(vm.ShowingMerge);
                Assert.Equal("a.txt", vm.MergePath);
                Assert.NotNull(vm.MergeEdit);
                Assert.True(vm.MergeEdit.ConflictCount > 0);
            }
            finally
            {
                await vm.DisposeAsync();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Disposal_waits_for_opening_and_prevents_reopening()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new AsyncHost { GitExecutable = repo.Git }, repo.Directory);
            var loading = vm.EnsureLoadedAsync();
            var disposing = vm.DisposeAsync().AsTask();
            Assert.Same(disposing, vm.DisposeAsync().AsTask());
            await disposing;
            Assert.True(loading.IsCompleted);
            await loading;
            Assert.False(vm.IsReady);
            Assert.False(vm.IsBusy);
            Assert.True(vm.EnsureLoadedAsync().IsCompletedSuccessfully);
            Assert.False(vm.IsReady);
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Disposal_drains_concurrent_details_and_blame_loads()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("second");
        repo.WriteFile("a.txt", "three\n");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new AsyncHost { GitExecutable = repo.Git }, repo.Directory);
            await vm.EnsureLoadedAsync();
            var blame = vm.ShowBlameCommand.ExecuteAsync(null);
            vm.SelectedGraphRow = vm.Rows.Single(row => row.Subject == "second");
            Dispatcher.UIThread.RunJobs();
            var details = vm.LoadDetailsCommand.ExecutionTask!;
            await vm.DisposeAsync();
            Assert.True(blame.IsCompleted);
            Assert.True(details.IsCompleted);
            await Task.WhenAll(blame, details);
            Assert.False(vm.IsReady);
            Assert.Empty(vm.ImageCompares);
            Assert.False(vm.IsBusy);
            return 0;
        }, CancellationToken.None);
    }

    public class DiffPreviewApp : Application
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<DiffPreviewApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    private sealed class AsyncHost : IWorkspaceHost
    {
        public IDialogService? Dialogs { get; init; }
        public GitProcessRunner Runner { get; } = new();
        public string? GitExecutable { get; init; }
        public string? MergeTool => null;
        public bool GitReady => GitExecutable is not null;
        public Func<Task> ActivateAction { get; init; } = () => Task.CompletedTask;
        public Func<Task> CloseAction { get; init; } = () => Task.CompletedTask;
        public Task Activate(RepositoryViewModel tab) => ActivateAction();
        public Task Close(RepositoryViewModel tab) => CloseAction();
        public void NoteLoaded(RepositoryViewModel tab) { }
        public void Save() { }
        public Task OpenRepositoryAsync(string path) => Task.CompletedTask;
    }

    private sealed class ClipboardDialogs : IDialogService
    {
        public TaskCompletionSource Pending { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Text { get; private set; } = "";
        public Task CopyAsync(string text)
        {
            Text = text;
            return Pending.Task;
        }

        public Task<string?> PickFolderAsync(string title, string? startDirectory = null) => Task.FromResult<string?>(null);
        public Task<string?> PickGitExecutableAsync() => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message, string confirm = "OK") => Task.FromResult(false);
        public Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false) => Task.FromResult<string?>(null);
        public Task<string?> PromptSecretAsync(string title, string message) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string suggestedName) => Task.FromResult<string?>(null);
        public Task<string?> PickFileAsync(string title, string typeName, IReadOnlyList<string> patterns) => Task.FromResult<string?>(null);
        public Task<CloneRequest?> PromptCloneAsync() => Task.FromResult<CloneRequest?>(null);
        public Task<string?> PickAsync(string title, string message, IReadOnlyList<string> options) => Task.FromResult<string?>(null);
        public Task<PerformanceChoice?> ConfirmPerformanceAsync(PerformanceSuggestion suggestion) => Task.FromResult<PerformanceChoice?>(null);
        public Task<IReadOnlyList<RebaseStep>?> EditRebaseAsync(IReadOnlyList<RebaseStep> steps) => Task.FromResult<IReadOnlyList<RebaseStep>?>(null);
        public Task<SettingsDraft?> EditSettingsAsync(SettingsDraft current) => Task.FromResult<SettingsDraft?>(null);
        public Task ShowAboutAsync() => Task.CompletedTask;
    }
}
