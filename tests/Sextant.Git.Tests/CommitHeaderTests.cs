using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.Git;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

public class CommitMessageTests
{
    [Fact]
    public async Task Commit_message_keeps_the_subject_and_the_body()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.Run("add", "-A");
        repo.Run("commit", "-m", "ship it", "-m", "Explain the change.");

        await using var session = await RepositorySession.OpenAsync(
            new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
        var sha = session.Snapshot().Commits[0].Commit.Sha;
        var message = await session.CommitMessageAsync(sha, CancellationToken.None);
        Assert.Equal("ship it\n\nExplain the change.", message);
    }
}

[Collection(HeadlessCollection.Name)]
public class CommitHeaderTests
{
    [Fact]
    public async Task Selected_commit_message_and_hash_are_read_only_fields()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        var body = string.Join('\n', Enumerable.Range(1, 24).Select(line => "line " + line.ToString()));
        repo.WriteFile("a.txt", "one\n");
        repo.Run("add", "-A");
        repo.Run("commit", "-m", "ship it", "-m", body);

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new CommitHeaderHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            view.Resources["CommitRemovedPillBrush"] = new SolidColorBrush(Color.Parse("#4A2C35"));
            view.Resources["CommitRemovedTextBrush"] = new SolidColorBrush(Color.Parse("#F38BA8"));
            view.Resources["CommitAddedPillBrush"] = new SolidColorBrush(Color.Parse("#244032"));
            view.Resources["CommitAddedTextBrush"] = new SolidColorBrush(Color.Parse("#A6E3A1"));
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var editor = view.GetVisualDescendants().OfType<TextBox>().First(box => box.PlaceholderText == "Commit message");
            Assert.False(editor.IsReadOnly);
            Assert.True(editor.IsEffectivelyVisible);
            Assert.False(view.FindControl<TextBox>("CommitMessageBox")!.IsEffectivelyVisible);

