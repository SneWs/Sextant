using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Sextant.Git;
using Sextant.Git.Models;
using Sextant.Git.Repo;
using Sextant.Git.Workspace;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class StashShortcutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stash_and_pop_latest_use_command_S_and_command_shift_S(bool autocrlf)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", autocrlf ? "true" : "false");
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "older\n");
        repo.Run("stash", "push", "-m", "older stash");
        repo.WriteFile("a.txt", "latest\n");

        await session.Dispatch(async () =>
        {
            var dialogs = new StashDialogs();
            var (vm, tab) = await Open(repo, dialogs);
            var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
            try
            {
                window.Show();
                await vm.InitializeAsync();
                vm.Attach(dialogs);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var until = DateTime.UtcNow.AddSeconds(5);
                while (tab.IsBusy && DateTime.UtcNow < until)
                    await Task.Delay(30);
                Assert.False(tab.IsBusy);

                var menu = NativeMenu.GetMenu(window)!;
                var repository = menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_Repository");
                var stash = repository.Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_Stash…");
                var pop = repository.Menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_Pop latest stash…");
                Assert.Equal(AppGestures.CommandKey(Key.S), stash.Gesture);
                Assert.Equal(AppGestures.CommandKey(Key.S, KeyModifiers.Shift), pop.Gesture);
                Assert.Same(vm.StashCommand, stash.Command);
                Assert.Same(vm.PopLatestStashCommand, pop.Command);
                Assert.True(stash.IsEnabled);
                Assert.True(pop.IsEnabled);

                var command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
                window.KeyPress(Key.S, command, PhysicalKey.S, null);
                Assert.Equal(1, dialogs.Prompts);
                Assert.Equal(0, dialogs.Confirmations);
                Assert.NotNull(vm.StashCommand.ExecutionTask);
                await vm.StashCommand.ExecutionTask;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("base\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")).Replace("\r\n", "\n", StringComparison.Ordinal));
                Assert.Equal(2, repo.RunCapture("stash", "list").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
                Assert.False(tab.CanStash);
                Assert.False(stash.IsEnabled);
                Assert.True(pop.IsEnabled);
                window.KeyPress(Key.S, command, PhysicalKey.S, null);
                Assert.Equal(1, dialogs.Prompts);

                window.KeyPress(Key.S, command | RawInputModifiers.Shift, PhysicalKey.S, null);
                Assert.Equal(1, dialogs.Prompts);
                Assert.Equal(1, dialogs.Confirmations);
                Assert.Contains("stash@{0}", dialogs.LastConfirmation, StringComparison.Ordinal);
                Assert.NotNull(vm.PopLatestStashCommand.ExecutionTask);
                await vm.PopLatestStashCommand.ExecutionTask;
                Assert.Equal("latest\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")).Replace("\r\n", "\n", StringComparison.Ordinal));
                Assert.Contains("older stash", repo.RunCapture("stash", "list"), StringComparison.Ordinal);
                Assert.Single(repo.RunCapture("stash", "list").Split('\n', StringSplitOptions.RemoveEmptyEntries));
                Assert.True(tab.CanStash);
                Assert.False(tab.HasBanner, tab.Banner);
            }
            finally
            {
                await vm.Shutdown();
                window.DataContext = null;
                window.Close();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Repository_stash_menu_starts_disabled_and_tracks_the_active_repository(bool stashExists, bool autocrlf)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", autocrlf ? "true" : "false");
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("first");
        if (stashExists)
        {
            repo.WriteFile("a.txt", "saved\n");
            repo.Run("stash", "push", "-m", "saved");
        }

        await session.Dispatch(async () =>
        {
            var (vm, tab) = await Open(repo, new StashDialogs());
            var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
            try
            {
                var menu = NativeMenu.GetMenu(window)!;
                var repository = menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_Repository");
                var stash = repository.Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_Stash…");
                var pop = repository.Menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_Pop latest stash…");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(AppGestures.CommandKey(Key.S), stash.Gesture);
                Assert.Equal(AppGestures.CommandKey(Key.S, KeyModifiers.Shift), pop.Gesture);
                Assert.False(stash.IsEnabled);
                Assert.Equal(stashExists, pop.IsEnabled);

                repo.WriteFile("a.txt", "changed\n");
                await tab.Refresh();
                Dispatcher.UIThread.RunJobs();
                Assert.True(stash.IsEnabled);
                Assert.Equal(stashExists, pop.IsEnabled);

                tab.IsBusy = true;
                Dispatcher.UIThread.RunJobs();
                Assert.False(stash.IsEnabled);
                Assert.False(pop.IsEnabled);
                tab.IsBusy = false;
                Dispatcher.UIThread.RunJobs();
                Assert.True(stash.IsEnabled);
                Assert.Equal(stashExists, pop.IsEnabled);

                vm.ActiveTab = null;
                Dispatcher.UIThread.RunJobs();
                Assert.False(stash.IsEnabled);
                Assert.False(pop.IsEnabled);
                vm.ActiveTab = tab;
                Dispatcher.UIThread.RunJobs();
                Assert.True(stash.IsEnabled);
                Assert.Equal(stashExists, pop.IsEnabled);

                repo.Run("restore", "--worktree", "--", "a.txt");
                await tab.Refresh();
                Dispatcher.UIThread.RunJobs();
                Assert.False(stash.IsEnabled);
                Assert.Equal(stashExists, pop.IsEnabled);
            }
            finally
            {
                await vm.Shutdown();
                window.DataContext = null;
                window.Close();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("clean", false)]
    [InlineData("untracked", false)]
    [InlineData("unborn", false)]
    [InlineData("unstaged", true)]
    [InlineData("staged", true)]
    public async Task Stash_is_available_only_when_git_has_changes_it_can_stash(string state, bool expected)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        if (state != "unborn")
            repo.CommitAll("first");
        if (state == "untracked")
            repo.WriteFile("new.txt", "new\n");
        if (state is "staged" or "unstaged")
            repo.WriteFile("a.txt", "changed\n");
        if (state is "staged" or "unborn")
            repo.Run("add", "a.txt");

        await session.Dispatch(async () =>
        {
            var dialogs = new StashDialogs();
            var (vm, tab) = await Open(repo, dialogs);
            try
            {
                Assert.Equal(expected, tab.StashCommand.CanExecute(null));
                Assert.Equal(expected, vm.StashCommand.CanExecute(null));
                Assert.False(tab.PopLatestStashCommand.CanExecute(null));
                Assert.False(vm.PopLatestStashCommand.CanExecute(null));
                vm.TogglePalette();
                Assert.Equal(expected, vm.PaletteMatches.Any(item => item.Title == "Stash"));
                Assert.DoesNotContain(vm.PaletteMatches, item => item.Title == "Pop latest stash");
                vm.ClosePalette();

                var changed = 0;
                vm.StashCommand.CanExecuteChanged += (_, _) => changed++;
                tab.IsBusy = true;
                Assert.False(tab.StashCommand.CanExecute(null));
                Assert.False(vm.StashCommand.CanExecute(null));
                await tab.StashCommand.ExecuteAsync(null);
                await vm.StashCommand.ExecuteAsync(null);
                Assert.Equal(0, dialogs.Prompts);
                tab.IsBusy = false;
                Assert.True(changed > 0);
                Assert.Equal(expected, vm.StashCommand.CanExecute(null));

                if (expected)
                {
                    await vm.StashCommand.ExecuteAsync(null);
                    Assert.Equal(1, dialogs.Prompts);
                    Assert.False(vm.StashCommand.CanExecute(null));
                    Assert.True(vm.PopLatestStashCommand.CanExecute(null));
                    vm.TogglePalette();
                    Assert.DoesNotContain(vm.PaletteMatches, item => item.Title == "Stash");
                    Assert.Contains(vm.PaletteMatches, item => item.Title == "Pop latest stash");
                    vm.ClosePalette();
                    tab.IsBusy = true;
                    Assert.False(vm.PopLatestStashCommand.CanExecute(null));
                    await tab.PopLatestStashCommand.ExecuteAsync(null);
                    await vm.PopLatestStashCommand.ExecuteAsync(null);
                    Assert.Equal(0, dialogs.Confirmations);
                    tab.IsBusy = false;
                    await vm.PopLatestStashCommand.ExecuteAsync(null);
                    Assert.Equal(1, dialogs.Confirmations);
                    Assert.True(vm.StashCommand.CanExecute(null));
                    Assert.False(vm.PopLatestStashCommand.CanExecute(null));
                }
                else
                {
                    await tab.StashCommand.ExecuteAsync(null);
                    await vm.StashCommand.ExecuteAsync(null);
                    Assert.Equal(0, dialogs.Prompts);
                }

                var confirmations = dialogs.Confirmations;
                await tab.PopLatestStashCommand.ExecuteAsync(null);
                await vm.PopLatestStashCommand.ExecuteAsync(null);
                Assert.Equal(confirmations, dialogs.Confirmations);
                vm.ActiveTab = null;
                Assert.False(vm.StashCommand.CanExecute(null));
                Assert.False(vm.PopLatestStashCommand.CanExecute(null));
                vm.ActiveTab = tab;
                Assert.Equal(expected, vm.StashCommand.CanExecute(null));
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
    public async Task Unresolved_conflicts_disable_stash_and_pop_latest()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        repo.WriteFile("a.txt", "stashed\n");
        repo.Run("stash", "push", "-m", "saved");
        var branch = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("a.txt", "other\n");
        repo.CommitAll("other");
        repo.Run("switch", branch);
        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");
        await session.Dispatch(async () =>
        {
            await using (var repository = await RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None))
                await Assert.ThrowsAsync<GitCommandFailedException>(() => repository.MergeAsync("other", CancellationToken.None));
            var dialogs = new StashDialogs();
            var (vm, tab) = await Open(repo, dialogs);
            try
            {
                Assert.False(tab.StashCommand.CanExecute(null));
                Assert.False(tab.PopLatestStashCommand.CanExecute(null));
                Assert.False(vm.StashCommand.CanExecute(null));
                Assert.False(vm.PopLatestStashCommand.CanExecute(null));
                await vm.StashCommand.ExecuteAsync(null);
                await vm.PopLatestStashCommand.ExecuteAsync(null);
                Assert.Equal(0, dialogs.Prompts);
                Assert.Equal(0, dialogs.Confirmations);
                Assert.Contains("saved", repo.RunCapture("stash", "list"), StringComparison.Ordinal);
            }
            finally
            {
                await vm.Shutdown();
            }
            return 0;
        }, CancellationToken.None);
    }

    private static async Task<(MainViewModel Vm, RepositoryViewModel Tab)> Open(TempRepo repo, StashDialogs dialogs)
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

    private sealed class StashDialogs : IDialogService
    {
        public int Prompts { get; private set; }
        public int Confirmations { get; private set; }
        public string LastConfirmation { get; private set; } = "";

        public Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false)
        {
            Prompts++;
            return Task.FromResult<string?>("");
        }

        public Task<bool> ConfirmAsync(string title, string message, string confirm = "OK")
        {
            Confirmations++;
            LastConfirmation = message;
            return Task.FromResult(true);
        }

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
