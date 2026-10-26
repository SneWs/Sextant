using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class LineStageMenuTests
{
    [Fact]
    public async Task Selected_lines_stage_and_unstage_from_the_diff_menu()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "one\nalpha\nbeta\ngamma\n");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new MenuHost(repo.Git), repo.Directory);
            Window? window = null;
            try
            {
                await vm.EnsureLoadedAsync();
                Assert.False(vm.SideBySide);
                await vm.ExpandAllSectionsCommand.ExecuteAsync(null);
                var unstaged = EditorFor(vm, staged: false);
                Assert.True(unstaged.Lines.Single(line => line.Text == "one").PatchLine < 0);
                var alpha = unstaged.Lines.Single(line => line.Text == "alpha").PatchLine;
                var beta = unstaged.Lines.Single(line => line.Text == "beta").PatchLine;
                Assert.True(alpha >= 0 && beta > alpha);

                var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
                window = new Window { Content = view, Width = 1100, Height = 700 };
                window.Show();
                var editor = ShowEditor(view, unstaged);
                Select(editor, unstaged, "alpha", "alpha");
                var single = OpenMenu(editor);
                Assert.Equal(["Stage line", "Copy", "Select all"], VisibleHeaders(single));
                var singleItem = Item(single, "Stage line");
                Assert.Equal([alpha], Assert.IsAssignableFrom<IReadOnlyList<int>>(singleItem.CommandParameter));
                single.Close();

                PlaceCaret(editor, unstaged, "alpha");
                single = OpenMenu(editor);
                Assert.Equal(["Stage line", "Copy", "Select all"], VisibleHeaders(single));
                Assert.Equal([alpha], Assert.IsAssignableFrom<IReadOnlyList<int>>(Item(single, "Stage line").CommandParameter));
                single.Close();

                Select(editor, unstaged, "one", "one");
                single = OpenMenu(editor);
                Assert.Equal(["Copy", "Select all"], VisibleHeaders(single));
                single.Close();

                Select(editor, unstaged, "alpha", "alpha");
                single = OpenMenu(editor);
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(Item(single, "Stage line").Command).ExecuteAsync(Item(single, "Stage line").CommandParameter);
                single.Close();

                Assert.False(vm.HasBanner, vm.Banner);
                Assert.Equal("one\nalpha\n", Text(repo.RunCapture("show", ":a.txt")));
                Assert.Equal("one\nalpha\nbeta\ngamma\n", Text(File.ReadAllText(Path.Combine(repo.Directory, "a.txt"))));
                var unstagedDiff = repo.RunCapture("diff", "--", "a.txt");
                Assert.Contains("+beta", unstagedDiff, StringComparison.Ordinal);
                Assert.Contains("+gamma", unstagedDiff, StringComparison.Ordinal);
                Assert.DoesNotContain("+alpha", unstagedDiff, StringComparison.Ordinal);

                unstaged = EditorFor(vm, staged: false);
                var betaLeft = unstaged.Lines.Single(line => line.Text == "beta").PatchLine;
                var gammaLeft = unstaged.Lines.Single(line => line.Text == "gamma").PatchLine;
                editor = ShowEditor(view, unstaged);
                Select(editor, unstaged, "beta", "gamma");
                var menu = OpenMenu(editor);
                Assert.Equal(["Stage selected lines", "Copy", "Select all"], VisibleHeaders(menu));
                var item = Item(menu, "Stage selected lines");
                Assert.Equal([betaLeft, gammaLeft], Assert.IsAssignableFrom<IReadOnlyList<int>>(item.CommandParameter));
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(item.Command).ExecuteAsync(item.CommandParameter);
                menu.Close();

                Assert.False(vm.HasBanner, vm.Banner);
                Assert.Equal("one\nalpha\nbeta\ngamma\n", Text(repo.RunCapture("show", ":a.txt")));
                Assert.Equal("one\nalpha\nbeta\ngamma\n", Text(File.ReadAllText(Path.Combine(repo.Directory, "a.txt"))));

                var staged = EditorFor(vm, staged: true);
                var stagedAlpha = staged.Lines.Single(line => line.Text == "alpha").PatchLine;
                editor = ShowEditor(view, staged);
                Select(editor, staged, "alpha", "alpha");
                menu = OpenMenu(editor);
                Assert.Equal(["Unstage line", "Copy", "Select all"], VisibleHeaders(menu));
                item = Item(menu, "Unstage line");
                Assert.Equal([stagedAlpha], Assert.IsAssignableFrom<IReadOnlyList<int>>(item.CommandParameter));
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(item.Command).ExecuteAsync(item.CommandParameter);
                menu.Close();

                Assert.False(vm.HasBanner, vm.Banner);
                Assert.Equal("one\nbeta\ngamma\n", Text(repo.RunCapture("show", ":a.txt")));
                Assert.Equal("one\nalpha\nbeta\ngamma\n", Text(File.ReadAllText(Path.Combine(repo.Directory, "a.txt"))));

                staged = EditorFor(vm, staged: true);
                editor = ShowEditor(view, staged);
                Select(editor, staged, "beta", "gamma");
                menu = OpenMenu(editor);
                Assert.Equal(["Unstage selected lines", "Copy", "Select all"], VisibleHeaders(menu));
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(Item(menu, "Unstage selected lines").Command)
                    .ExecuteAsync(Item(menu, "Unstage selected lines").CommandParameter);
                menu.Close();

                Assert.False(vm.HasBanner, vm.Banner);
                Assert.Equal("", repo.RunCapture("diff", "--cached", "--name-only").Trim());
                Assert.Equal("one\nalpha\nbeta\ngamma\n", Text(File.ReadAllText(Path.Combine(repo.Directory, "a.txt"))));
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
    public async Task Side_by_side_stages_the_line_on_its_own_column()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "one\n\nAdding more lines...!\n");
        repo.Run("add", "a.txt");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new MenuHost(repo.Git), repo.Directory) { SideBySide = true };
            Window? window = null;
            try
            {
                await vm.EnsureLoadedAsync();
                Assert.True(vm.SideBySide);
                await vm.ExpandAllSectionsCommand.ExecuteAsync(null);
                var staged = EditorFor(vm, staged: true);
                Assert.True(staged.SideBySide);
                var blank = staged.RightLines.Single(line => line.Kind == EditorLineKind.Added && line.Text == "");
                var text = staged.RightLines.Single(line => line.Text == "Adding more lines...!");
                Assert.True(blank.ShowAction && text.ShowAction);
                Assert.Equal("Unstage line", text.ActionLabel);
                Assert.True(blank.PatchLine >= 0 && text.PatchLine > blank.PatchLine);
                Assert.All(staged.Lines, line => Assert.False(line.ShowAction));

                var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
                window = new Window { Content = view, Width = 1100, Height = 700 };
                window.Show();
                var (left, right) = ShowSides(view, staged);
                Assert.DoesNotContain(left.TextArea.LeftMargins, margin => margin.Bounds.Width >= 90);
                Assert.Contains(right.TextArea.LeftMargins, margin => margin.Bounds.Width >= 90);

                var leftMenu = OpenMenu(left);
                Assert.Equal(["Copy", "Select all"], VisibleHeaders(leftMenu));
                leftMenu.Close();

                Select(right, staged.RightLines, "Adding more lines...!", "Adding more lines...!");
                var menu = OpenMenu(right);
                Assert.Equal(["Unstage line", "Copy", "Select all"], VisibleHeaders(menu));
                var item = Item(menu, "Unstage line");
                Assert.Equal([text.PatchLine], Assert.IsAssignableFrom<IReadOnlyList<int>>(item.CommandParameter));
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(item.Command).ExecuteAsync(item.CommandParameter);
                menu.Close();

                Assert.False(vm.HasBanner, vm.Banner);
                Assert.Equal("one\n\n", Text(repo.RunCapture("show", ":a.txt")));
                Assert.Equal("one\n\nAdding more lines...!\n", Text(File.ReadAllText(Path.Combine(repo.Directory, "a.txt"))));

                staged = EditorFor(vm, staged: true);
                (_, right) = ShowSides(view, staged);
                PlaceCaret(right, staged.RightLines, "");
                menu = OpenMenu(right);
                Assert.Equal(["Unstage line", "Copy", "Select all"], VisibleHeaders(menu));
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(Item(menu, "Unstage line").Command)
                    .ExecuteAsync(Item(menu, "Unstage line").CommandParameter);
                menu.Close();

                Assert.False(vm.HasBanner, vm.Banner);
                Assert.Equal("", repo.RunCapture("diff", "--cached", "--name-only").Trim());
                Assert.Equal("one\n\nAdding more lines...!\n", Text(File.ReadAllText(Path.Combine(repo.Directory, "a.txt"))));
            }
            finally
            {
                window?.Close();
                await vm.DisposeAsync();
            }

            return 0;
        }, CancellationToken.None);
    }

    private static DiffEditorRow EditorFor(RepositoryViewModel vm, bool staged)
    {
        var header = vm.DiffRows.OfType<DiffFileRow>().Single(row => row.Path == "a.txt" && row.StagingFile?.FromStagedList == staged);
        return vm.DiffRows.Skip(vm.DiffRows.IndexOf(header) + 1).OfType<DiffEditorRow>().First();
    }

    private static TextEditor ShowEditor(RepositoryView view, DiffEditorRow row)
    {
        var list = view.FindControl<CopyListBox>("DiffList");
        Assert.NotNull(list);
        list.ScrollIntoView(row);
        Settle(view);
        var host = view.GetVisualDescendants().OfType<DiffTextHost>().Single(item => ReferenceEquals(item.Editor, row));
        return Assert.Single(host.GetVisualDescendants().OfType<TextEditor>());
    }

    private static (TextEditor Left, TextEditor Right) ShowSides(RepositoryView view, DiffEditorRow row)
    {
        var list = view.FindControl<CopyListBox>("DiffList");
        Assert.NotNull(list);
        list.ScrollIntoView(row);
        Settle(view);
        var host = view.GetVisualDescendants().OfType<DiffTextHost>().Single(item => ReferenceEquals(item.Editor, row));
        var editors = host.GetVisualDescendants().OfType<TextEditor>().ToArray();
        Assert.Equal(2, editors.Length);
        return (editors[0], editors[1]);
    }

    private static MenuItem Item(ContextMenu menu, string header) =>
        menu.Items.OfType<MenuItem>().Single(entry => entry.Header as string == header);

    private static void PlaceCaret(TextEditor editor, DiffEditorRow row, string text) =>
        PlaceCaret(editor, row.Lines, text);

    private static void PlaceCaret(TextEditor editor, IReadOnlyList<EditorLine> lines, string text)
    {
        var index = lines.ToList().FindIndex(line => line.Text == text);
        Assert.True(index >= 0);
        var document = editor.Document;
        Assert.NotNull(document);
        editor.Select(document.GetLineByNumber(index + 1).Offset, 0);
    }

    private static void Select(TextEditor editor, DiffEditorRow row, string from, string to) =>
        Select(editor, row.Lines, from, to);

    private static void Select(TextEditor editor, IReadOnlyList<EditorLine> lines, string from, string to)
    {
        var first = lines.ToList().FindIndex(line => line.Text == from);
        var last = lines.ToList().FindIndex(line => line.Text == to);
        Assert.True(first >= 0 && last >= first);
        var document = editor.Document;
        Assert.NotNull(document);
        var start = document.GetLineByNumber(first + 1).Offset;
        var end = document.GetLineByNumber(last + 1).EndOffset;
        editor.Select(start, end - start);
    }

    private static ContextMenu OpenMenu(TextEditor editor)
    {
        var menu = editor.ContextMenu;
        Assert.NotNull(menu);
        // A right-click lands on the text area and bubbles to the editor menu.
        editor.TextArea.RaiseEvent(new ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();
        return menu;
    }

    private static string[] VisibleHeaders(ContextMenu menu) =>
        menu.Items.OfType<MenuItem>().Where(item => item.IsVisible).Select(item => item.Header as string ?? "").ToArray();

    private static void Settle(Control view)
    {
        for (var pass = 0; pass < 6; pass++)
        {
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static string Text(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed class MenuHost(string git) : IWorkspaceHost
    {
        public IDialogService? Dialogs => null;

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