            var row = vm.Rows.Single(candidate => candidate.Subject == "ship it");
            vm.SelectedGraphRow = row;
            var expected = "ship it\n\n" + body;
            var until = DateTime.UtcNow.AddSeconds(5);
            while ((vm.CommitMessageText != expected || !vm.ShowCommitStats) && DateTime.UtcNow < until)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20);
            }

            Assert.Equal(expected, vm.CommitMessageText);
            Assert.Equal(row.Sha, vm.CommitShaText);
            Assert.Equal("Test <test@example.com>", vm.CommitAuthorText);
            Assert.Contains("2026", vm.CommitDateText);
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var message = view.FindControl<TextBox>("CommitMessageBox");
            var hash = view.FindControl<TextBox>("CommitShaBox");
            var author = view.FindControl<TextBox>("CommitAuthorBox");
            var date = view.FindControl<TextBox>("CommitDateBox");
            Assert.NotNull(message);
            Assert.NotNull(hash);
            Assert.NotNull(author);
            Assert.NotNull(date);
            Assert.True(message.IsReadOnly);
            Assert.True(hash.IsReadOnly);
            Assert.True(author.IsReadOnly);
            Assert.True(date.IsReadOnly);
            Assert.True(message.IsEffectivelyVisible);
            Assert.True(hash.IsEffectivelyVisible);
            Assert.True(author.IsEffectivelyVisible);
            Assert.True(date.IsEffectivelyVisible);
            Assert.False(editor.IsEffectivelyVisible);
            Assert.Equal(new Thickness(0), hash.BorderThickness);
            Assert.Equal(new Thickness(0), author.BorderThickness);
            Assert.Equal(new Thickness(0), date.BorderThickness);
            Assert.False(HasDrawnBorder(hash));
            Assert.False(HasDrawnBorder(author));
            Assert.False(HasDrawnBorder(date));
            Assert.True(message.BorderThickness.Left > 0 || HasDrawnBorder(message));
            Assert.True(message.Bounds.Height <= 168);
            Assert.True(message.Bounds.Height > 40, $"message height {message.Bounds.Height}");

            var files = view.FindControl<ListBox>("FileList");
            Assert.NotNull(files);
            var stats = view.FindControl<StackPanel>("CommitStatsRow");
            var removedChip = view.FindControl<Border>("CommitRemovedChip");
            var addedChip = view.FindControl<Border>("CommitAddedChip");
            var removedPill = view.FindControl<TextBlock>("CommitRemovedPill");
            var addedPill = view.FindControl<TextBlock>("CommitAddedPill");
            var statsFiles = view.FindControl<TextBlock>("CommitStatsFiles");
            Assert.NotNull(stats);
            Assert.NotNull(removedChip);
            Assert.NotNull(addedChip);
            Assert.NotNull(removedPill);
            Assert.NotNull(addedPill);
            Assert.NotNull(statsFiles);
            Assert.True(stats.IsEffectivelyVisible);
            Assert.Equal("1 file changed", statsFiles.Text);
            Assert.Equal("-0", removedPill.Text);
            Assert.Equal("+1", addedPill.Text);
            Assert.Equal(Color.Parse("#4A2C35"), Assert.IsType<SolidColorBrush>(removedChip.Background).Color);
            Assert.Equal(Color.Parse("#F38BA8"), Assert.IsType<SolidColorBrush>(removedPill.Foreground).Color);
            Assert.Equal(Color.Parse("#244032"), Assert.IsType<SolidColorBrush>(addedChip.Background).Color);
            Assert.Equal(Color.Parse("#A6E3A1"), Assert.IsType<SolidColorBrush>(addedPill.Foreground).Color);
            Assert.True(removedChip.CornerRadius.TopLeft >= 8);
            Assert.True(addedChip.CornerRadius.TopLeft >= 8);
            var changes = files.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text != null && text.Text.StartsWith("Changes", StringComparison.Ordinal));
            Assert.True(Bottom(date, view) <= Top(stats, view), $"date {Bottom(date, view)} stats {Top(stats, view)}");
            Assert.True(Bottom(stats, view) <= Top(message, view), $"stats {Bottom(stats, view)} message {Top(message, view)}");
            Assert.True(Top(changes, view) >= Top(files, view), $"changes {Top(changes, view)} list {Top(files, view)}");
            Assert.True(Bottom(message, view) <= Top(changes, view), $"message {Bottom(message, view)} changes {Top(changes, view)}");

            Assert.True(message.Focus());
            message.SelectAll();
            Assert.Equal(expected, message.SelectedText);
            Assert.True(hash.Focus());
            hash.SelectAll();
            Assert.Equal(row.Sha, hash.SelectedText);
            Assert.Equal(40, hash.SelectedText!.Length);

            vm.SelectedGraphRow = vm.Rows.Single(candidate => candidate.IsWorkingCopy);
            until = DateTime.UtcNow.AddSeconds(5);
            while (vm.ShowingCommit && DateTime.UtcNow < until)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20);
            }

            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.ShowingWorkingCopy);
            Assert.False(message.IsEffectivelyVisible);
            Assert.True(editor.IsEffectivelyVisible);
            Assert.False(editor.IsReadOnly);

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    private static bool HasDrawnBorder(Control box) =>
        box.GetVisualDescendants().OfType<Border>().Any(border =>
            border.IsEffectivelyVisible
            && (border.BorderThickness.Left > 0
                || border.BorderThickness.Top > 0
                || border.BorderThickness.Right > 0
                || border.BorderThickness.Bottom > 0));

    private static double Top(Control control, Visual relative)
    {
        var origin = control.TranslatePoint(default, relative);
        Assert.NotNull(origin);
        return origin.Value.Y;
    }

    private static double Bottom(Control control, Visual relative) => Top(control, relative) + control.Bounds.Height;

    private sealed class CommitHeaderHost(string git) : IWorkspaceHost
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
