using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Sextant.Git.Parsing;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class MergeEditorScrollTests
{
    [Fact]
    public async Task The_merge_editor_opens_on_the_first_conflict()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            ApplicationStyles();
            var editor = new MergeEditor { IsVisible = false };
            var window = new Window
            {
                Width = 900,
                Height = 320,
                Content = editor,
            };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var single = MergeSession.FromPieces(ConflictParser.Parse(Marked(before: 90, second: false)));
            var only = Assert.Single(single.Conflicts);
            editor.Session = single;
            Assert.Equal(0, editor.ConflictIndex);
            editor.IsVisible = true;
            var result = WaitUntilVisible(window, editor, only.ResultOffset);
            var singleLine = result.Document!.GetLineByOffset(only.ResultOffset).LineNumber;
            Assert.True(singleLine > 40, $"single conflict landed on line {singleLine}");
            Assert.Equal(only.ResultOffset, result.CaretOffset);
            var singleOffset = result.TextArea.TextView.VerticalOffset;
            Assert.True(singleOffset > result.TextArea.TextView.DefaultLineHeight * 20, $"offset {singleOffset}");

            var several = MergeSession.FromPieces(ConflictParser.Parse(Marked(before: 220, second: true)));
            Assert.Equal(2, several.ConflictCount);
            editor.Session = several;
            Assert.Equal(0, editor.ConflictIndex);
            var first = several.Conflicts[0];
            var second = several.Conflicts[1];
            result = WaitUntilVisible(window, editor, first.ResultOffset);
            var firstLine = result.Document!.GetLineByOffset(first.ResultOffset).LineNumber;
            var secondLine = result.Document.GetLineByOffset(second.ResultOffset).LineNumber;
            Assert.True(firstLine > 150, $"first conflict landed on line {firstLine}");
            Assert.True(secondLine > firstLine + 40, $"second conflict on line {secondLine}");
            Assert.Equal(first.ResultOffset, result.CaretOffset);
            Assert.False(LineInView(result, secondLine), Describe(result, secondLine));

            window.Close();
            return 0;
        }, CancellationToken.None);
    }

    private static void ApplicationStyles()
    {
        var styles = Application.Current!.Styles;
        var source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml");
        if (styles.OfType<StyleInclude>().Any(include => include.Source == source))
            return;
        styles.Add(new StyleInclude(source) { Source = source });
    }

    private static TextEditor WaitUntilVisible(Window window, MergeEditor editor, int offset)
    {
        TextEditor? result = null;
        for (var i = 0; i < 30; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
            result = editor.GetVisualDescendants().OfType<TextEditor>().Single(item => item.Name == "MergeResult");
            if (result.Document is not null && LineInView(result, result.Document.GetLineByOffset(offset).LineNumber))
                return result;
        }

        Assert.Fail(result is null ? "The result column was not created." : Describe(result, result.Document?.GetLineByOffset(offset).LineNumber ?? 0));
        throw new InvalidOperationException("The result column did not show the conflict.");
    }

    private static bool LineInView(TextEditor editor, int lineNumber)
    {
        var view = editor.TextArea.TextView;
        if (view.Bounds.Height < 1 || view.Document is null)
            return false;
        var top = view.GetVisualTopByDocumentLine(lineNumber);
        return top >= view.VerticalOffset - 1 && top < view.VerticalOffset + view.Bounds.Height - 1;
    }

    private static string Describe(TextEditor editor, int lineNumber)
    {
        var view = editor.TextArea.TextView;
        var top = view.Document is null ? -1 : view.GetVisualTopByDocumentLine(lineNumber);
        return $"line {lineNumber} top {top} offset {view.VerticalOffset} height {view.Bounds.Height} caret {editor.CaretOffset}";
    }

    private static string Marked(int before, bool second)
    {
        var text = new StringBuilder();
        for (var i = 0; i < before; i++)
            text.Append("same\n");
        text.Append("<<<<<<< HEAD\nmain\n=======\nother\n>>>>>>> other\n");
        if (!second)
            return text.ToString();
        for (var i = 0; i < 80; i++)
            text.Append("mid\n");
        text.Append("<<<<<<< HEAD\nmain2\n=======\nother2\n>>>>>>> other\n");
        return text.ToString();
    }
}
