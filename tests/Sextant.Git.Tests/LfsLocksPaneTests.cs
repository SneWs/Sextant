using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.Git;
using Sextant.Git.Models;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class LfsLocksPaneTests
{
    [Fact]
    public async Task Lock_groups_show_my_locks_and_other_locks_with_the_matching_actions()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        var mine = new LfsLockListing(
            [new LfsLock("own-b", "mine-b.bin", "Test"), new LfsLock("own-a", "mine-a.bin", "Test")],
            [new LfsLock("other-1", "other.bin", "Other Person")],
            ForceUnlockSupported: true);

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new LockHost(repo.Git), repo.Directory);
            vm.ApplyLfsLockListing(mine);
            Assert.Equal(
                ["My Locks", "mine-a.bin", "mine-b.bin", "Locks", "other.bin"],
                vm.LfsLocks.Select(row => row.IsHeader ? row.Label : row.Path).ToArray());
            Assert.All(vm.LfsLocks.Where(row => row.IsLock && row.IsMine), row =>
            {
                Assert.True(row.ShowCheck);
                Assert.True(row.ShowUnlock);
                Assert.False(row.ShowForceUnlock);
            });
            var other = vm.LfsLocks.Single(row => row.Path == "other.bin");
            Assert.False(other.ShowCheck);
            Assert.False(other.ShowUnlock);
            Assert.True(other.ShowForceUnlock);
            Assert.Equal("Other Person", other.Owner);

            var view = new LfsLocksToolView { DataContext = vm, Width = 800, Height = 700 };
            var window = new Window { Content = view, Width = 800, Height = 700 };
            window.Show();
            Layout(view);

            var list = view.FindControl<ListBox>("LfsLockList");
            Assert.NotNull(list);
            Assert.Equal(["My Locks", "Locks"], VisibleHeads(list));
            Assert.Equal(["Unlock", "Unlock", "Force unlock"], VisibleButtons(list));
            Assert.Equal(3, VisibleChecks(list).Count);
            Assert.Equal(false, MineHeader(vm).HeaderChecked);
            Assert.False(MineHeader(vm).ShowUnlockAll);
            Assert.DoesNotContain(VisibleButtons(list), label => label == "Unlock all");

            MineHeader(vm).ToggleChecksCommand!.Execute(null);
            Layout(view);
            Assert.Equal(true, MineHeader(vm).HeaderChecked);
            Assert.True(MineHeader(vm).ShowUnlockAll);
            Assert.All(vm.LfsLocks.Where(row => row.IsLock && row.IsMine), row => Assert.True(row.IsChecked));
            Assert.Equal(["Unlock all", "Unlock", "Unlock", "Force unlock"], VisibleButtons(list));
            Assert.Equal(true, HeaderCheck(list).IsChecked);

            vm.LfsLocks.Single(row => row.Path == "mine-b.bin").IsChecked = false;
            Layout(view);
            Assert.Null(MineHeader(vm).HeaderChecked);
            Assert.True(MineHeader(vm).ShowUnlockAll);
            Assert.Null(HeaderCheck(list).IsChecked);
            Assert.Contains("Unlock all", VisibleButtons(list));

            MineHeader(vm).ToggleChecksCommand!.Execute(null);
            Layout(view);
            Assert.Equal(true, MineHeader(vm).HeaderChecked);
            Assert.All(vm.LfsLocks.Where(row => row.IsLock && row.IsMine), row => Assert.True(row.IsChecked));

            MineHeader(vm).ToggleChecksCommand!.Execute(null);
            Layout(view);
            Assert.Equal(false, MineHeader(vm).HeaderChecked);
            Assert.False(MineHeader(vm).ShowUnlockAll);
            Assert.All(vm.LfsLocks.Where(row => row.IsLock && row.IsMine), row => Assert.False(row.IsChecked));
            Assert.DoesNotContain(VisibleButtons(list), label => label == "Unlock all");

            vm.LfsLocks.Single(row => row.Path == "mine-a.bin").IsChecked = true;
            vm.ApplyLfsLockListing(mine);
            Layout(view);
            Assert.True(vm.LfsLocks.Single(row => row.Path == "mine-a.bin").IsChecked);
            Assert.False(vm.LfsLocks.Single(row => row.Path == "mine-b.bin").IsChecked);
            Assert.True(MineHeader(vm).ShowUnlockAll);
            Assert.Contains("Unlock all", VisibleButtons(list));

            vm.ApplyLfsLockListing(new LfsLockListing([], [new LfsLock("other-1", "other.bin", "Other Person")], false));
            Layout(view);
            Assert.Equal(["Locks", "other.bin"], vm.LfsLocks.Select(row => row.IsHeader ? row.Label : row.Path).ToArray());
            Assert.Equal(["Locks"], VisibleHeads(list));
            Assert.Empty(VisibleButtons(list));
            Assert.Empty(VisibleChecks(list));
            Assert.False(vm.LfsLocks.Single(row => row.Path == "other.bin").ShowForceUnlock);

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Repository_without_lfs_does_not_ask_for_locks()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new LockHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            Assert.DoesNotContain("lfs locks", vm.CommandLog, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(vm.LfsLocks);
            Assert.Equal("", vm.LocksMessage);

            await vm.RefreshLfsLocksCommand.ExecuteAsync(null);
            Assert.False(vm.HasBanner, vm.Banner);
            Assert.Equal("This repository does not use Git LFS.", vm.LocksMessage);
            Assert.True(vm.ShowLocksMessage);
            Assert.Empty(vm.LfsLocks);
            Assert.DoesNotContain("lfs locks", vm.CommandLog, StringComparison.OrdinalIgnoreCase);
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Checked_locks_unlock_together_and_another_persons_lock_asks_before_force()
    {
        if (!await GitLfsAvailability.IsInstalledAsync())
            return;
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        LfsLock[] mine = [new("own-a", "mine-a.bin", "Test"), new("own-b", "mine-b.bin", "Test")];
        LfsLock[] theirs = [new("other-1", "other.bin", "Other Person")];
        await using var server = new LocalLfsLockServer(mine.Concat(theirs));
        using var repo = LfsLockTests.CreateLfsRepo(server, mine.Concat(theirs).Select(item => item.Path));

        await session.Dispatch(async () =>
        {
            var dialogs = new ScriptedDialogs();
            var vm = new RepositoryViewModel(new LockHost(repo.Git, dialogs), repo.Directory);
            await vm.EnsureLoadedAsync();
            Assert.DoesNotContain("lfs locks", vm.CommandLog, StringComparison.OrdinalIgnoreCase);

            await vm.RefreshLfsLocksCommand.ExecuteAsync(null);
            Assert.False(vm.HasBanner, vm.Banner);
            Assert.Equal(
                ["My Locks", "mine-a.bin", "mine-b.bin", "Locks", "other.bin"],
                Labels(vm));
            Assert.Contains("lfs locks --verify", vm.CommandLog, StringComparison.Ordinal);

            server.VerifyStatus = 500;
            await vm.RefreshLfsLocksCommand.ExecuteAsync(null);
            Assert.True(vm.HasBanner);
            Assert.Empty(vm.LfsLocks);
            Assert.Equal("", vm.LocksMessage);

            server.VerifyStatus = 200;
            await vm.RefreshLfsLocksCommand.ExecuteAsync(null);
            Assert.False(vm.HasBanner, vm.Banner);
            Assert.Equal(["My Locks", "mine-a.bin", "mine-b.bin", "Locks", "other.bin"], Labels(vm));

            Check(vm, "mine-a.bin");
            Check(vm, "mine-b.bin");
            dialogs.Expect("Unlock all", "Unlock all", accept: false);
            await MineHeader(vm).UnlockAllCommand!.ExecuteAsync(null);
            Assert.Contains("Unlock 2 selected LFS locks?", dialogs.Message, StringComparison.Ordinal);
            Assert.Equal(["My Locks", "mine-a.bin", "mine-b.bin", "Locks", "other.bin"], Labels(vm));
            Assert.DoesNotContain(server.Requests, request => request.Route.EndsWith("/unlock", StringComparison.Ordinal));

            vm.LfsLocks.Single(row => row.Path == "mine-b.bin").IsChecked = false;
            dialogs.Expect("Unlock all", "Unlock all", accept: false);
            await MineHeader(vm).UnlockAllCommand!.ExecuteAsync(null);
            Assert.Contains("Unlock 1 selected LFS lock?", dialogs.Message, StringComparison.Ordinal);
            Assert.True(vm.LfsLocks.Single(row => row.Path == "mine-a.bin").IsChecked);

            await vm.RefreshLfsLocksCommand.ExecuteAsync(null);
            Assert.True(vm.LfsLocks.Single(row => row.Path == "mine-a.bin").IsChecked);
            Assert.False(vm.LfsLocks.Single(row => row.Path == "mine-b.bin").IsChecked);

            dialogs.Expect("Unlock all", "Unlock all", accept: true);
            await MineHeader(vm).UnlockAllCommand!.ExecuteAsync(null);
            Assert.False(vm.HasBanner, vm.Banner);
            Assert.Equal(["My Locks", "mine-b.bin", "Locks", "other.bin"], Labels(vm));
            Assert.Contains(server.Requests, request => request.Route == "locks/own-a/unlock" && !request.Force);

            var confirmations = dialogs.Confirmations;
            await vm.LfsLocks.Single(row => row.Path == "mine-b.bin").UnlockCommand!.ExecuteAsync(null);
            Assert.Equal(confirmations, dialogs.Confirmations);
            Assert.False(vm.HasBanner, vm.Banner);
            Assert.Equal(["Locks", "other.bin"], Labels(vm));
            Assert.Contains(server.Requests, request => request.Route == "locks/own-b/unlock" && !request.Force);

            dialogs.Expect("Force Unlock file", "Force Unlock", accept: false);
            await vm.LfsLocks.Single(row => row.Path == "other.bin").UnlockCommand!.ExecuteAsync(null);
            Assert.Contains("Other Person", dialogs.Message, StringComparison.Ordinal);
            Assert.Contains("other.bin", dialogs.Message, StringComparison.Ordinal);
            Assert.Equal(["Locks", "other.bin"], Labels(vm));
            Assert.DoesNotContain(server.Requests, request => request.Route == "locks/other-1/unlock");

            dialogs.Expect("Force Unlock file", "Force Unlock", accept: true);
            await vm.LfsLocks.Single(row => row.Path == "other.bin").UnlockCommand!.ExecuteAsync(null);
            Assert.False(vm.HasBanner, vm.Banner);
            Assert.Empty(vm.LfsLocks);
            Assert.Equal("No LFS locks.", vm.LocksMessage);
            Assert.Contains(server.Requests, request => request.Route == "locks/other-1/unlock" && request.Force);
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    private static void Layout(LfsLocksToolView view)
    {
        view.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static LfsLockRow MineHeader(RepositoryViewModel vm) =>
        vm.LfsLocks.Single(row => row.IsHeader && row.IsMine);

    private static string[] Labels(RepositoryViewModel vm) =>
        vm.LfsLocks.Select(row => row.IsHeader ? row.Label : row.Path).ToArray();

    private static void Check(RepositoryViewModel vm, string path) =>
        vm.LfsLocks.Single(row => row.Path == path).IsChecked = true;

    private static IReadOnlyList<string> VisibleHeads(ListBox list) =>
        list.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("filehead") && border.IsEffectivelyVisible)
            .OrderBy(border => Top(border, list))
            .Select(border => border.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible).Text ?? "")
            .ToArray();

    private static IReadOnlyList<string> VisibleButtons(ListBox list) =>
        list.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible)
            .OrderBy(button => Top(button, list))
            .ThenBy(button => Left(button, list))
            .Select(button => Assert.IsType<string>(button.Content))
            .ToArray();

    private static IReadOnlyList<CheckBox> VisibleChecks(ListBox list) =>
        list.GetVisualDescendants().OfType<CheckBox>().Where(box => box.IsEffectivelyVisible).ToArray();

    private static CheckBox HeaderCheck(ListBox list) =>
        VisibleChecks(list).Single(box => AutomationProperties.GetName(box) == "Select my locks");

    private static double Top(Control control, Visual relative) =>
        control.TranslatePoint(default, relative)!.Value.Y;

    private static double Left(Control control, Visual relative) =>
        control.TranslatePoint(default, relative)!.Value.X;

    private sealed class LockHost(string git, IDialogService? dialogs = null) : IWorkspaceHost
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

    private sealed class ScriptedDialogs : IDialogService
    {
        private readonly Queue<(string Title, string Confirm, bool Accept)> _expected = new();

        public string Message { get; private set; } = "";

        public int Confirmations { get; private set; }

        public void Expect(string title, string confirm, bool accept) =>
            _expected.Enqueue((title, confirm, accept));

        public Task<bool> ConfirmAsync(string title, string message, string confirm = "OK")
        {
            var step = _expected.Dequeue();
            Assert.Equal(step.Title, title);
            Assert.Equal(step.Confirm, confirm);
            Message = message;
            Confirmations++;
            return Task.FromResult(step.Accept);
        }

        public Task<string?> PickFolderAsync(string title, string? startDirectory = null) => throw new NotSupportedException();

        public Task<string?> PickGitExecutableAsync() => throw new NotSupportedException();

        public Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false) => throw new NotSupportedException();

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
}
