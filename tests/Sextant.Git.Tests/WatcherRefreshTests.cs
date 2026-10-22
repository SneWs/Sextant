using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Sextant.Git;
using Sextant.Services;
using Sextant.ViewModels;

namespace Sextant.Git.Tests;

public class GitDirectoryWatcherTests
{
    [Fact]
    public async Task Worktree_edit_reports_no_ref_change()
    {
        var root = CreateRoot();
        var git = Path.Combine(root, ".git");
        Directory.CreateDirectory(git);
        try
        {
            var refsChanged = await WatchAsync(git, root, () =>
                File.WriteAllText(Path.Combine(root, "note.txt"), "hello\n"));
            Assert.False(refsChanged);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Ref_update_reports_a_ref_change()
    {
        var root = CreateRoot();
        var git = Path.Combine(root, ".git");
        Directory.CreateDirectory(Path.Combine(git, "refs", "heads"));
        try
        {
            var refsChanged = await WatchAsync(git, root, () =>
                File.WriteAllText(Path.Combine(git, "refs", "heads", "main"), "abc\n"));
            Assert.True(refsChanged);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Common_dir_ref_update_reports_a_ref_change()
    {
        var root = CreateRoot();
        var git = Path.Combine(root, "wtgit");
        var common = Path.Combine(root, "common.git");
        var tree = Path.Combine(root, "tree");
        Directory.CreateDirectory(git);
        Directory.CreateDirectory(Path.Combine(common, "refs", "heads"));
        Directory.CreateDirectory(tree);
        File.WriteAllText(Path.Combine(git, "commondir"), "../common.git\n");
        try
        {
            var refsChanged = await WatchAsync(git, tree, () =>
                File.WriteAllText(Path.Combine(common, "refs", "heads", "main"), "abc\n"));
            Assert.True(refsChanged);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task<bool> WatchAsync(string gitDirectory, string workTree, Action change)
    {
        var ready = 0;
        var hits = 0;
        var refs = 0;
        using var watcher = new GitDirectoryWatcher(gitDirectory, refsChanged =>
        {
            if (Volatile.Read(ref ready) != 1)
                return;
            if (refsChanged)
                Interlocked.Exchange(ref refs, 1);
            Interlocked.Increment(ref hits);
        }, workTree);
        // The watcher debounces for 200ms. Ignore anything raised while it attaches.
        await Task.Delay(400);
        Volatile.Write(ref ready, 1);
        change();
        var deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline && Volatile.Read(ref hits) == 0)
            await Task.Delay(50);
        Assert.True(Volatile.Read(ref hits) > 0, "Watcher did not report the change.");
        await Task.Delay(300);
        return Volatile.Read(ref refs) == 1;
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "sextant-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(40);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(40);
            }
        }
    }
}

[Collection(HeadlessCollection.Name)]
public class WatcherRefreshTests
{
    [Fact]
    public async Task Editor_save_appears_without_activating_the_window()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new WatchHost(repo.Git), repo.Directory);
            Window? window = null;
            try
            {
                await vm.EnsureLoadedAsync();
                var working = vm.Rows.Single(row => row.IsWorkingCopy);
                Assert.Equal("Clean", working.Detail);
                window = new Window
                {
                    Width = 400,
                    Height = 300,
                    Content = new TextBlock { Text = "sextant" },
                };
                window.Show();

                repo.WriteFile("a.txt", "two\n");
                var appeared = await UntilAsync(() =>
                    vm.Files.Any(row => row.IsFile && row.Path == "a.txt"));
                working = vm.Rows.Single(row => row.IsWorkingCopy);
                Assert.True(appeared, "Timed out waiting for the edit. Detail: " + working.Detail);
                Assert.Contains("unstaged", working.Detail, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                window?.Close();
                await vm.DisposeAsync();
            }

            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task External_commit_appears_without_activating_the_window()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new WatchHost(repo.Git), repo.Directory);
            Window? window = null;
            try
            {
                await vm.EnsureLoadedAsync();
                Assert.DoesNotContain(vm.Rows, row => row.Subject == "second");
                window = new Window
                {
                    Width = 400,
                    Height = 300,
                    Content = new TextBlock { Text = "sextant" },
                };
                window.Show();

                repo.WriteFile("b.txt", "two\n");
                repo.CommitAll("second");
                var appeared = await UntilAsync(() => vm.Rows.Any(row => row.Subject == "second"));
                Assert.True(appeared, "Timed out waiting for the external commit.");
            }
            finally
            {
                window?.Close();
                await vm.DisposeAsync();
            }

            return 0;
        }, CancellationToken.None);
    }

    private static async Task<bool> UntilAsync(Func<bool> ready)
    {
        var deadline = Environment.TickCount64 + 8000;
        while (Environment.TickCount64 < deadline)
        {
            if (ready())
                return true;
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            await Task.Delay(50);
        }

        return ready();
    }

    private sealed class WatchHost(string git) : IWorkspaceHost
    {
        public IDialogService? Dialogs => null;

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
