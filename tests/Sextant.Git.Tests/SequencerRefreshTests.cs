using Avalonia.Headless;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git.Parsing;
using Sextant.Services;
using Sextant.ViewModels;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class SequencerRefreshTests
{
    [Fact]
    public async Task Merge_keeps_the_open_editor_until_refresh()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = Conflict(merge: true);
        await session.Dispatch(() => HoldViews(repo), CancellationToken.None);
    }

    [Fact]
    public async Task Rebase_keeps_the_open_editor_until_refresh()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = Conflict(merge: false);
        await session.Dispatch(() => HoldViews(repo), CancellationToken.None);
    }

    private static async Task<int> HoldViews(TempRepo repo)
    {
        var vm = new RepositoryViewModel(new Host(repo.Git), repo.Directory);
        try
        {
            await vm.EnsureLoadedAsync();
            var resolve = Assert.IsAssignableFrom<IAsyncRelayCommand>(
                vm.Files.Single(row => row.IsFile && row.Path == "a.txt").MergetoolCommand);
            await resolve.ExecuteAsync(null);
            Assert.True(vm.ShowingMerge);
            var edit = vm.MergeEdit;
            Assert.NotNull(edit);
            edit.ApplyEdit(0, 0, "kept ");
            var text = edit.Result;
            var conflict = vm.Files.Single(row => row.IsFile && row.Path == "a.txt");
            var working = vm.SelectedGraphRow;
            Assert.NotNull(working);
            Assert.True(working.IsWorkingCopy);

            repo.WriteFile("extra.txt", "later\n");
            await vm.RefreshFromWatcherCommand.ExecuteAsync(null);
            await vm.RefreshFromFocusAsync();
            await vm.FetchInBackground();

            Assert.Same(edit, vm.MergeEdit);
            Assert.Equal(text, edit.Result);
            Assert.StartsWith("kept ", text, StringComparison.Ordinal);
            Assert.Same(conflict, vm.Files.Single(row => row.IsFile && row.Path == "a.txt"));
            Assert.Same(working, vm.SelectedGraphRow);
            Assert.DoesNotContain(vm.Files, row => row.Path == "extra.txt");

            await vm.Refresh();
            Assert.Contains(vm.Files, row => row.IsFile && row.Path == "extra.txt");
        }
        finally
        {
            await vm.DisposeAsync();
        }

        return 0;
    }

    private static TempRepo Conflict(bool merge)
    {
        var repo = new TempRepo();
        repo.Run("config", "merge.tool", "");
        repo.Run("remote", "add", "origin", Path.Combine(Path.GetTempPath(), "sextant-missing-" + Guid.NewGuid().ToString("N")));
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("a.txt", "other\n");
        repo.CommitAll("other");
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");
        if (merge)
            Assert.Throws<InvalidOperationException>(() => repo.Run("merge", "other"));
        else
        {
            repo.Run("switch", "other");
            Assert.Throws<InvalidOperationException>(() => repo.Run("rebase", trunk));
        }

        return repo;
    }

    private sealed class Host(string git) : IWorkspaceHost
    {
        public IDialogService? Dialogs => null;
        public GitProcessRunner Runner { get; } = new();
        public string? GitExecutable => git;
        public string? MergeTool => null;
        public bool GitReady => true;
        public Task Activate(RepositoryViewModel tab) => Task.CompletedTask;
        public Task Close(RepositoryViewModel tab) => Task.CompletedTask;
        public void NoteLoaded(RepositoryViewModel tab) { }
        public void Save() { }
        public Task OpenRepositoryAsync(string path) => Task.CompletedTask;
    }
}
