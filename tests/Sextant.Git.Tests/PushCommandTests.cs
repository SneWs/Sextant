using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.Git;
using Sextant.Git.Models;
using Sextant.Git.Workspace;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class PushCommandTests
{
    [Fact]
    public async Task Push_runs_only_when_origin_is_missing_local_commits()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        var (origin, bare, clone) = CreateClone();
        try
        {
            await session.Dispatch(async () =>
            {
                var dialogs = new PushDialogs("origin");
                var (vm, tab) = await Open(clone, origin.Git, dialogs);
                var view = new RepositoryView { DataContext = tab, Width = 1100, Height = 700 };
                var window = new Window { Content = view, Width = 1100, Height = 700 };
                try
                {
                    window.Show();
                    view.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();

                    Assert.False(tab.CanPush);
                    Assert.False(vm.CanPush);
                    Assert.False(vm.PushCommand.CanExecute(null));
                    var push = view.GetVisualDescendants().OfType<SplitButton>().Single(button => button.Name == "PushSplitButton");
                    var primary = push.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_PrimaryButton");
                    Assert.True(push.IsEffectivelyEnabled);
                    Assert.False(primary.IsEffectivelyEnabled);
                    var flyout = Assert.IsType<MenuFlyout>(push.Flyout);
                    flyout.ShowAt(push);
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(flyout.Items.OfType<MenuItem>().Single(item => item.Header as string == "Push").IsEffectivelyEnabled);
                    Assert.False(flyout.Items.OfType<MenuItem>().Single(item => item.Header as string == "Push ignoring local checks").IsEffectivelyEnabled);
                    Assert.True(flyout.Items.OfType<MenuItem>().Single(item => item.Header as string == "Push with force-with-lease").IsEffectivelyEnabled);
                    flyout.Hide();

                    vm.TogglePalette();
                    Assert.DoesNotContain(vm.PaletteMatches, item => item.Title == "Push");
                    Assert.Contains(vm.PaletteMatches, item => item.Title == "Push with force-with-lease");
                    vm.ClosePalette();

                    var before = PushCount(tab.CommandLog);
                    await tab.Push();
                    await tab.PushIgnoringLocalChecksCommand.ExecuteAsync(null);
                    await vm.PushCommand.ExecuteAsync(null);
                    Assert.Equal(before, PushCount(tab.CommandLog));
                    Assert.Equal(0, dialogs.Picks);
                    Assert.Equal("second", Tip(origin.Git, bare, "HEAD"));

                    File.WriteAllText(Path.Combine(clone, "a.txt"), "local\n");
                    Git(origin.Git, clone, "add", "a.txt");
                    Git(origin.Git, clone, "commit", "-m", "local");
                    await tab.Refresh();
                    await WaitUntilIdle(tab);
                    view.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();

                    Assert.True(tab.CanPush);
                    Assert.True(vm.CanPush);
                    Assert.True(vm.PushCommand.CanExecute(null));
                    Assert.True(primary.IsEffectivelyEnabled);
                    vm.TogglePalette();
                    Assert.Contains(vm.PaletteMatches, item => item.Title == "Push");
                    vm.ClosePalette();

                    await tab.Push();
                    await WaitUntilIdle(tab);
                    Assert.Equal(0, dialogs.Picks);
                    Assert.Equal("local", Tip(origin.Git, bare, "HEAD"));
                    Assert.False(tab.CanPush);
                    Assert.False(vm.PushCommand.CanExecute(null));

                    var pushed = PushCount(tab.CommandLog);
                    Assert.Equal(1, pushed);
                    await tab.Push();
                    await vm.PushCommand.ExecuteAsync(null);
                    Assert.Equal(pushed, PushCount(tab.CommandLog));
                    Assert.Equal("local", Tip(origin.Git, bare, "HEAD"));
                }
                finally
                {
                    window.Close();
                    await vm.Shutdown();
                }

                return 0;
            }, CancellationToken.None);
        }
        finally
        {
            origin.Dispose();
            TryDelete(bare);
            TryDelete(clone);
        }
    }

    [Fact]
    public async Task Push_skips_a_branch_whose_commits_origin_already_has()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        var (origin, bare, clone) = CreateClone();
        try
        {
            Git(origin.Git, clone, "switch", "-c", "feature");
            await session.Dispatch(async () =>
            {
                var dialogs = new PushDialogs("origin");
                var (vm, tab) = await Open(clone, origin.Git, dialogs);
                try
                {
                    Assert.Equal("feature", tab.BranchText);
                    Assert.False(tab.CanPush);
                    var before = PushCount(tab.CommandLog);
                    await tab.Push();
                    Assert.Equal(before, PushCount(tab.CommandLog));
                    Assert.Equal(0, dialogs.Picks);
                    Assert.False(tab.HasBanner);

                    Git(origin.Git, clone, "reset", "--hard", "HEAD~1");
                    await WaitUntilIdle(tab);
                    await tab.Refresh();
                    await WaitUntilIdle(tab);

                    Assert.True(tab.CanPush);
                    await tab.Push();
                    await WaitUntilIdle(tab);
                    Assert.Equal(before, PushCount(tab.CommandLog));
                    Assert.Equal(0, dialogs.Picks);
                    Assert.Equal("Nothing to push. origin already has these commits.", tab.Banner);
                    Assert.Equal("second", Tip(origin.Git, bare, "HEAD"));
                }
                finally
                {
                    await vm.Shutdown();
                }

                return 0;
            }, CancellationToken.None);
        }
        finally
        {
            origin.Dispose();
            TryDelete(bare);
            TryDelete(clone);
        }
    }

    [Fact]
    public async Task Push_publishes_commits_that_are_not_on_origin_yet()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        var (origin, bare, clone) = CreateClone();
        try
        {
            Git(origin.Git, clone, "switch", "-c", "feature");
            File.WriteAllText(Path.Combine(clone, "c.txt"), "topic\n");
            Git(origin.Git, clone, "add", "c.txt");
            Git(origin.Git, clone, "commit", "-m", "topic");
            await session.Dispatch(async () =>
            {
                var dialogs = new PushDialogs("origin");
                var (vm, tab) = await Open(clone, origin.Git, dialogs);
                try
                {
                    Assert.Equal("feature", tab.BranchText);
                    Assert.True(tab.CanPush);
                    Assert.True(vm.CanPush);

                    await tab.Push();
                    await WaitUntilIdle(tab);

                    Assert.Equal(0, dialogs.Picks);
                    Assert.Equal(1, PushCount(tab.CommandLog));
                    Assert.Equal("topic", Tip(origin.Git, bare, "feature"));
                    Assert.Equal("origin/feature", Git(origin.Git, clone, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}").Trim());
                    Assert.False(tab.CanPush);

                    var pushed = PushCount(tab.CommandLog);
                    await tab.Push();
                    Assert.Equal(pushed, PushCount(tab.CommandLog));
                    Assert.Equal(0, dialogs.Picks);
                }
                finally
                {
                    await vm.Shutdown();
                }

                return 0;
            }, CancellationToken.None);
        }
        finally
        {
            origin.Dispose();
            TryDelete(bare);
            TryDelete(clone);
        }
    }

    [Fact]
    public async Task Push_asks_which_remote_when_several_exist()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        var (origin, bare, clone) = CreateClone();
        try
        {
            Git(origin.Git, clone, "remote", "add", "backup", bare);
            Git(origin.Git, clone, "switch", "-c", "feature");
            File.WriteAllText(Path.Combine(clone, "c.txt"), "topic\n");
            Git(origin.Git, clone, "add", "c.txt");
            Git(origin.Git, clone, "commit", "-m", "topic");
            await session.Dispatch(async () =>
            {
                var dialogs = new PushDialogs("backup");
                var (vm, tab) = await Open(clone, origin.Git, dialogs);
                try
                {
                    Assert.True(tab.CanPush);
                    await tab.Push();
                    await WaitUntilIdle(tab);

                    Assert.Equal(1, dialogs.Picks);
                    Assert.NotNull(dialogs.LastOptions);
                    Assert.Contains("origin", dialogs.LastOptions);
                    Assert.Contains("backup", dialogs.LastOptions);
                    Assert.Equal("topic", Tip(origin.Git, bare, "feature"));
                    Assert.Equal("backup/feature", Git(origin.Git, clone, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}").Trim());
                }
                finally
                {
                    await vm.Shutdown();
                }

                return 0;
            }, CancellationToken.None);
        }
        finally
        {
            origin.Dispose();
            TryDelete(bare);
            TryDelete(clone);
        }
    }

    private static (TempRepo Origin, string Bare, string Clone) CreateClone()
    {
        var origin = new TempRepo();
        origin.WriteFile("a.txt", "one\n");
        origin.CommitAll("first");
        origin.WriteFile("b.txt", "two\n");
        origin.CommitAll("second");
        var bare = Path.Combine(Path.GetTempPath(), "sextant-bare-" + Guid.NewGuid().ToString("N"));
        var clone = Path.Combine(Path.GetTempPath(), "sextant-clone-" + Guid.NewGuid().ToString("N"));
        origin.Run("clone", "--bare", origin.Directory, bare);
        origin.Run("clone", bare, clone);
        origin.SetIdentity(clone);
        return (origin, bare, clone);
    }

    private static async Task<(MainViewModel Vm, RepositoryViewModel Tab)> Open(string directory, string git, PushDialogs dialogs)
    {
        var store = new WorkspaceStore(Path.Combine(directory, ".git", "sextant-workspace"));
        var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings { GitExecutable = git, ReopenTabs = false }, new GitProcessRunner())
        {
            GitExecutable = git,
            GitReady = true,
        };
        vm.Attach(dialogs);
        var tab = new RepositoryViewModel(vm, directory);
        vm.Tabs.Add(tab);
        await vm.Activate(tab);
        await WaitUntilIdle(tab);
        return (vm, tab);
    }

    private static async Task WaitUntilIdle(RepositoryViewModel vm)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (vm.IsBusy && DateTime.UtcNow < until)
            await Task.Delay(30);
        Assert.False(vm.IsBusy);
    }

    private static int PushCount(string log)
    {
        var count = 0;
        foreach (var line in log.Split('\n'))
        {
            if (line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("push"))
                count++;
        }

        return count;
    }

    private static string Git(string git, string directory, params string[] args)
    {
        var info = new ProcessStartInfo(git)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(directory);
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        var error = process.StandardError.ReadToEnd();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} exited {process.ExitCode}{Environment.NewLine}{error}");
        return output;
    }

    private static string Tip(string git, string gitDirectory, string revision)
    {
        var info = new ProcessStartInfo(git)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("--git-dir");
        info.ArgumentList.Add(gitDirectory);
        info.ArgumentList.Add("log");
        info.ArgumentList.Add("-1");
        info.ArgumentList.Add("--format=%s");
        info.ArgumentList.Add(revision);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
        var text = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error);
        return text.Trim();
    }

    private static void TryDelete(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
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

    private sealed class PushDialogs(string? choice) : IDialogService
    {
        public int Picks { get; private set; }

        public IReadOnlyList<string>? LastOptions { get; private set; }

        public Task<string?> PickAsync(string title, string message, IReadOnlyList<string> options)
        {
            Picks++;
            LastOptions = options;
            return Task.FromResult(choice);
        }

        public Task<string?> PickFolderAsync(string title, string? startDirectory = null) => Task.FromResult<string?>(null);

        public Task<string?> PickGitExecutableAsync() => Task.FromResult<string?>(null);

        public Task<bool> ConfirmAsync(string title, string message, string confirm = "OK") => Task.FromResult(true);

        public Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false) => Task.FromResult<string?>(null);

        public Task<string?> PromptSecretAsync(string title, string message) => Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedName) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string title, string typeName, IReadOnlyList<string> patterns) => Task.FromResult<string?>(null);

        public Task<CloneRequest?> PromptCloneAsync() => Task.FromResult<CloneRequest?>(null);

        public Task<PerformanceChoice?> ConfirmPerformanceAsync(PerformanceSuggestion suggestion) => Task.FromResult<PerformanceChoice?>(null);

        public Task<IReadOnlyList<RebaseStep>?> EditRebaseAsync(IReadOnlyList<RebaseStep> steps) => Task.FromResult<IReadOnlyList<RebaseStep>?>(null);

        public Task<SettingsDraft?> EditSettingsAsync(SettingsDraft current) => Task.FromResult<SettingsDraft?>(null);

        public Task ShowAboutAsync() => Task.CompletedTask;

        public Task CopyAsync(string text) => Task.CompletedTask;
    }
}
