using Avalonia.Headless;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
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
