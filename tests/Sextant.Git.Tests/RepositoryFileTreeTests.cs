using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class RepositoryFileTreeTests
{
    [Fact]
    public async Task Files_tab_is_lazy_virtualized_and_keeps_history_and_directory_expansion()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        await using var server = new LocalLfsLockServer([]);
        repo.Run("config", "lfs.url", server.Url);
        repo.WriteFile("docs/nested/readme.txt", "tracked\n");
        repo.WriteFile("root.txt", "root\n");
        repo.WriteFile(".gitignore", "ignored/\n");
        repo.CommitAll("first");
        repo.WriteFile("docs/untracked.txt", "new\n");
        repo.WriteFile("ignored/cache.txt", "ignored\n");
        for (var i = 0; i < 1000; i++)
            repo.WriteFile($"many/file-{i:D4}.txt", "new\n");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new TreeHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            Assert.True(vm.HistoryTabOn);
            Assert.Empty(vm.RepositoryFileTree);
            Assert.DoesNotContain("ls-files", vm.CommandLog, StringComparison.Ordinal);
            var selected = vm.SelectedGraphRow;
            var history = vm.Rows.ToArray();
            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var historyButton = view.FindControl<Button>("HistoryTabButton")!;
            var filesButton = view.FindControl<Button>("RepositoryFilesTabButton")!;
            Assert.Contains("selected", historyButton.Classes);
            await vm.ShowRepositoryFilesTabCommand.ExecuteAsync(null);
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.RepositoryFilesTabOn);
            Assert.Contains("selected", filesButton.Classes);
            Assert.DoesNotContain("selected", historyButton.Classes);
            Assert.False(view.FindControl<ListBox>("GraphList")!.IsEffectivelyVisible);
            Assert.Equal(history, vm.Rows);
            Assert.Same(selected, vm.SelectedGraphRow);
            var commandLog = vm.CommandLog;
            await vm.ShowRepositoryFilesTabCommand.ExecuteAsync(null);
            Assert.Equal(commandLog, vm.CommandLog);
            Assert.DoesNotContain(vm.RepositoryFileTree, item => item.Path.StartsWith("ignored", StringComparison.Ordinal));
            Assert.Equal(["docs", "many"], vm.RepositoryFileTree.Where(item => item.IsDirectory).Select(item => item.Path));

            var docs = vm.RepositoryFileTree.Single(item => item.Path == "docs");
            Assert.False(docs.IsExpanded);
            vm.ToggleRepositoryDirectory(docs);
            Assert.Equal(["docs", "docs/nested", "docs/untracked.txt", "many", ".gitignore", "root.txt"],
                vm.RepositoryFileTree.Select(item => item.Path));
            var nested = vm.RepositoryFileTree.Single(item => item.Path == "docs/nested");
            vm.ToggleRepositoryDirectory(nested);
            var file = vm.RepositoryFileTree.Single(item => item.Path == "docs/nested/readme.txt");
            Assert.Equal(2, file.Depth);
            Assert.False(file.LfsTracked);
            Assert.Equal("Git", file.StorageText);
            Assert.Contains("not tracked with Git LFS", file.Tip, StringComparison.Ordinal);
            Assert.Equal(file.LocksKnown, file.ShowLock);
            Assert.False(file.ShowUnlock);
            vm.SelectedRepositoryFile = file;
            vm.ToggleRepositoryDirectory(vm.RepositoryFileTree.Single(item => item.Path == "many"));
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var list = view.FindControl<ListBox>("RepositoryFileList")!;
            Assert.True(list.IsEffectivelyVisible);
            Assert.True(list.GetVisualDescendants().OfType<ListBoxItem>().Count() < 100,
                "The repository file tree must realize only the visible rows.");
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "readme.txt");
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Git");
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), button => button.Content as string == "Refresh");

            repo.WriteFile("docs/another.txt", "new after refresh\n");
            await vm.RefreshRepositoryFilesCommand.ExecuteAsync(null);
            Assert.True(vm.RepositoryFileTree.Single(item => item.Path == "docs").IsExpanded);
            Assert.True(vm.RepositoryFileTree.Single(item => item.Path == "docs/nested").IsExpanded);
            Assert.Equal("docs/nested/readme.txt", vm.SelectedRepositoryFile?.Path);
            Assert.Contains(vm.RepositoryFileTree, item => item.Path == "docs/another.txt");
            Assert.Contains("lfs locks", vm.CommandLog, StringComparison.Ordinal);

            await vm.RepositoryFileTree.Single(item => item.Path == "docs/nested/readme.txt").HistoryCommand.ExecuteAsync(null);
            Assert.True(vm.HistoryTabOn);
            Assert.True(vm.HasHistoryQuery);
            Assert.Contains(vm.Rows, row => row.Subject == "first");
            await vm.ShowAllCommitsCommand.ExecuteAsync(null);
            await vm.ShowRepositoryFilesTabCommand.ExecuteAsync(null);
            historyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(vm.HistoryTabOn);
            Assert.Equal(history.Select(row => row.Subject), vm.Rows.Select(row => row.Subject));
            vm.ShowHistorySearch = true;
            await vm.ShowRepositoryFilesTabCommand.ExecuteAsync(null);
            Assert.False(vm.ShowHistoryChrome);
            vm.ToggleHistorySearchCommand.Execute(null);
            Assert.True(vm.HistoryTabOn);
            Assert.True(vm.ShowHistorySearch);
            Assert.True(vm.ShowHistoryChrome);

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Unavailable_lfs_locks_leave_files_visible_and_do_not_claim_they_are_unlocked()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.Run("config", "lfs.url", "file://" + repo.Directory.Replace('\\', '/'));
        repo.WriteFile(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        repo.WriteFile("asset.bin", "version https://git-lfs.github.com/spec/v1\noid sha256:" + new string('a', 64) + "\nsize 4\n");
        repo.WriteFile("plain.txt", "plain\n");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new TreeHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            await vm.ShowRepositoryFilesTabCommand.ExecuteAsync(null);
            var file = vm.RepositoryFileTree.Single(item => item.Path == "asset.bin");
            Assert.True(file.LfsTracked);
            Assert.Equal("LFS", file.StorageText);
            Assert.Equal("", file.LockText);
            Assert.False(file.LocksKnown);
            Assert.False(file.ShowLock);
            Assert.False(file.ShowUnlock);
            Assert.True(vm.HasBanner);
            Assert.Contains(vm.RepositoryFileTree, item => item.Path == "plain.txt");
            Assert.False(vm.IsBusy);
            Assert.DoesNotContain("lock status unavailable", file.Tip, StringComparison.Ordinal);
            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var message = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == vm.Banner);
            var messagePoint = message.TranslatePoint(default, view);
            var files = view.FindControl<ListBox>("RepositoryFileList")!;
            var filesPoint = files.TranslatePoint(default, view);
            Assert.NotNull(messagePoint);
            Assert.NotNull(filesPoint);
            Assert.True(messagePoint.Value.Y < filesPoint.Value.Y);
            Assert.DoesNotContain(files.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == vm.Banner);
            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task File_menu_places_force_unlock_below_unlock_and_shows_the_lock_owner()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("root.txt", "root\n");
        repo.CommitAll("first");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new TreeHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            await vm.ShowRepositoryFilesTabCommand.ExecuteAsync(null);
            var desktopMenu = new WorktreeFileMenu { OpenFolderLabel = DesktopOpen.FolderLabel(DesktopOpen.Current) };
            var locked = new RepositoryFileTreeItem
            {
                Path = "locked.bin", Label = "locked.bin", LfsTracked = true, LocksKnown = true,
                Lock = new LfsLock("123", "locked.bin", "Teammate"),
                FileMenuFactory = () => desktopMenu,
            };
            var unlocked = new RepositoryFileTreeItem
            {
                Path = "unlocked.bin", Label = "unlocked.bin", LfsTracked = true, LocksKnown = true,
                FileMenuFactory = () => desktopMenu,
            };
            var plain = new RepositoryFileTreeItem
            {
                Path = "plain.txt", Label = "plain.txt", LocksKnown = true,
            };
            Assert.True(plain.ShowLock);
            Assert.False(plain.LfsTracked);
            Assert.False(unlocked.HasLockInfo);
            Assert.False(plain.HasLockInfo);
            Assert.True(locked.HasLockInfo);
            vm.RepositoryFileTree.Reset([locked, unlocked, plain]);
            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var list = view.FindControl<ListBox>("RepositoryFileList")!;
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Locked by Teammate");
            Assert.DoesNotContain(list.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Unlocked");
            var border = list.GetVisualDescendants().OfType<Border>()
                .Single(item => item.DataContext == locked && item.ContextMenu is not null);
            var menu = border.ContextMenu!;
            menu.Open(border);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Unlock file", "Force Unlock file", "History", desktopMenu.OpenFolderLabel, "Open in editor", "Remove file…"],
                menu.Items.OfType<MenuItem>().Where(item => item.IsVisible).Select(item => item.Header as string));
            Assert.Same(desktopMenu.OpenEditorCommand, menu.Items.OfType<MenuItem>().Single(item => item.Header as string == "Open in editor").Command);
            Assert.Same(desktopMenu.OpenFolderCommand, menu.Items.OfType<MenuItem>().Single(item => item.Header as string == desktopMenu.OpenFolderLabel).Command);
            menu.Close();
            border = list.GetVisualDescendants().OfType<Border>()
                .Single(item => item.DataContext == unlocked && item.ContextMenu is not null);
            menu = border.ContextMenu!;
            menu.Open(border);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Lock file", "History", desktopMenu.OpenFolderLabel, "Open in editor", "Remove file…"],
                menu.Items.OfType<MenuItem>().Where(item => item.IsVisible).Select(item => item.Header as string));
            menu.Close();
            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task File_commands_refresh_locks_and_force_unlock_requires_confirmation()
    {
        if (!LfsLockTests.GitLfsInstalled())
            return;
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await using var server = new LocalLfsLockServer([new LfsLock("other-1", "other.bin", "Teammate")]);
        using var repo = LfsLockTests.CreateLfsRepo(server, ["own.bin", "other.bin", "plain.txt"]);

        await session.Dispatch(async () =>
        {
            var dialogs = new FileActionDialogs();
            var vm = new RepositoryViewModel(new TreeHost(repo.Git, dialogs), repo.Directory);
            await vm.EnsureLoadedAsync();
            await vm.ShowRepositoryFilesTabCommand.ExecuteAsync(null);
            var own = vm.RepositoryFileTree.Single(item => item.Path == "own.bin");
            Assert.Equal("", own.LockText);
            Assert.True(own.LockCommand.CanExecute(null));
            await own.LockCommand.ExecuteAsync(null);
            Assert.False(vm.HasBanner, vm.Banner);
            own = vm.RepositoryFileTree.Single(item => item.Path == "own.bin");
            Assert.Equal("Locked by Test", own.LockText);
            Assert.True(own.ShowUnlock);
            Assert.False(own.ShowLock);
            await own.UnlockCommand.ExecuteAsync(null);
            Assert.False(vm.HasBanner, vm.Banner);
            Assert.Equal("", vm.RepositoryFileTree.Single(item => item.Path == "own.bin").LockText);

            var plain = vm.RepositoryFileTree.Single(item => item.Path == "plain.txt");
            Assert.False(plain.LfsTracked);
            Assert.True(plain.LockCommand.CanExecute(null));
            await plain.LockCommand.ExecuteAsync(null);
            Assert.False(vm.HasBanner, vm.Banner);
            plain = vm.RepositoryFileTree.Single(item => item.Path == "plain.txt");
            Assert.Equal("Locked by Test", plain.LockText);
            await plain.UnlockCommand.ExecuteAsync(null);
            Assert.False(vm.HasBanner, vm.Banner);
            Assert.Equal("", vm.RepositoryFileTree.Single(item => item.Path == "plain.txt").LockText);

            var other = vm.RepositoryFileTree.Single(item => item.Path == "other.bin");
            Assert.Equal("Locked by Teammate", other.LockText);
            var before = server.Requests.Count;
            dialogs.Accept = false;
            dialogs.BeforeConfirm = vm.RefreshFromFocusAsync;
            await other.ForceUnlockCommand.ExecuteAsync(null);
            Assert.Equal(before, server.Requests.Count);
            Assert.Equal("Locked by Teammate", vm.RepositoryFileTree.Single(item => item.Path == "other.bin").LockText);
            Assert.Contains("Teammate", dialogs.Message, StringComparison.Ordinal);
            Assert.Contains("other.bin", dialogs.Message, StringComparison.Ordinal);

            await other.UnlockCommand.ExecuteAsync(null);
            Assert.True(vm.HasBanner);
            Assert.Equal("", vm.RepositoryFileTree.Single(item => item.Path == "other.bin").LockText);
            Assert.False(vm.RepositoryFileTree.Single(item => item.Path == "other.bin").LocksKnown);
            await vm.RefreshRepositoryFilesCommand.ExecuteAsync(null);
            Assert.Equal("Locked by Teammate", vm.RepositoryFileTree.Single(item => item.Path == "other.bin").LockText);

            dialogs.Accept = true;
            await vm.RepositoryFileTree.Single(item => item.Path == "other.bin").ForceUnlockCommand.ExecuteAsync(null);
            Assert.False(vm.HasBanner, vm.Banner);
            Assert.Equal("", vm.RepositoryFileTree.Single(item => item.Path == "other.bin").LockText);
            Assert.Contains(server.Requests, request => request.Route == "locks/other-1/unlock" && request.Force);
            Assert.Equal(2, dialogs.Confirmations);

            server.ListStatus = 500;
            await vm.RefreshRepositoryFilesCommand.ExecuteAsync(null);
            Assert.True(vm.HasBanner);
            Assert.All(vm.RepositoryFileTree.Where(item => item.IsFile), item =>
            {
                Assert.Equal("", item.LockText);
                Assert.False(item.LocksKnown);
                Assert.False(item.LockCommand.CanExecute(null));
                Assert.False(item.UnlockCommand.CanExecute(null));
            });
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Repository_with_only_ordinary_git_files_loads_locks_and_can_lock_and_unlock()
    {
        if (!LfsLockTests.GitLfsInstalled())
            return;
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await using var server = new LocalLfsLockServer([new LfsLock("own-1", "locked.txt", "Test")]);
        using var repo = LfsLockTests.CreateLfsRepo(server, ["locked.txt", "plain.txt"]);

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new TreeHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            await vm.ShowRepositoryFilesTabCommand.ExecuteAsync(null);
            Assert.False(vm.HasBanner, vm.Banner);
            Assert.All(vm.RepositoryFileTree, item => Assert.False(item.LfsTracked));
            var locked = vm.RepositoryFileTree.Single(item => item.Path == "locked.txt");
            Assert.True(locked.HasLockInfo);
            Assert.Equal("Locked by Test", locked.LockText);
            Assert.True(locked.UnlockCommand.CanExecute(null));
            await locked.UnlockCommand.ExecuteAsync(null);
            Assert.False(vm.HasBanner, vm.Banner);
            Assert.False(vm.RepositoryFileTree.Single(item => item.Path == "locked.txt").HasLockInfo);

            var plain = vm.RepositoryFileTree.Single(item => item.Path == "plain.txt");
            Assert.True(plain.LockCommand.CanExecute(null));
            await plain.LockCommand.ExecuteAsync(null);
            Assert.False(vm.HasBanner, vm.Banner);
            plain = vm.RepositoryFileTree.Single(item => item.Path == "plain.txt");
            Assert.True(plain.HasLockInfo);
            Assert.Equal("Locked by Test", plain.LockText);
            Assert.True(plain.ShowUnlock);
            await plain.UnlockCommand.ExecuteAsync(null);
            Assert.False(vm.HasBanner, vm.Banner);
            Assert.False(vm.RepositoryFileTree.Single(item => item.Path == "plain.txt").HasLockInfo);
            Assert.Contains(server.Requests, request => request.Method == "GET" && request.Route == "locks");
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Lock_loading_message_appears_in_the_branch_bar_not_above_the_file_tree()
    {
        if (!LfsLockTests.GitLfsInstalled())
            return;
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await using var server = new LocalLfsLockServer([]);
        using var repo = LfsLockTests.CreateLfsRepo(server, ["plain.txt"]);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.BeforeList = token =>
        {
            requested.TrySetResult();
            return release.Task.WaitAsync(token);
        };

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new TreeHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            var loading = vm.ShowRepositoryFilesTabCommand.ExecuteAsync(null);
            try
            {
                await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
                view.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.True(vm.IsBusy);
                Assert.Equal("Loading LFS locks…", vm.BusyText);
                Assert.False(vm.HasBanner, vm.Banner);
                var message = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == "Loading LFS locks…");
                var messagePoint = message.TranslatePoint(default, view);
                var filesPoint = view.FindControl<ListBox>("RepositoryFileList")!.TranslatePoint(default, view);
                Assert.NotNull(messagePoint);
                Assert.NotNull(filesPoint);
                Assert.True(messagePoint.Value.Y < filesPoint.Value.Y);
            }
            finally
            {
                release.TrySetResult();
                await loading;
                window.Close();
                await vm.DisposeAsync();
            }
            Assert.False(vm.IsBusy);
            Assert.Equal("", vm.BusyText);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Returning_to_repository_files_or_the_window_refreshes_files_and_remote_locks()
    {
        if (!LfsLockTests.GitLfsInstalled())
            return;
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await using var server = new LocalLfsLockServer([]);
        using var repo = LfsLockTests.CreateLfsRepo(server, ["docs/plain.txt"]);
        using var other = new TempRepo();
        other.WriteFile("other.txt", "another repository\n");
        other.CommitAll("other");

        await session.Dispatch(async () =>
        {
            var store = new WorkspaceStore(Path.Combine(repo.Directory, ".git", "test-workspace"));
            var main = new MainViewModel(store, new WorkspaceState(),
                new AppSettings { GitExecutable = repo.Git, ReopenTabs = false }, new GitProcessRunner());
            await main.InitializeAsync();
            var first = new RepositoryViewModel(main, repo.Directory);
            var second = new RepositoryViewModel(main, other.Directory);
            main.Tabs.Add(first);
            main.Tabs.Add(second);
            try
            {
                main.Activate(first);
                await first.EnsureLoadedAsync();
                await WaitUntilIdle(first);
                await first.ShowRepositoryFilesTabCommand.ExecuteAsync(null);
                first.ToggleRepositoryDirectory(first.RepositoryFileTree.Single(item => item.Path == "docs"));
                first.SelectedRepositoryFile = first.RepositoryFileTree.Single(item => item.Path == "docs/plain.txt");

                main.Activate(second);
                await second.EnsureLoadedAsync();
                await WaitUntilIdle(second);
                await using var external = await RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
                await external.LockLfsFileAsync("docs/plain.txt", CancellationToken.None);
                repo.WriteFile("docs/new.txt", "new while away\n");
                var requests = server.Requests.Count(request => request.Method == "GET");
                main.Activate(first);
                await WaitUntilIdle(first);
                Assert.False(first.HasBanner, first.Banner);
                Assert.True(server.Requests.Count(request => request.Method == "GET") > requests);
                Assert.Equal("Locked by Test", first.RepositoryFileTree.Single(item => item.Path == "docs/plain.txt").LockText);
                Assert.Contains(first.RepositoryFileTree, item => item.Path == "docs/new.txt");
                Assert.True(first.RepositoryFileTree.Single(item => item.Path == "docs").IsExpanded);
                Assert.Equal("docs/plain.txt", first.SelectedRepositoryFile?.Path);

                await external.UnlockLfsFileAsync("docs/plain.txt", force: false, CancellationToken.None);
                requests = server.Requests.Count(request => request.Method == "GET");
                main.OnWindowActivated();
                await WaitUntilIdle(first);
                Assert.False(first.HasBanner, first.Banner);
                Assert.True(server.Requests.Count(request => request.Method == "GET") > requests);
                Assert.False(first.RepositoryFileTree.Single(item => item.Path == "docs/plain.txt").HasLockInfo);
                Assert.Equal("docs/plain.txt", first.SelectedRepositoryFile?.Path);

                first.ShowHistoryTabCommand.Execute(null);
                requests = server.Requests.Count(request => request.Method == "GET");
                main.OnWindowActivated();
                await WaitUntilIdle(first);
                Assert.Equal(requests, server.Requests.Count(request => request.Method == "GET"));
                await external.LockLfsFileAsync("docs/plain.txt", CancellationToken.None);
                await first.ShowRepositoryFilesTabCommand.ExecuteAsync(null);
                Assert.False(first.HasBanner, first.Banner);
                Assert.Equal("Locked by Test", first.RepositoryFileTree.Single(item => item.Path == "docs/plain.txt").LockText);
            }
            finally
            {
                main.Shutdown();
            }
        }, CancellationToken.None);
    }

    private static async Task WaitUntilIdle(RepositoryViewModel tab)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (tab.IsBusy && DateTime.UtcNow < until)
            await Task.Delay(20);
        Assert.False(tab.IsBusy);
    }

    [Fact]
    public async Task Remove_file_requires_confirmation_stages_deletion_and_refreshes_the_tree()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.Run("config", "lfs.url", "file://" + repo.Directory.Replace('\\', '/'));
        repo.WriteFile("remove.txt", "committed\n");
        repo.WriteFile("keep.txt", "keep\n");
        repo.CommitAll("base");
        repo.WriteFile("remove.txt", "uncommitted\n");

        await session.Dispatch(async () =>
        {
            var dialogs = new FileActionDialogs("Remove file", "Remove");
            var vm = new RepositoryViewModel(new TreeHost(repo.Git, dialogs), repo.Directory);
            await vm.EnsureLoadedAsync();
            await vm.ShowRepositoryFilesTabCommand.ExecuteAsync(null);
            var row = vm.RepositoryFileTree.Single(item => item.Path == "remove.txt");
            Assert.NotNull(row.FileMenu);
            Assert.True(row.FileMenu.OpenEditorCommand.CanExecute(null));
            Assert.True(row.FileMenu.OpenFolderCommand.CanExecute(null));
            Assert.Equal(DesktopOpen.FolderLabel(DesktopOpen.Current), row.FileMenu.OpenFolderLabel);
            dialogs.BeforeConfirm = vm.RefreshFromFocusAsync;
            var before = vm.CommandLog;
            await row.RemoveCommand.ExecuteAsync(null);
            Assert.Equal(before, vm.CommandLog);
            Assert.True(File.Exists(Path.Combine(repo.Directory, "remove.txt")));
            Assert.Contains("remove.txt", repo.RunCapture("ls-files"), StringComparison.Ordinal);

            dialogs.Accept = true;
            await row.RemoveCommand.ExecuteAsync(null);
            Assert.Contains("remove.txt", dialogs.Message, StringComparison.Ordinal);
            Assert.Contains("Uncommitted changes will be lost", dialogs.Message, StringComparison.Ordinal);
            Assert.False(vm.HasBanner, vm.Banner);
            Assert.False(File.Exists(Path.Combine(repo.Directory, "remove.txt")));
            Assert.True(File.Exists(Path.Combine(repo.Directory, "keep.txt")));
            Assert.DoesNotContain(vm.RepositoryFileTree, item => item.Path == "remove.txt");
            Assert.Contains(vm.Files, item => item.Path == "remove.txt" && item.Kind == ChangeKind.Deleted && item.FromStagedList);
            Assert.Equal(2, dialogs.Confirmations);
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    private sealed class TreeHost(string git, IDialogService? dialogs = null) : IWorkspaceHost
    {
        public IDialogService? Dialogs => dialogs;
        public GitProcessRunner Runner { get; } = new();
        public string? GitExecutable => git;
        public string? MergeTool => null;
        public bool GitReady => true;
        public void Activate(RepositoryViewModel tab) { }
        public void Close(RepositoryViewModel tab) { }
        public void NoteLoaded(RepositoryViewModel tab) { }
        public void Save() { }
        public Task OpenRepositoryAsync(string path) => Task.CompletedTask;
    }

    private sealed class FileActionDialogs(string expectedTitle = "Force Unlock file", string expectedConfirm = "Force Unlock") : IDialogService
    {
        public bool Accept { get; set; }
        public string Message { get; private set; } = "";
        public int Confirmations { get; private set; }
        public Func<Task>? BeforeConfirm { get; set; }
        public async Task<bool> ConfirmAsync(string title, string message, string confirm = "OK")
        {
            Assert.Equal(expectedTitle, title);
            Assert.Equal(expectedConfirm, confirm);
            Message = message;
            Confirmations++;
            if (BeforeConfirm is not null)
                await BeforeConfirm();
            return Accept;
        }
        public Task<string?> PickFolderAsync(string title) => throw new NotSupportedException();
        public Task<string?> PickGitExecutableAsync() => throw new NotSupportedException();
        public Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false) => throw new NotSupportedException();
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
}
