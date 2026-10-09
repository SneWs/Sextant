using Avalonia.Threading;
using Avalonia.Headless;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;
using Sextant.Git.Models;
using Sextant.Git.Repo;
using Sextant.Git.Workspace;
using Sextant.Services;
using Sextant.ViewModels;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class TagRevealTests
{
    [Fact]
    public async Task Clicking_a_tag_outside_the_loaded_history_loads_history_up_to_it()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("first");
        repo.Run("switch", "-c", "side");
        repo.WriteFile("b.txt", "side\n");
        repo.CommitAll("side commit");
        repo.Run("tag", "v1");
        var tagOid = repo.RunCapture("rev-parse", "v1").Trim();

        await session.Dispatch(async () =>
        {
            var dialogs = new QuietDialogs();
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
            try
            {
                await Settle(tab);

                // Hiding the branch also drops tags from the graph roots, so the
                // tagged commit leaves the loaded history.
                var side = tab.Locations.Single(item => item.Key == "b:side");
                await Run(((AsyncRelayCommand)side.HideCommand!));
                await Settle(tab);
                Assert.DoesNotContain(tab.Rows, row => string.Equals(row.Sha, tagOid, StringComparison.OrdinalIgnoreCase));

                var tag = tab.Locations.Single(item => item.Key == "t:v1");
                var reveal = (AsyncRelayCommand)tag.RevealCommand!;
                reveal.Execute(null);
                Assert.True(tab.IsLoadingHistory, "spinner should be up while the history loads");
                if (reveal.ExecutionTask is { } revealTask)
                    await revealTask;
                Dispatcher.UIThread.RunJobs();
                Assert.False(tab.IsLoadingHistory, "spinner should be down once the history is loaded");
                await Settle(tab);

                Assert.False(tab.HasBanner, tab.Banner);
                Assert.Contains(tab.Rows, row => string.Equals(row.Sha, tagOid, StringComparison.OrdinalIgnoreCase));
                Assert.Equal(tagOid, tab.SelectedGraphRow?.Sha);
                Assert.True(tab.HasHistoryQuery);
            }
            finally
            {
                await vm.Shutdown();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Show_in_graph_on_a_hidden_branch_loads_history_up_to_it()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("first");
        repo.Run("switch", "-c", "side");
        repo.WriteFile("b.txt", "side\n");
        repo.CommitAll("side commit");
        var tipOid = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("switch", "master");
        repo.WriteFile("c.txt", "main\n");
        repo.CommitAll("main commit");

        await session.Dispatch(async () =>
        {
            var dialogs = new QuietDialogs();
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
            try
            {
                await Settle(tab);

                var side = tab.Locations.Single(item => item.Key == "b:side");
                await Run(((AsyncRelayCommand)side.HideCommand!));
                await Settle(tab);
                Assert.DoesNotContain(tab.Rows, row => string.Equals(row.Sha, tipOid, StringComparison.OrdinalIgnoreCase));

                var reveal = (AsyncRelayCommand)side.RevealCommand!;
                reveal.Execute(null);
                Assert.True(tab.IsLoadingHistory, "spinner should be up while the history loads");
                if (reveal.ExecutionTask is { } revealTask)
                    await revealTask;
                Dispatcher.UIThread.RunJobs();
                Assert.False(tab.IsLoadingHistory, "spinner should be down once the history is loaded");
                await Settle(tab);

                Assert.False(tab.HasBanner, tab.Banner);
                Assert.Contains(tab.Rows, row => string.Equals(row.Sha, tipOid, StringComparison.OrdinalIgnoreCase));
                Assert.Equal(tipOid, tab.SelectedGraphRow?.Sha);
                Assert.True(tab.HasHistoryQuery);
            }
            finally
            {
                await vm.Shutdown();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Show_in_graph_pages_to_a_branch_tip_beyond_the_first_page()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("first");
        for (var i = 0; i < 330; i++)
        {
            repo.WriteFile("a.txt", $"base {i}\n");
            repo.Run("commit", "-a", "-m", $"c{i}");
        }
        repo.Run("branch", "deep", "HEAD~320");
        var tipOid = repo.RunCapture("rev-parse", "deep").Trim();

        await session.Dispatch(async () =>
        {
            var dialogs = new QuietDialogs();
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
            try
            {
                await Settle(tab);
                Assert.True(tab.Rows.Count >= 300);
                Assert.DoesNotContain(tab.Rows, row => string.Equals(row.Sha, tipOid, StringComparison.OrdinalIgnoreCase));

                var deep = tab.Locations.Single(item => item.Key == "b:deep");
                await Run(((AsyncRelayCommand)deep.RevealCommand!));
                await Settle(tab);

                Assert.False(tab.HasBanner, tab.Banner);
                Assert.Equal(tipOid, tab.SelectedGraphRow?.Sha);
            }
            finally
            {
                await vm.Shutdown();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Show_in_graph_on_a_hidden_remote_branch_loads_history_up_to_it()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("first");
        repo.Run("switch", "-c", "feat");
        repo.WriteFile("b.txt", "feat\n");
        repo.CommitAll("feat commit");
        var tipOid = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("switch", "master");
        repo.Run("update-ref", "refs/remotes/origin/feat", tipOid);
        repo.Run("branch", "-D", "feat");

        await session.Dispatch(async () =>
        {
            var dialogs = new QuietDialogs();
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
            try
            {
                await Settle(tab);

                var remote = tab.Locations.Single(item => item.Key == "r:origin/feat");
                await Run(((AsyncRelayCommand)remote.HideCommand!));
                await Settle(tab);
                Assert.DoesNotContain(tab.Rows, row => string.Equals(row.Sha, tipOid, StringComparison.OrdinalIgnoreCase));

                await Run(((AsyncRelayCommand)remote.RevealCommand!));
                await Settle(tab);

                Assert.False(tab.HasBanner, tab.Banner);
                Assert.Contains(tab.Rows, row => string.Equals(row.Sha, tipOid, StringComparison.OrdinalIgnoreCase));
                Assert.Equal(tipOid, tab.SelectedGraphRow?.Sha);
                Assert.True(tab.HasHistoryQuery);
            }
            finally
            {
                await vm.Shutdown();
            }
            return 0;
        }, CancellationToken.None);
    }

    private static async Task Run(AsyncRelayCommand command)
    {
        command.Execute(null);
        if (command.ExecutionTask is { } task)
            await task;
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Settle(RepositoryViewModel tab)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (tab.IsBusy && DateTime.UtcNow < until)
        {
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.False(tab.IsBusy);
    }

    private sealed class QuietDialogs : IDialogService
    {
        public Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false) =>
            Task.FromResult<string?>(initial);

        public Task<bool> ConfirmAsync(string title, string message, string confirm = "OK") =>
            Task.FromResult(true);

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
