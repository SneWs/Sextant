using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant;
using Sextant.Git;
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

    private static DiffFileRow Header(string name) => new()
    {
        Label = name,
        Path = name,
        CanFold = true,
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

    private sealed class HeaderHost : IWorkspaceHost
    {
        public IDialogService? Dialogs => null;

        public GitProcessRunner Runner { get; } = new();

        public string? GitExecutable => null;

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
