using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class CommitButtonTests
{
    [Fact]
    public async Task Commit_with_a_message_stages_everything_when_the_index_is_empty()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.WriteFile("c.txt", "keep\n");
        repo.CommitAll("first");

        // Returning a value makes Dispatch wait for the async body. A bare async lambda does not.
        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new CommitHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            vm.CommitMessage = "empty index";
            Assert.True(vm.NothingStaged);
            Assert.False(vm.CanCommit);
            await vm.Commit();
            Assert.Equal("first", TipSubject(repo));

            repo.WriteFile("a.txt", "two\n");
            repo.WriteFile("b.txt", "new\n");
            File.Delete(Path.Combine(repo.Directory, "c.txt"));
            await vm.Refresh();
            vm.CommitMessage = "";
            Assert.True(vm.NothingStaged);
            Assert.False(vm.CanCommit);
            Assert.Equal("Commit stages all changes.", vm.NothingStagedText);
            await vm.Commit();
            Assert.Equal("first", TipSubject(repo));
            Assert.Contains("a.txt", repo.RunCapture("status", "--porcelain"), StringComparison.Ordinal);

            vm.CommitMessage = "next";
            Assert.True(vm.NothingStaged);
            Assert.True(vm.CanCommit);
            Assert.False(vm.CanAmend);
            Assert.True(vm.CanCommitOrAmend);

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var commit = view.GetVisualDescendants().OfType<SplitButton>().Single(button => button.Content as string == "Commit");
            var primary = Part(commit, "PART_PrimaryButton");
            var secondary = Part(commit, "PART_SecondaryButton");
            Assert.True(commit.IsEffectivelyEnabled);
            Assert.True(primary.IsEffectivelyEnabled);
            Assert.True(secondary.IsEffectivelyEnabled);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Commit stages all changes." && text.IsEffectivelyVisible);

            await vm.Commit();
            await WaitUntilIdle(vm);
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(vm.Rows, row => row.Subject == "next");
            Assert.Equal("", vm.CommitMessage);
            Assert.Equal("next", TipSubject(repo));
            var names = TipNames(repo);
            Assert.Contains("M\ta.txt", names, StringComparison.Ordinal);
            Assert.Contains("A\tb.txt", names, StringComparison.Ordinal);
            Assert.Contains("D\tc.txt", names, StringComparison.Ordinal);
            Assert.Equal("", repo.RunCapture("status", "--porcelain").Trim());
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Nothing staged." && text.IsEffectivelyVisible);

            window.Close();
            await vm.DisposeAsync();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Commit_leaves_unstaged_files_alone_when_something_is_already_staged()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.WriteFile("b.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "staged\n");
        repo.WriteFile("b.txt", "later\n");
        repo.Run("add", "a.txt");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new CommitHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            vm.CommitMessage = "partial";
            Assert.False(vm.NothingStaged);
            Assert.True(vm.CanCommit);

            await vm.Commit();
            await WaitUntilIdle(vm);

            Assert.Equal("partial", TipSubject(repo));
            var names = TipNames(repo);
            Assert.Contains("M\ta.txt", names, StringComparison.Ordinal);
            Assert.DoesNotContain("b.txt", names, StringComparison.Ordinal);
            Assert.Contains(" M b.txt", repo.RunCapture("status", "--porcelain"), StringComparison.Ordinal);
            Assert.Equal("", vm.CommitMessage);

            await vm.DisposeAsync();
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Commit_does_not_stage_during_a_rebase_or_a_merge()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        using var rebaseRepo = new TempRepo();
        var trunk = StartConflict(rebaseRepo);
        rebaseRepo.Run("switch", "other");
        Assert.Throws<InvalidOperationException>(() => rebaseRepo.Run("rebase", trunk));
        rebaseRepo.WriteFile("extra.txt", "side\n");

        using var mergeRepo = new TempRepo();
        StartConflict(mergeRepo);
        Assert.Throws<InvalidOperationException>(() => mergeRepo.Run("merge", "other"));
        mergeRepo.WriteFile("extra.txt", "side\n");

        await session.Dispatch(async () =>
        {
            await AssertCommitLeavesTheOperation(rebaseRepo, "rebase-merge", "rebase-apply");
            await AssertCommitLeavesTheOperation(mergeRepo, "MERGE_HEAD");
            return 0;
        }, CancellationToken.None);
    }

    private static string StartConflict(TempRepo repo)
    {
        repo.WriteFile("a.txt", "base\n");
        repo.CommitAll("base");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("a.txt", "other\n");
        repo.CommitAll("other");
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "main\n");
        repo.CommitAll("main");
        return trunk;
    }

    private static async Task AssertCommitLeavesTheOperation(TempRepo repo, params string[] markers)
    {
        var vm = new RepositoryViewModel(new CommitHost(repo.Git), repo.Directory);
        try
        {
            await vm.EnsureLoadedAsync();
            var before = TipSubject(repo);
            vm.CommitMessage = "should not land";
            Assert.True(vm.NothingStaged);
            Assert.False(vm.CanCommit);
            Assert.False(vm.CanCommitOrAmend);
            Assert.Equal("Nothing staged.", vm.NothingStagedText);

            await vm.Commit();
            Assert.Equal("should not land", vm.CommitMessage);
            Assert.Equal(before, TipSubject(repo));
            var status = repo.RunCapture("status", "--porcelain");
            Assert.Contains("UU a.txt", status, StringComparison.Ordinal);
            Assert.Contains("?? extra.txt", status, StringComparison.Ordinal);
            Assert.Contains(markers, marker =>
                Directory.Exists(Path.Combine(repo.Directory, ".git", marker))
                || File.Exists(Path.Combine(repo.Directory, ".git", marker)));
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    private static string TipSubject(TempRepo repo) =>
        repo.RunCapture("log", "-1", "--format=%s").Trim();

    private static string TipNames(TempRepo repo) =>
        repo.RunCapture("show", "--name-status", "--pretty=format:", "HEAD");

    private static Button Part(SplitButton commit, string name) =>
        commit.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);

    private static async Task WaitUntilIdle(RepositoryViewModel vm)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (vm.IsBusy && DateTime.UtcNow < until)
            await Task.Delay(30);
        Assert.False(vm.IsBusy);
    }

    private sealed class CommitHost(string git) : IWorkspaceHost
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
