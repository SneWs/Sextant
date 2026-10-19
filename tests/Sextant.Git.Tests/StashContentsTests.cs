using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Sextant.Git;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class StashContentsTests
{
    [Fact]
    public async Task Clicking_a_stash_shows_its_files_and_diff()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "three\n");
        repo.WriteFile("new.txt", "fresh\n");
        repo.WriteFile("sub/deep.txt", "deep\n");
        var branch = repo.CurrentBranch();
        repo.Run("add", "a.txt");
        repo.Run("stash", "push", "--include-untracked", "-m", "wip note");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new StashHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            var stash = vm.Locations.Single(item => item.ShowPop);
            Assert.False(stash.ShowCheckout);
            Assert.True(stash.ShowReveal);
            Assert.False(string.IsNullOrEmpty(stash.Oid));
            Assert.Contains("wip note", stash.Label, StringComparison.Ordinal);

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var locations = view.FindControl<ListBox>("LocationList");
            Assert.NotNull(locations);
            locations.ScrollIntoView(stash);
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Click(window, locations, stash);

            await Until(() =>
                vm.Files.Any(row => row.Path == "new.txt")
                && vm.Files.Any(row => row.Path == "a.txt")
                && vm.Files.Any(row => row.Path == "sub/deep.txt")
                && vm.DiffRows.OfType<DiffFileRow>().Any(row => row.Path == "new.txt")
                && vm.CommitStatsFiles == "3 files changed");
            Assert.Contains(vm.Files, row => row.Path == "a.txt");
            Assert.Contains(vm.Files, row => row.Path == "new.txt");
            Assert.Contains(vm.Files, row => row.Path == "sub/deep.txt");
            Assert.Contains(vm.DiffRows.OfType<DiffFileRow>(), row => row.Path == "new.txt");
            Assert.Contains(vm.DiffRows.OfType<DiffFileRow>(), row => row.Path == "sub/deep.txt");
            Assert.Equal("3 files changed", vm.CommitStatsFiles);
            Assert.Equal(stash.Oid, vm.SelectedGraphRow?.Sha, ignoreCase: true);
            Assert.Equal(branch, repo.CurrentBranch());
            Assert.Equal("one\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")));
            Assert.False(File.Exists(Path.Combine(repo.Directory, "new.txt")));

            await vm.ToggleAllFilesCommand.ExecuteAsync(null);
            vm.SelectedFile = vm.Files.First(row => row.Path == "new.txt");
            if (vm.LoadSelectedFileCommand.ExecutionTask is { } loading)
                await loading;
            await Until(() => HasLine(vm, "fresh") && !HasLine(vm, "three"));
            Assert.True(HasLine(vm, "fresh"), string.Join(" | ", Lines(vm)));
            Assert.False(HasLine(vm, "three"), string.Join(" | ", Lines(vm)));
            Assert.False(HasLine(vm, "deep"), string.Join(" | ", Lines(vm)));

            vm.SelectedGraphRow = vm.Rows.Single(row => row.Subject == "first");
            await Until(() => vm.ShowingCommit && vm.Files.All(row => row.Path != "new.txt") && vm.Files.Any(row => row.Path == "a.txt"));
            Assert.DoesNotContain(vm.Files, row => row.Path == "new.txt");
            Assert.Contains(vm.Files, row => row.Path == "a.txt");

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    private static async Task Until(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (!done() && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    private static void Click(Window window, ListBox list, object item)
    {
        var row = list.ContainerFromItem(item) as ListBoxItem;
        Assert.NotNull(row);
        var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static bool HasLine(RepositoryViewModel vm, string text) =>
        Lines(vm).Any(line => line.Contains(text, StringComparison.Ordinal));

    private static IEnumerable<string> Lines(RepositoryViewModel vm) =>
        vm.DiffRows.OfType<DiffEditorRow>().SelectMany(row => row.Lines.Concat(row.RightLines)).Select(line => line.Text);

    private sealed class StashHost(string git) : IWorkspaceHost
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
