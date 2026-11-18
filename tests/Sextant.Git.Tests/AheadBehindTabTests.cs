using System.Diagnostics;
using Avalonia.Headless;
using Sextant.Git;
using Sextant.Git.Workspace;
using Sextant.ViewModels;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class AheadBehindTabTests
{
    [Fact]
    public async Task Switching_tabs_refreshes_ahead_behind_without_reloading_the_one_you_leave()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var origin = new TempRepo();
        origin.WriteFile("a.txt", "one\n");
        origin.CommitAll("base");
        var leftDir = Clone(origin);
        var rightDir = Clone(origin);
        var directory = Path.Combine(Path.GetTempPath(), "sextant-ahead-tabs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner())
                {
                    GitExecutable = origin.Git,
                    GitReady = true,
                };
                var left = new RepositoryViewModel(vm, leftDir);
                var right = new RepositoryViewModel(vm, rightDir);
                vm.Tabs.Add(left);
                vm.Tabs.Add(right);
                try
                {
                    await vm.Activate(left);
                    await vm.Activate(right);
                    await vm.Activate(left);
                    Assert.True(left.IsReady, left.Banner);
                    Assert.True(right.IsReady, right.Banner);
                    Assert.Equal("", left.AheadBehindText);
                    Assert.Equal("", right.AheadBehindText);
                    left.QuietWatcher();
                    right.QuietWatcher();

                    Commit(origin.Git, leftDir, "one");
                    Assert.Equal("", left.AheadBehindText);
                    Assert.DoesNotContain(left.Rows, row => row.Subject == "one");

                    await vm.Activate(right);
                    Assert.Same(right, vm.ActiveTab);
                    Assert.Equal("↑1  ↓0", left.AheadBehindText);
                    Assert.True(left.ShowAheadBehind);
                    Assert.Equal("", right.AheadBehindText);
                    Assert.False(left.IsBusy);
                    Assert.DoesNotContain(left.Rows, row => row.Subject == "one");

                    Commit(origin.Git, leftDir, "two");
                    Assert.Equal("↑1  ↓0", left.AheadBehindText);

                    await vm.Activate(left);
                    Assert.Equal("↑2  ↓0", left.AheadBehindText);
                    Assert.True(left.ShowAheadBehind);
                    Assert.Contains(left.Rows, row => row.Subject == "one");
                    Assert.Contains(left.Rows, row => row.Subject == "two");
                    Assert.Equal("", right.AheadBehindText);
                    Assert.False(right.ShowAheadBehind);
                }
                finally
                {
                    await vm.Shutdown();
                }
            }, CancellationToken.None);
        }
        finally
        {
            DeleteDirectory(leftDir);
            DeleteDirectory(rightDir);
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task Fetch_pull_and_push_update_the_tab_ahead_behind()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var origin = new TempRepo();
        origin.WriteFile("a.txt", "one\n");
        origin.CommitAll("base");
        origin.Run("config", "receive.denyCurrentBranch", "updateInstead");
        var cloneDir = Clone(origin);
        var directory = Path.Combine(Path.GetTempPath(), "sextant-ahead-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            origin.WriteFile("a.txt", "two\n");
            origin.CommitAll("second");
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner())
                {
                    GitExecutable = origin.Git,
                    GitReady = true,
                };
                var tab = new RepositoryViewModel(vm, cloneDir);
                vm.Tabs.Add(tab);
                try
                {
                    await vm.Activate(tab);
                    Assert.True(tab.IsReady, tab.Banner);
                    Assert.Equal("", tab.AheadBehindText);

                    await tab.Fetch();
                    Assert.False(tab.HasBanner, tab.Banner);
                    Assert.Equal("↑0  ↓1", tab.AheadBehindText);
                    Assert.True(tab.ShowAheadBehind);

                    await tab.Pull();
                    Assert.False(tab.HasBanner, tab.Banner);
                    Assert.Equal("", tab.AheadBehindText);
                    Assert.False(tab.ShowAheadBehind);

                    tab.QuietWatcher();
                    Commit(origin.Git, cloneDir, "local");
                    await tab.RefreshAheadBehindAsync();
                    Assert.Equal("↑1  ↓0", tab.AheadBehindText);
                    Assert.True(tab.ShowAheadBehind);

                    await tab.Push();
                    Assert.False(tab.HasBanner, tab.Banner);
                    Assert.Equal("", tab.AheadBehindText);
                    Assert.False(tab.ShowAheadBehind);
                }
                finally
                {
                    await vm.Shutdown();
                }
            }, CancellationToken.None);
        }
        finally
        {
            DeleteDirectory(cloneDir);
            DeleteDirectory(directory);
        }
    }

    private static void Commit(string git, string directory, string message)
    {
        File.WriteAllText(Path.Combine(directory, "a.txt"), message + "\n");
        Git(git, directory, "add", "-A");
        Git(git, directory, "commit", "-m", message);
    }

    private static void Git(string git, string directory, params string[] args)
    {
        var info = new ProcessStartInfo
        {
            FileName = git,
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(directory);
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        var error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} exited {process.ExitCode}{Environment.NewLine}{error}");
    }

    private static string Clone(TempRepo origin)
    {
        var clone = Path.Combine(Path.GetTempPath(), "sextant-ahead-clone-" + Guid.NewGuid().ToString("N"));
        origin.Run("clone", origin.Directory, clone);
        origin.SetIdentity(clone);
        return clone;
    }

    private static void DeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;
                foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                    entry.Attributes = FileAttributes.Normal;
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(40);
            }
        }
    }
}
