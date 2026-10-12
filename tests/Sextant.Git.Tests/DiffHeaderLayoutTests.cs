using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Sextant;
using Sextant.Git;
using Sextant.Git.Models;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class DiffHeaderLayoutTests
{
    [Fact]
    public async Task File_headers_keep_one_left_edge_when_a_file_expands()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        // Returning a value makes Dispatch wait for the async body. A bare async lambda does not.
        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new HeaderHost(), "C:\\repo");
            var open = Header("open.cs");
            open.Expanded = true;
            var lines = new EditorLine[12];
            for (var i = 0; i < lines.Length; i++)
            {
                var number = (i + 1).ToString();
                lines[i] = new EditorLine
                {
                    Text = "changed " + number,
                    OldNumber = number,
                    NewNumber = number,
                    ShowAction = true,
                    ActionLabel = "Stage line",
                };
            }

            vm.DiffRows.Add(Header("closed.cs"));
            vm.DiffRows.Add(open);
            vm.DiffRows.Add(new DiffHunkRow { Header = "@@ -1,2 +1,3 @@", ActionLabel = "Stage hunk", ShowAction = true });
            vm.DiffRows.Add(new DiffEditorRow { Path = "open.cs", Lines = lines });
            vm.DiffRows.Add(Header("after.cs"));

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            try
            {
                Settle(view);
                var list = view.FindControl<CopyListBox>("DiffList");
                Assert.NotNull(list);
                Assert.True(list.Bounds.Width > 200, "diff list " + list.Bounds);
                SameEdge(Edges(list), "expanded");

                vm.DiffRows.Clear();
                vm.DiffRows.Add(Header("closed.cs"));
                vm.DiffRows.Add(Header("open.cs"));
                vm.DiffRows.Add(Header("after.cs"));
                vm.DiffRows.Add(new DiffFileRow { Label = "plain.cs", Path = "plain.cs" });
                Settle(view);
                SameEdge(Edges(list), "collapsed");
            }
            finally
            {
                window.Close();
                await vm.DisposeAsync();
            }

            return 0;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task File_header_button_stages_and_unstages_the_whole_file(bool sideBySide)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.WriteFile("b.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "partially staged\n");
        repo.Run("add", "a.txt");
        repo.WriteFile("a.txt", "all changes\n");
        repo.WriteFile("b.txt", "other changes\n");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new HeaderHost(repo.Git), repo.Directory);
            Window? window = null;
            try
            {
                await vm.EnsureLoadedAsync();
                await vm.ApplyDiffPreferences(sideBySide, ignoreWhitespace: false);
                var file = vm.Files.Single(row => row.Path == "a.txt" && !row.FromStagedList);
                vm.SelectedFile = file;
                await vm.LoadSelectedFileCommand.ExecutionTask!;
                await vm.ExpandAllSectionsCommand.ExecuteAsync(null);
                var header = vm.DiffRows.OfType<DiffFileRow>().Single(row => row.Path == "a.txt" && row.StagingFile?.FromStagedList == false);
                Assert.Equal(3, vm.DiffRows.OfType<DiffFileRow>().Count());
                Assert.Same(file.StageCommand, header.ActionCommand);
                Assert.True(header.ShowAction);
                Assert.True(header.Expanded);
                Assert.Contains(vm.DiffRows.OfType<DiffHunkRow>(), row => row.ShowAction && row.ActionLabel == "Stage hunk");

                var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
                window = new Window { Content = view, Width = 1100, Height = 700 };
                window.Show();
                Settle(view);
                var stage = view.GetVisualDescendants().OfType<Button>()
                    .Single(button => ReferenceEquals(button.DataContext, header) && button.Content as string == "Stage file");
                Assert.True(stage.IsEffectivelyVisible);
                Assert.Same(header.ActionCommand, stage.Command);
                var point = stage.TranslatePoint(new Point(stage.Bounds.Width / 2, stage.Bounds.Height / 2), window);
                Assert.NotNull(point);
                window.MouseDown(point.Value, MouseButton.Left);
                Assert.True(header.Expanded);
                window.MouseUp(point.Value, MouseButton.Left);
                var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(header.ActionCommand);
                Assert.NotNull(command.ExecutionTask);
                await command.ExecutionTask;

                Assert.False(vm.HasBanner, vm.Banner);
                Assert.Equal("all changes\n", repo.RunCapture("show", ":a.txt").Replace("\r\n", "\n", StringComparison.Ordinal));
                Assert.Equal("a.txt", repo.RunCapture("diff", "--cached", "--name-only").Trim());
                Assert.DoesNotContain(vm.Files, row => row.Path == "a.txt" && !row.FromStagedList);
                Assert.Contains(vm.Files, row => row.Path == "b.txt" && !row.FromStagedList);

                var staged = vm.Files.Single(row => row.Path == "a.txt" && row.FromStagedList);
                vm.SelectedFile = staged;
                await vm.LoadSelectedFileCommand.ExecutionTask!;
                header = vm.DiffRows.OfType<DiffFileRow>().Single(row => row.Path == "a.txt" && row.StagingFile?.FromStagedList == true);
                Assert.Contains(vm.DiffRows.OfType<DiffFileRow>(), row => row.Path == "b.txt" && row.StagingFile?.FromStagedList == false);
                Assert.Equal("Unstage file", header.ActionLabel);
                Assert.True(header.ShowAction);
                Assert.Same(staged.UnstageCommand, header.ActionCommand);
                Settle(view);
                var unstage = view.GetVisualDescendants().OfType<Button>()
                    .Single(button => ReferenceEquals(button.DataContext, header) && button.Content as string == "Unstage file");
                Assert.True(unstage.IsEffectivelyVisible);
                Assert.Same(header.ActionCommand, unstage.Command);
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(header.ActionCommand).ExecuteAsync(null);
                Assert.Equal("", repo.RunCapture("diff", "--cached", "--name-only").Trim());
                Assert.Contains(vm.Files, row => row.Path == "a.txt" && !row.FromStagedList);
            }
            finally
            {
                window?.Close();
                await vm.DisposeAsync();
            }
            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Discard_hunk_sits_beside_stage_hunk_and_restores_that_change()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        using var repo = new TempRepo();
        var lines = Enumerable.Range(0, 40).Select(index => $"line {index}").ToArray();
        repo.WriteFile("a.txt", string.Join('\n', lines) + "\n");
        repo.WriteFile("b.txt", "one\n");
        repo.CommitAll("base");
        repo.WriteFile("b.txt", "staged\n");
        repo.Run("add", "b.txt");
        lines[2] = "changed-a";
        lines[18] = "changed-b";
        lines[34] = "changed-c";
        repo.WriteFile("a.txt", string.Join('\n', lines) + "\n");

        var dialogs = new DiscardDialogs();
        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new HeaderHost(repo.Git, dialogs), repo.Directory);
            Window? window = null;
            try
            {
                await vm.EnsureLoadedAsync();
                await vm.ExpandAllSectionsCommand.ExecuteAsync(null);
                var unstaged = vm.DiffRows.OfType<DiffHunkRow>().Where(row => row.ActionLabel == "Stage hunk").ToList();
                var staged = vm.DiffRows.OfType<DiffHunkRow>().Where(row => row.ActionLabel == "Unstage hunk").ToList();
                Assert.Equal(3, unstaged.Count);
                Assert.All(unstaged, row => Assert.True(row.ShowDiscard));
                var stagedHunk = Assert.Single(staged);
                Assert.False(stagedHunk.ShowDiscard);

                var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 900 };
                window = new Window { Content = view, Width = 1100, Height = 900 };
                window.Show();
                var list = view.FindControl<CopyListBox>("DiffList");
                Assert.NotNull(list);
                list.ScrollIntoView(unstaged[1]);
                Settle(view);
                var discard = ButtonOn(view, unstaged[1], "Discard hunk");
                var stage = ButtonOn(view, unstaged[1], "Stage hunk");
                Assert.True(discard.IsEffectivelyVisible);
                Assert.True(stage.IsEffectivelyVisible);
                Assert.Same(unstaged[1].DiscardCommand, discard.Command);
                Assert.Same(unstaged[1].ActionCommand, stage.Command);
                var discardAt = discard.TranslatePoint(default, list);
                var stageAt = stage.TranslatePoint(default, list);
                Assert.NotNull(discardAt);
                Assert.NotNull(stageAt);
                Assert.True(discardAt.Value.X + discard.Bounds.Width <= stageAt.Value.X + 1,
                    $"discard {discardAt.Value.X} stage {stageAt.Value.X}");
                Assert.True(Math.Abs(discardAt.Value.Y - stageAt.Value.Y) < 2, "discard and stage hunk are on one row");

                list.ScrollIntoView(stagedHunk);
                Settle(view);
                Assert.True(ButtonOn(view, stagedHunk, "Unstage hunk").IsEffectivelyVisible);
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(),
                    button => ReferenceEquals(button.DataContext, stagedHunk)
                        && button.IsEffectivelyVisible
                        && button.Content as string == "Discard hunk");

                list.ScrollIntoView(unstaged[1]);
                Settle(view);
                discard = ButtonOn(view, unstaged[1], "Discard hunk");
                await Click(window, discard);
                Assert.Equal(1, dialogs.Confirmations);
                Assert.Equal("Discard hunk", dialogs.Title);
                Assert.Equal("Discard hunk", dialogs.Confirm);
                Assert.Contains("a.txt", dialogs.Message, StringComparison.Ordinal);
                Assert.Contains("changed-b", Read(repo), StringComparison.Ordinal);

                dialogs.Accept = true;
                discard = ButtonOn(view, unstaged[1], "Discard hunk");
                await Click(window, discard);
                Assert.Equal(2, dialogs.Confirmations);
                Assert.False(vm.HasBanner, vm.Banner);
                var text = Read(repo);
                Assert.Contains("changed-a", text, StringComparison.Ordinal);
                Assert.DoesNotContain("changed-b", text, StringComparison.Ordinal);
                Assert.Contains("changed-c", text, StringComparison.Ordinal);
                Assert.Equal("b.txt", repo.RunCapture("diff", "--cached", "--name-only").Trim());
            }
            finally
            {
                window?.Close();
                await vm.DisposeAsync();
            }

            return 0;
        }, CancellationToken.None);
    }

    private static Button ButtonOn(Visual root, object row, string content) =>
        root.GetVisualDescendants().OfType<Button>().Single(button =>
            ReferenceEquals(button.DataContext, row) && button.Content as string == content);

    private static async Task Click(Window window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
        var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(button.Command);
        Assert.NotNull(command.ExecutionTask);
        await command.ExecutionTask;
    }

    private static string Read(TempRepo repo) =>
        File.ReadAllText(Path.Combine(repo.Directory, "a.txt")).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static DiffFileRow Header(string name) => new()
    {
        Label = name,
        Path = name,
        CanFold = true,
        StagingFile = new FileRowViewModel { Path = name, ShowStage = true },
    };

    private static void Settle(Control view)
    {
        for (var pass = 0; pass < 6; pass++)
        {
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private readonly record struct Edge(string Name, double Border, double Label, double PadLeft);

    private static List<Edge> Edges(CopyListBox list)
    {
        var edges = new List<Edge>();
        foreach (var label in list.GetVisualDescendants().OfType<TextBlock>())
        {
            if (label.Text is not { Length: > 3 } text || !text.EndsWith(".cs", StringComparison.Ordinal) || !label.IsEffectivelyVisible)
                continue;
            if (label.FindAncestorOfType<ListBoxItem>() is not { } item)
                continue;
            var border = label.FindAncestorOfType<Border>();
            if (border is null)
                continue;
            edges.Add(new Edge(text, X(border, list), X(label, list), item.Padding.Left));
        }

        return edges;
    }

    private static void SameEdge(IReadOnlyList<Edge> edges, string when)
    {
        Assert.True(edges.Count >= 2, when + " realized " + edges.Count + " file headers");
        var border = edges.Max(edge => edge.Border) - edges.Min(edge => edge.Border);
        var label = edges.Max(edge => edge.Label) - edges.Min(edge => edge.Label);
        var pad = edges.Max(edge => edge.PadLeft) - edges.Min(edge => edge.PadLeft);
        Assert.True(border <= 1 && label <= 1 && pad <= 0.5, when + " " + string.Join(" | ", edges));
    }

    private static double X(Visual visual, Visual relative)
    {
        var point = visual.TranslatePoint(default, relative);
        Assert.NotNull(point);
        return point.Value.X;
    }

    private sealed class DiscardDialogs : IDialogService
    {
        public bool Accept { get; set; }

        public string Title { get; private set; } = "";

        public string Message { get; private set; } = "";

        public string Confirm { get; private set; } = "";

        public int Confirmations { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message, string confirm = "OK")
        {
            Title = title;
            Message = message;
            Confirm = confirm;
            Confirmations++;
            return Task.FromResult(Accept);
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

    private sealed class HeaderHost(string? git = null, IDialogService? dialogs = null) : IWorkspaceHost
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
}
