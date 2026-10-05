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

[Collection(HeadlessCollection.Name)]
public class FileListTests
{
    [Fact]
    public async Task Staged_files_sit_above_unstaged_and_the_section_is_a_bar()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("staged.txt", "staged\n");
        repo.Run("add", "staged.txt");
        repo.WriteFile("unstaged.txt", "unstaged\n");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new FileListHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();

            Assert.Equal(
                ["Staged", "staged.txt", "Unstaged", "unstaged.txt"],
                vm.Files.Select(row => row.IsHeader ? row.Label : row.Path).ToArray());
            var stagedFile = vm.Files.First(row => row.Path == "staged.txt");
            Assert.Same(stagedFile, vm.SelectedFile);
            vm.SelectedFile = vm.Files.First(row => row.IsHeader && row.Label == "Staged");
            Assert.Same(stagedFile, vm.SelectedFile);

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var list = view.FindControl<ListBox>("FileList");
            Assert.NotNull(list);
            var heads = list.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("filehead") && border.IsEffectivelyVisible)
                .OrderBy(border => Top(border, list))
                .ToList();
            Assert.Equal(2, heads.Count);
            var stagedHead = heads[0];
            var unstagedHead = heads[1];
            Assert.Equal("Staged", HeadText(stagedHead));
            Assert.Equal("Unstaged", HeadText(unstagedHead));

            var stagedText = Text(list, "staged.txt");
            var unstagedText = Text(list, "unstaged.txt");
            var stagedTop = Top(stagedHead, list);
            var stagedFileTop = Top(stagedText, list);
            var unstagedTop = Top(unstagedHead, list);
            var unstagedFileTop = Top(unstagedText, list);
            Assert.True(stagedTop < stagedFileTop, $"staged header {stagedTop} file {stagedFileTop}");
            Assert.True(stagedFileTop < unstagedTop, $"staged file {stagedFileTop} unstaged header {unstagedTop}");
            Assert.True(unstagedTop < unstagedFileTop, $"unstaged header {unstagedTop} file {unstagedFileTop}");

            Assert.True(stagedHead.Bounds.Width > stagedText.Bounds.Width + 40);
            Assert.True(stagedHead.CornerRadius.TopLeft > 0);
            Assert.True(stagedHead.BorderThickness.Left > 0);
            Assert.True(stagedHead.BorderThickness.Top > 0);
            Assert.False(IsClear(stagedHead.Background));
            Assert.Equal(FontWeight.SemiBold, HeadLabel(stagedHead).FontWeight);
            Assert.True(HeadLabel(stagedHead).Opacity > 0.9);

            var fileBorder = stagedText.FindAncestorOfType<Border>();
            Assert.NotNull(fileBorder);
            Assert.DoesNotContain("filehead", fileBorder.Classes);
            Assert.True(IsClear(fileBorder.Background));
            Assert.Equal(new Thickness(0), fileBorder.BorderThickness);
            Assert.NotEqual(FontWeight.SemiBold, stagedText.FontWeight);

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    private static TextBlock Text(ListBox list, string value) =>
        list.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == value && text.IsEffectivelyVisible);

    private static string HeadText(Border head) => HeadLabel(head).Text ?? "";

    private static TextBlock HeadLabel(Border head) =>
        head.GetVisualDescendants().OfType<TextBlock>().Single();

    private static double Top(Control control, Visual relative)
    {
        var origin = control.TranslatePoint(default, relative);
        Assert.NotNull(origin);
        return origin.Value.Y;
    }

    private static bool IsClear(IBrush? brush) =>
        brush is null || brush is ISolidColorBrush solid && solid.Color.A == 0;

    private sealed class FileListHost(string git) : IWorkspaceHost
    {
        public IDialogService? Dialogs => null;

        public GitProcessRunner Runner { get; } = new();

        public string? GitExecutable { get; } = git;

        public string? MergeTool => null;

        public bool GitReady => true;

        public void Activate(RepositoryViewModel tab)
        {
        }

        public void Close(RepositoryViewModel tab)
        {
        }

        public void NoteLoaded(RepositoryViewModel tab)
        {
        }

        public void Save()
        {
        }

        public Task OpenRepositoryAsync(string path) => Task.CompletedTask;
    }
}
