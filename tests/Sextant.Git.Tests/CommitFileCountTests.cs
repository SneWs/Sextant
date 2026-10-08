using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Sextant.Git;
using Sextant.Git.Parsing;
using Sextant.Git.Repo;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;
using System.Text;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class CommitFileCountTests
{
    [Fact]
    public void Batch_numstat_counts_files_per_commit_and_skips_the_separator()
    {
        var stdout = Encoding.UTF8.GetBytes(
            "@@aaa\0\n3\t1\ta.txt\0-\t-\tbin.dat\0@@bbb\0\n2\t0\tb.txt\0");
        var counts = NumStatParser.ParseFileCounts(stdout);
        Assert.Equal(2, counts["aaa"]);
        Assert.Equal(1, counts["bbb"]);
    }

    [Fact]
    public void Batch_numstat_gives_a_merge_no_files()
    {
        var stdout = Encoding.UTF8.GetBytes("@@aaa\0\n1\t1\ta.txt\0@@bbb\0\n");
        var counts = NumStatParser.ParseFileCounts(stdout);
        Assert.Equal(1, counts["aaa"]);
        Assert.Equal(0, counts["bbb"]);
    }

    [Fact]
    public void Batch_numstat_counts_a_rename_as_one_file()
    {
        // -z writes a rename as two records: "2\t2\told" then "new".
        var stdout = Encoding.UTF8.GetBytes("@@aaa\0\n2\t2\told.txt\0new.txt\0");
        var counts = NumStatParser.ParseFileCounts(stdout);
        Assert.Equal(1, counts["aaa"]);
    }

    [Fact]
    public async Task File_counts_come_from_one_walk_of_the_requested_commits()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("root");
        repo.WriteFile("b.txt", "two\n");
        repo.WriteFile("c.txt", "three\n");
        repo.CommitAll("two files");
        var shas = repo.RunCapture("rev-list", "HEAD")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(sha => sha.Trim())
            .ToArray();

        await using var session = await RepositorySession.OpenAsync(
            new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
        var counts = await session.FileCountsAsync(shas, CancellationToken.None);
        Assert.Equal(2, counts.Count);
        Assert.Equal(2, counts[shas[0]]);
        Assert.Equal(1, counts[shas[1]]);
        Assert.Empty(await session.FileCountsAsync([], CancellationToken.None));
    }

    [Fact]
    public async Task Realized_history_rows_get_a_file_count_pill_and_a_merge_gets_none()
    {
        using var headless = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("root");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "feature");
        repo.WriteFile("f.txt", "hello\n");
        repo.CommitAll("feature");
        repo.Run("switch", trunk);
        repo.Run("merge", "--no-ff", "--no-edit", "feature");

        await headless.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new CountHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var beta = vm.Rows.FirstOrDefault(row => row.Subject == "feature");
            var merge = vm.Rows.FirstOrDefault(row => row.Subject is not null && row.Subject.StartsWith("Merge branch", StringComparison.Ordinal));
            Assert.NotNull(beta);
            Assert.NotNull(merge);
            for (var attempt = 0; attempt < 100 && beta.FileCountText.Length == 0; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20);
            }

            Assert.Equal("1", beta.FileCountText);
            Assert.Equal("", merge.FileCountText);
            Assert.Equal("", vm.Rows[0].FileCountText);
            window.Close();
        }, CancellationToken.None);
    }

    private sealed class CountHost(string git) : IWorkspaceHost
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
