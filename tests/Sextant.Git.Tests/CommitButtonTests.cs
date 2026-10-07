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
    public async Task Commit_stays_disabled_until_a_file_is_staged()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "two\n");

        // Returning a value makes Dispatch wait for the async body. A bare async lambda does not.
        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new CommitHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            vm.CommitMessage = "next";
            Assert.True(vm.NothingStaged);
            Assert.False(vm.CanCommit);
            Assert.False(vm.CanAmend);
            Assert.False(vm.CanCommitOrAmend);

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var commit = view.GetVisualDescendants().OfType<SplitButton>().Single(button => button.Content as string == "Commit");
            var primary = Part(commit, "PART_PrimaryButton");
            var secondary = Part(commit, "PART_SecondaryButton");
            Assert.False(commit.IsEffectivelyEnabled);
            Assert.False(primary.IsEffectivelyEnabled);
            Assert.False(secondary.IsEffectivelyEnabled);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Nothing staged." && text.IsEffectivelyVisible);

            var before = vm.Rows.Count(row => !row.IsWorkingCopy);
            await vm.Commit();
            Assert.Equal(before, vm.Rows.Count(row => !row.IsWorkingCopy));
            Assert.Equal("next", vm.CommitMessage);

            var file = vm.Files.First(row => !row.IsHeader && row.Path == "a.txt");
            await ((IAsyncRelayCommand)file.StageCommand).ExecuteAsync(null);
            await WaitUntilIdle(vm);
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.False(vm.NothingStaged);
            Assert.True(vm.CanCommit);
            Assert.True(vm.CanAmend);
            Assert.True(commit.IsEffectivelyEnabled);
            Assert.True(primary.IsEffectivelyEnabled);
            Assert.True(secondary.IsEffectivelyEnabled);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Nothing staged." && text.IsEffectivelyVisible);

            await vm.Commit();
            await WaitUntilIdle(vm);
            Assert.Contains(vm.Rows, row => row.Subject == "next");
            Assert.Equal("", vm.CommitMessage);

            window.Close();
            await vm.DisposeAsync();
            return 0;
        }, CancellationToken.None);
    }

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
