using Avalonia.Threading;
using Avalonia.Headless;
using Sextant.Git;
using Sextant.Git.Models;
using Sextant.Git.Repo;
using Sextant.Git.Workspace;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class CommitterTests
{
    [Fact]
    public async Task EditCommitter_writes_local_config_and_updates_the_row()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("first");

        await session.Dispatch(async () =>
        {
            var dialogs = new CommitterDialogs(new CommitterEdit("New Name", "new@example.com", false), null);
            var (vm, tab) = await Open(repo, dialogs);
            try
            {
                var until = DateTime.UtcNow.AddSeconds(5);
                while (tab.IsBusy && DateTime.UtcNow < until)
                    await Task.Delay(30);
                Assert.False(tab.IsBusy);
                Assert.Equal("Test <test@example.com>", tab.CommitterText);

                tab.EditCommitterCommand.Execute(null);
                if (tab.EditCommitterCommand.ExecutionTask is { } task)
                    await task;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("New Name", repo.RunCapture("config", "--local", "--get", "user.name").Trim());
                Assert.Equal("new@example.com", repo.RunCapture("config", "--local", "--get", "user.email").Trim());
                Assert.Equal("New Name <new@example.com>", tab.CommitterText);
                Assert.False(tab.HasBanner, tab.Banner);
            }
            finally
            {
                await vm.Shutdown();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditCommitter_saves_when_the_window_reactivates_while_the_dialog_is_open()
    {
        // Closing a dialog re-activates the main window, which would normally
        // start a focus refresh that takes IsBusy and silently drops the save.
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("first");

        await session.Dispatch(async () =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var dialogs = new CommitterDialogs(new CommitterEdit("Delayed Name", "delayed@example.com", false), gate.Task);
            var (vm, tab) = await Open(repo, dialogs);
            try
            {
                var until = DateTime.UtcNow.AddSeconds(5);
                while (tab.IsBusy && DateTime.UtcNow < until)
                    await Task.Delay(30);

                tab.EditCommitterCommand.Execute(null);
                await Task.Delay(100);
                // Fire without awaiting: Refresh() takes IsBusy synchronously,
                // reproducing the activation race that closed the dialog triggers.
                _ = tab.RefreshFromFocusAsync();
                gate.SetResult();
                if (tab.EditCommitterCommand.ExecutionTask is { } task)
                    await task;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("Delayed Name", repo.RunCapture("config", "--local", "--get", "user.name").Trim());
                Assert.Equal("Delayed Name <delayed@example.com>", tab.CommitterText);
                Assert.False(tab.HasBanner, tab.Banner);
            }
            finally
            {
                await vm.Shutdown();
            }
            return 0;
        }, CancellationToken.None);
    }

    private static async Task<(MainViewModel Vm, RepositoryViewModel Tab)> Open(TempRepo repo, CommitterDialogs dialogs)
    {
        var store = new WorkspaceStore(Path.Combine(repo.Directory, ".git", "sextant-workspace"));
        var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings { GitExecutable = repo.Git, ReopenTabs = false }, new GitProcessRunner())
        {
            GitExecutable = repo.Git,
            GitReady = true,
        };
        vm.Attach(dialogs);
        var tab = new RepositoryViewModel(vm, repo.Directory);
        vm.Tabs.Add(tab);
        await vm.Activate(tab);
        return (vm, tab);
    }

    private sealed class CommitterDialogs : IDialogService
    {
        private readonly CommitterEdit? _edit;
        private readonly Task? _hold;

        public CommitterDialogs(CommitterEdit? edit, Task? hold = null)
        {
            _edit = edit;
            _hold = hold;
        }

        public async Task<CommitterEdit?> PromptCommitterAsync(string? name, string? email)
        {
            if (_hold is not null)
                await _hold;
            return _edit;
        }
        public Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false) => Task.FromResult<string?>(null);
        public Task<bool> ConfirmAsync(string title, string message, string confirm = "OK") => Task.FromResult(true);
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
}
