using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant;
using Sextant.Git;
using Sextant.Git.Workspace;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class BranchShortcutTests
{
    [Fact]
    public async Task Create_branch_shortcut_opens_the_prompt_once()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");

        await session.Dispatch(async () =>
        {
            var (vm, tab, window) = await Open(repo);
            try
            {
                await WaitUntilIdle(tab);
                var modifiers = ShortcutModifiers();
                var menu = NativeMenu.GetMenu(window)!;
                var repository = menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_Repository");
                var create = repository.Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_Create Branch");
                Assert.Equal(AppGestures.CommandKey(Key.C, KeyModifiers.Shift), create.Gesture);

                // Key repeat delivers more than one key-down before the key comes up.
                window.KeyPress(Key.C, modifiers, PhysicalKey.C, null);
                window.KeyPress(Key.C, modifiers, PhysicalKey.C, null);
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(window.OwnedWindows);

                window.KeyRelease(Key.C, modifiers, PhysicalKey.C, null);
                Dispatcher.UIThread.RunJobs();
                var dialog = Assert.Single(window.OwnedWindows);
                Assert.Equal("Create branch", dialog.Title);
                var name = dialog.GetVisualDescendants().OfType<TextBox>().Single();
                name.Text = "topic";
                Assert.True(name.Focus());
                dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                var creating = tab.CreateBranchCommand.ExecutionTask;
                Assert.NotNull(creating);
                await creating;
                Dispatcher.UIThread.RunJobs();
                await WaitUntilIdle(tab);

                Assert.Empty(window.OwnedWindows);
                Assert.Equal("topic", repo.CurrentBranch());
                Assert.False(tab.HasBanner, tab.Banner);

                // A key-down that arrives after the key was released must not open the prompt again.
                window.KeyPress(Key.C, modifiers, PhysicalKey.C, null);
                window.KeyPress(Key.C, modifiers, PhysicalKey.C, null);
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(window.OwnedWindows);
                Assert.Equal("topic", repo.CurrentBranch());
            }
            finally
            {
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Dispatcher.UIThread.RunJobs();
                await vm.Shutdown();
                window.DataContext = null;
                window.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    private static RawInputModifiers ShortcutModifiers()
    {
        var modifiers = RawInputModifiers.Shift;
        if (AppGestures.Command == KeyModifiers.Meta)
            modifiers |= RawInputModifiers.Meta;
        else
            modifiers |= RawInputModifiers.Control;
        return modifiers;
    }

    private static async Task<(MainViewModel Vm, RepositoryViewModel Tab, MainWindow Window)> Open(TempRepo repo)
    {
        var store = new WorkspaceStore(Path.Combine(repo.Directory, ".git", "sextant-workspace"));
        var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings { GitExecutable = repo.Git, ReopenTabs = false }, new GitProcessRunner())
        {
            GitExecutable = repo.Git,
            GitReady = true,
        };
        var tab = new RepositoryViewModel(vm, repo.Directory);
        vm.Tabs.Add(tab);
        await vm.Activate(tab);
        var window = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
        window.Show();
        await vm.InitializeAsync();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (vm, tab, window);
    }

    private static async Task WaitUntilIdle(RepositoryViewModel tab)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (tab.IsBusy && DateTime.UtcNow < until)
            await Task.Delay(30);
        Assert.False(tab.IsBusy, tab.BusyText);
        Assert.True(tab.IsReady);
    }
}
