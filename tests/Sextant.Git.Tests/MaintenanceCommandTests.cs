using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Sextant;
using Sextant.Git;
using Sextant.Git.Workspace;
using Sextant.Views;
using Sextant.Git.Diff;
using Sextant.Git.Models;
using Sextant.Git.Repo;
using Sextant.Services;
using Sextant.ViewModels;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class MaintenanceCommandTests
{
    [Fact]
    public async Task Garbage_collection_keeps_the_repository_usable()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        await using var session = await Open(repo);
        await session.CollectGarbageAsync(false, null, CancellationToken.None);
        await session.CollectGarbageAsync(true, null, CancellationToken.None);
        Assert.Contains("first", repo.RunCapture("log", "-1", "--format=%s"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fetch_and_prune_drops_a_remote_branch_the_remote_no_longer_has()
    {
        using var origin = new TempRepo();
        origin.WriteFile("a.txt", "one\n");
        origin.CommitAll("first");
        using var repo = new TempRepo();
        repo.Run("remote", "add", "origin", origin.Directory);
        repo.Run("fetch", "origin");
        var branch = origin.CurrentBranch();
        var head = origin.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("update-ref", "refs/remotes/origin/stale", head);

        await using var session = await Open(repo);
        await session.FetchPruneAsync(null, CancellationToken.None);

        var remoteBranches = repo.RunCapture("branch", "-r");
        Assert.Contains("origin/" + branch, remoteBranches, StringComparison.Ordinal);
        Assert.DoesNotContain("origin/stale", remoteBranches, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Merged_branches_leave_out_the_current_branch_a_worktree_and_an_unmerged_branch()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var current = repo.CurrentBranch();
        repo.Run("branch", "done");
        repo.Run("branch", "linked");
        var worktree = Path.Combine(Path.GetTempPath(), "sextant-wt-" + Guid.NewGuid().ToString("N"));
        repo.Run("worktree", "add", worktree, "linked");
        try
        {
            repo.Run("switch", "-c", "open");
            repo.WriteFile("a.txt", "two\n");
            repo.CommitAll("second");
            repo.Run("switch", current);

            await using var session = await Open(repo);
            var names = await session.MergedBranchNamesAsync(CancellationToken.None);
            Assert.Equal(["done"], names);
            await session.DeleteBranchesAsync(names, CancellationToken.None);

            var branches = repo.RunCapture("branch");
            Assert.DoesNotContain("done", branches, StringComparison.Ordinal);
            Assert.Contains(current, branches, StringComparison.Ordinal);
            Assert.Contains("open", branches, StringComparison.Ordinal);
            Assert.Contains("linked", branches, StringComparison.Ordinal);
        }
        finally
        {
            repo.Run("worktree", "remove", "--force", worktree);
        }
    }

    [Fact]
    public async Task Clean_removes_untracked_files_and_directories_and_leaves_ignored_files()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.WriteFile(".gitignore", "*.log\n");
        repo.CommitAll("first");
        repo.WriteFile("junk.txt", "x\n");
        repo.WriteFile("junkdir/a.txt", "y\n");
        repo.WriteFile("kept.log", "z\n");

        await using var session = await Open(repo);
        var preview = await session.CleanPreviewAsync(CancellationToken.None);
        Assert.Contains("junk.txt", preview, StringComparison.Ordinal);
        Assert.Contains("junkdir/", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("kept.log", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("a.txt", preview, StringComparison.Ordinal);
        await session.CleanUntrackedAsync(null, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(repo.Directory, "junk.txt")));
        Assert.False(Directory.Exists(Path.Combine(repo.Directory, "junkdir")));
        Assert.Equal("z\n", File.ReadAllText(Path.Combine(repo.Directory, "kept.log")).Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(repo.Directory, "a.txt")));
    }

    [Fact]
    public async Task Clean_and_delete_merged_branches_wait_for_confirmation()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.WriteFile(".gitignore", "*.log\n");
        repo.CommitAll("first");
        var current = repo.CurrentBranch();
        repo.Run("branch", "done");
        repo.Run("switch", "-c", "open");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("second");
        repo.Run("switch", current);
        repo.WriteFile("junk.txt", "x\n");
        repo.WriteFile("kept.log", "z\n");

        await session.Dispatch(async () =>
        {
            var dialogs = new ChoiceDialogs();
            var vm = new RepositoryViewModel(new MaintenanceHost(repo.Git, dialogs), repo.Directory);
            try
            {
                await vm.EnsureLoadedAsync();
                await vm.CleanUntrackedFiles();
                Assert.Equal("Clean untracked files", dialogs.Title);
                Assert.Equal("Clean", dialogs.Confirm);
                Assert.Contains("junk.txt", dialogs.Message, StringComparison.Ordinal);
                Assert.Contains("cannot be undone", dialogs.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("kept.log", dialogs.Message, StringComparison.Ordinal);
                Assert.True(File.Exists(Path.Combine(repo.Directory, "junk.txt")));

                dialogs.Accept = true;
                await vm.CleanUntrackedFiles();
                Assert.False(vm.HasBanner, vm.Banner);
                Assert.False(File.Exists(Path.Combine(repo.Directory, "junk.txt")));
                Assert.True(File.Exists(Path.Combine(repo.Directory, "kept.log")));

                dialogs.Accept = false;
                await vm.DeleteMergedBranches();
                Assert.Equal("Delete merged branches", dialogs.Title);
                Assert.Equal("Delete", dialogs.Confirm);
                Assert.Contains("done", dialogs.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("open", dialogs.Message, StringComparison.Ordinal);
                Assert.Contains("done", repo.RunCapture("branch"), StringComparison.Ordinal);

                dialogs.Accept = true;
                await vm.DeleteMergedBranches();
                Assert.False(vm.HasBanner, vm.Banner);
                var branches = repo.RunCapture("branch");
                Assert.DoesNotContain("done", branches, StringComparison.Ordinal);
                Assert.Contains("open", branches, StringComparison.Ordinal);
                Assert.Contains(current, branches, StringComparison.Ordinal);

                await vm.DeleteMergedBranches();
                Assert.Equal("No merged branches to delete.", vm.Banner);
                await vm.CleanUntrackedFiles();
                Assert.Equal("No untracked files to clean.", vm.Banner);
            }
            finally
            {
                await vm.DisposeAsync();
            }

            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Maintenance_menu_runs_git_gc_on_the_open_repository()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var directory = Path.Combine(repo.Directory, ".git", "sextant-workspace");

        await session.Dispatch(async () =>
        {
            var store = new WorkspaceStore(directory);
            var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings { GitExecutable = repo.Git, ReopenTabs = false }, new GitProcessRunner())
            {
                GitExecutable = repo.Git,
                GitReady = true,
            };
            var window = new MainWindow { DataContext = vm, Width = 800, Height = 600 };
            window.Show();
            try
            {
                Assert.False(vm.CollectGarbageCommand.CanExecute(null));
                var menu = NativeMenu.GetMenu(window);
                Assert.NotNull(menu);
                var repository = menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_Repository");
                var maintenance = repository.Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_Maintenance");
                var items = maintenance.Menu!.Items.OfType<NativeMenuItem>().ToList();
                Assert.Equal(
                    ["_Git GC", "Git GC _aggressively", "_Delete merged branches", "_Fetch and prune", "_Clean untracked files"],
                    items.Select(item => item.Header).ToList());
                Assert.Same(vm.CollectGarbageCommand, items[0].Command);
                Assert.Same(vm.CollectGarbageAggressivelyCommand, items[1].Command);
                Assert.Same(vm.DeleteMergedBranchesCommand, items[2].Command);
                Assert.Same(vm.FetchAndPruneCommand, items[3].Command);
                Assert.Same(vm.CleanUntrackedFilesCommand, items[4].Command);

                var tab = new RepositoryViewModel(vm, repo.Directory);
                vm.Tabs.Add(tab);
                await vm.Activate(tab);
                Assert.True(vm.CollectGarbageCommand.CanExecute(null));
                await vm.CollectGarbageCommand.ExecuteAsync(null);
                Assert.Contains(" gc", tab.CommandLog, StringComparison.Ordinal);
                Assert.DoesNotContain("--aggressive", tab.CommandLog, StringComparison.Ordinal);
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

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);

    private sealed class ChoiceDialogs : IDialogService
    {
        public bool Accept { get; set; }

        public string Title { get; private set; } = "";

        public string Message { get; private set; } = "";

        public string Confirm { get; private set; } = "";

        public Task<bool> ConfirmAsync(string title, string message, string confirm = "OK")
        {
            Title = title;
            Message = message;
            Confirm = confirm;
            return Task.FromResult(Accept);
        }

        public Task<string?> PickFolderAsync(string title, string? startDirectory = null) => Task.FromResult<string?>(null);

        public Task<string?> PickGitExecutableAsync() => Task.FromResult<string?>(null);

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

        public Task CopyAsync(string text) => Task.CompletedTask;
    }

    private sealed class MaintenanceHost(string git, ChoiceDialogs dialogs) : IWorkspaceHost
    {
        public IDialogService? Dialogs => dialogs;

        public GitProcessRunner Runner { get; } = new();

        public string? GitExecutable => git;

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
