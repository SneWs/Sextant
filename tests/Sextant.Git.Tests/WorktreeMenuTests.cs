using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;
using Sextant.Git.Models;
using Sextant.Git.Repo;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class WorktreeMenuTests
{
    [Fact]
    public async Task A_linked_worktree_menu_can_lock_move_and_remove_it()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("keep.txt", "keep\n");
        repo.CommitAll("keep");
        var extra = Path.Combine(Path.GetTempPath(), "sextant-wt-" + Guid.NewGuid().ToString("N"));
        var destination = extra + "-moved";
        repo.Run("worktree", "add", "-b", "feature", extra);
        var dialogs = new WorktreeDialogs();

        try
        {
            await session.Dispatch(async () =>
            {
                var vm = new RepositoryViewModel(new MenuHost(repo.Git, dialogs), repo.Directory);
                await vm.EnsureLoadedAsync();
                var main = vm.Locations.Single(item => item.Key.StartsWith("w:", StringComparison.Ordinal) && item.IsCurrent);
                var linked = vm.Locations.Single(item => item.ShowRemove);
                Assert.False(main.ShowRemove);
                Assert.False(main.ShowLock);
                Assert.False(main.ShowMove);
                Assert.True(linked.ShowOpen);
                Assert.True(linked.ShowLock);
                Assert.True(linked.ShowMove);
                Assert.False(linked.ShowUnlock);

                var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 900 };
                var window = new Window { Content = view, Width = 1100, Height = 900 };
                window.Show();
                view.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                AssertVisibleMenu(view, linked, ["Open", "Remove worktree", "Lock worktree…", "Move worktree…"]);

                dialogs.Answers.Enqueue(false);
                await ((AsyncRelayCommand)linked.RemoveCommand).ExecuteAsync(null);
                Assert.True(Directory.Exists(extra));
                Assert.Equal(("Remove worktree", "Remove"), dialogs.Confirms[0]);

                dialogs.Prompts.Enqueue("away");
                await ((AsyncRelayCommand)linked.LockCommand).ExecuteAsync(null);
                linked = vm.Locations.Single(item => item.ShowRemove);
                Assert.Contains("locked: away", linked.Label, StringComparison.Ordinal);
                Assert.True(linked.ShowUnlock);
                Assert.False(linked.ShowLock);
                Assert.False(linked.ShowMove);
                AssertVisibleMenu(view, linked, ["Open", "Remove worktree", "Unlock worktree"]);

                await ((AsyncRelayCommand)linked.UnlockCommand).ExecuteAsync(null);
                linked = vm.Locations.Single(item => item.ShowRemove);
                Assert.True(linked.ShowLock);
                Assert.True(linked.ShowMove);

                dialogs.Prompts.Enqueue(destination);
                await ((AsyncRelayCommand)linked.MoveCommand).ExecuteAsync(null);
                Assert.False(Directory.Exists(extra));
                Assert.True(File.Exists(Path.Combine(destination, "keep.txt")));
                linked = vm.Locations.Single(item => item.ShowRemove);
                Assert.Contains(destination, linked.Key, StringComparison.OrdinalIgnoreCase);

                dialogs.Answers.Enqueue(true);
                await ((AsyncRelayCommand)linked.RemoveCommand).ExecuteAsync(null);
                Assert.False(Directory.Exists(destination));
                Assert.DoesNotContain(vm.Locations, item => item.ShowRemove);
                Assert.Contains("feature", repo.RunCapture("branch", "--list"), StringComparison.Ordinal);

                window.Close();
                await vm.DisposeAsync();
            }, CancellationToken.None);
        }
        finally
        {
            DeleteWorktree(repo, extra);
            DeleteWorktree(repo, destination);
        }
    }

    [Fact]
    public async Task Removing_a_dirty_worktree_asks_again_before_deleting_changes()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("keep.txt", "keep\n");
        repo.CommitAll("keep");
        var extra = Path.Combine(Path.GetTempPath(), "sextant-wt-" + Guid.NewGuid().ToString("N"));
        repo.Run("worktree", "add", "-b", "feature", extra);
        File.WriteAllText(Path.Combine(extra, "dirty.txt"), "dirty\n");
        var dialogs = new WorktreeDialogs();

        try
        {
            await session.Dispatch(async () =>
            {
                var vm = new RepositoryViewModel(new MenuHost(repo.Git, dialogs), repo.Directory);
                await vm.EnsureLoadedAsync();
                var linked = vm.Locations.Single(item => item.ShowRemove);

                dialogs.Answers.Enqueue(true);
                dialogs.Answers.Enqueue(false);
                await ((AsyncRelayCommand)linked.RemoveCommand).ExecuteAsync(null);
                Assert.True(File.Exists(Path.Combine(extra, "dirty.txt")));
                Assert.Equal(2, dialogs.Confirms.Count);
                Assert.Contains("changes", dialogs.Message, StringComparison.OrdinalIgnoreCase);

                dialogs.Answers.Enqueue(true);
                dialogs.Answers.Enqueue(true);
                await ((AsyncRelayCommand)linked.RemoveCommand).ExecuteAsync(null);
                Assert.False(Directory.Exists(extra));
                Assert.Contains("feature", repo.RunCapture("branch", "--list"), StringComparison.Ordinal);

                await vm.DisposeAsync();
            }, CancellationToken.None);
        }
        finally
        {
            DeleteWorktree(repo, extra);
        }
    }

    private static void AssertVisibleMenu(RepositoryView view, LocationItem row, string[] headers)
    {
        var list = view.FindControl<ListBox>("LocationList");
        Assert.NotNull(list);
        list.ScrollIntoView(row);
        view.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var border = list.GetVisualDescendants().OfType<Border>()
            .Single(item => item.DataContext == row && item.ContextMenu is not null);
        var menu = border.ContextMenu!;
        menu.Open(border);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(headers, menu.Items.OfType<MenuItem>().Where(item => item.IsVisible).Select(item => item.Header as string));
        menu.Close();
    }

    private static void DeleteWorktree(TempRepo repo, string path)
    {
        try
        {
            if (Directory.Exists(path))
                repo.Run("worktree", "remove", "--force", "--force", path);
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class WorktreeDialogs : IDialogService
    {
        public Queue<bool> Answers { get; } = new();

        public Queue<string?> Prompts { get; } = new();

        public List<(string Title, string Confirm)> Confirms { get; } = [];

        public string Message { get; private set; } = "";

        public Task<bool> ConfirmAsync(string title, string message, string confirm = "OK")
        {
            Confirms.Add((title, confirm));
            Message = message;
            return Task.FromResult(Answers.Count > 0 && Answers.Dequeue());
        }

        public Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false) =>
            Task.FromResult(Prompts.Count > 0 ? Prompts.Dequeue() : null);

        public Task<string?> PickFolderAsync(string title, string? startDirectory = null) => throw new NotSupportedException();

        public Task<string?> PickGitExecutableAsync() => throw new NotSupportedException();

        public Task<string?> PromptSecretAsync(string title, string message) => throw new NotSupportedException();

        public Task<string?> SaveFileAsync(string title, string suggestedName) => throw new NotSupportedException();

        public Task<string?> PickFileAsync(string title, string typeName, IReadOnlyList<string> patterns) => throw new NotSupportedException();

        public Task<CloneRequest?> PromptCloneAsync() => throw new NotSupportedException();

        public Task<string?> PickAsync(string title, string message, IReadOnlyList<string> options) => throw new NotSupportedException();

        public Task<PerformanceChoice?> ConfirmPerformanceAsync(PerformanceSuggestion suggestion) => throw new NotSupportedException();

        public Task<IReadOnlyList<RebaseStep>?> EditRebaseAsync(IReadOnlyList<RebaseStep> steps) => throw new NotSupportedException();

        public Task<SettingsDraft?> EditSettingsAsync(SettingsDraft current) => throw new NotSupportedException();

        public Task ShowAboutAsync() => throw new NotSupportedException();

        public Task CopyAsync(string text) => throw new NotSupportedException();
    }

    private sealed class MenuHost(string git, IDialogService dialogs) : IWorkspaceHost
    {
        public IDialogService? Dialogs => dialogs;

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
