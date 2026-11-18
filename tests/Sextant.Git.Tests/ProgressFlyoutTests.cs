using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.Controls;
using Sextant.Git;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class ProgressFlyoutTests
{
    [Fact]
    public async Task Busy_pill_highlights_and_a_click_shows_progress_lines()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            var vm = new RepositoryViewModel(new Host(), "repo") { BranchText = "main" };
            var chrome = new RepositoryChrome { DataContext = vm, Width = 900, Height = 80 };
            var window = new Window { Content = chrome, Width = 1100, Height = 900 };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var pill = window.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "BranchPill");
            var button = window.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "ProgressButton");
            Assert.NotNull(pill);
            Assert.NotNull(button);
            Assert.False(button.IsVisible);
            Assert.DoesNotContain("busy", pill.Classes);
            Assert.Equal(new Thickness(1), pill.BorderThickness);

            vm.IsBusy = true;
            vm.BusyText = "Fetching…";
            vm.BusyText = "Receiving objects: 10% (1/10)";
            vm.BusyText = "Receiving objects: 40% (4/10), 2.00 MiB | 1.00 MiB/s";
            vm.BusyText = "Counting objects: 5% (1/20)";
            vm.BusyText = "Counting objects: 100% (3/3), done.";
            vm.BusyText = "remote: Counting objects: 100% (10/10), done.";
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var expected =
                "remote: Counting objects: 100% (10/10), done." + Environment.NewLine
                + Environment.NewLine
                + "Counting objects: 100% (3/3), done." + Environment.NewLine
                + "Counting objects: 5% (1/20)" + Environment.NewLine
                + Environment.NewLine
                + "Receiving objects: 40% (4/10), 2.00 MiB | 1.00 MiB/s" + Environment.NewLine
                + "Receiving objects: 10% (1/10)";
            Assert.Equal("Fetching…", vm.ProgressHeadline);
            Assert.Equal(expected, vm.ProgressDetail);
            vm.BusyText = "remote: Counting objects: 100% (10/10), done.";
            Assert.Equal(expected, vm.ProgressDetail);
            Assert.Contains("busy", pill.Classes);
            Assert.Equal(new Thickness(2), pill.BorderThickness);
            Assert.True(Application.Current!.TryGetResource("SystemAccentColor", window.ActualThemeVariant, out var accent));
            var accentColor = Assert.IsType<Color>(accent);
            Assert.Equal(accentColor, Assert.IsAssignableFrom<ISolidColorBrush>(pill.BorderBrush).Color);
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(pill.Background);
            Assert.Equal(accentColor, fill.Color);
            Assert.Equal(0.18, fill.Opacity, 2);
            Assert.True(button.IsEffectivelyVisible);
            Assert.NotEmpty(button.GetVisualDescendants().OfType<LoadingSpinner>());
            var label = button.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == vm.BusyText);
            Assert.Equal(FontWeight.SemiBold, label.FontWeight);
            Assert.True(label.IsEffectivelyVisible);

            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
            Assert.NotNull(point);
            window.MouseDown(point.Value, MouseButton.Left);
            window.MouseUp(point.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var flyout = Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(pill));
            Assert.True(flyout.IsOpen);
            var detail = FindProgressBox(window);
            Assert.NotNull(detail);
            Assert.Equal(expected, detail.Text);
            Assert.DoesNotContain("Fetching…", detail.Text);
            var card = window.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "ProgressFlyoutCard");
            var headline = window.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Name == "ProgressHeadlineText");
            Assert.Equal("Fetching…", headline.Text);
            Assert.Equal(FontWeight.SemiBold, headline.FontWeight);
            var headlineAt = headline.TranslatePoint(default, card);
            var detailAt = detail.TranslatePoint(default, card);
            Assert.NotNull(headlineAt);
            Assert.NotNull(detailAt);
            Assert.True(headlineAt.Value.Y < detailAt.Value.Y, "the command sits above the progress");
            Assert.Equal(0, detail.CaretIndex);
            Assert.True(detail.IsEffectivelyVisible);
            Assert.True(detail.Bounds.Height >= 280, $"progress detail is {detail.Bounds.Height}px tall");
            Assert.True(card.Bounds.Width >= 560, $"progress flyout is {card.Bounds.Width}px wide");
            AssertFitsWithoutScrolling(window);

            vm.BusyText = "Resolving deltas: 100% (8/8), done.";
            Dispatcher.UIThread.RunJobs();
            Assert.StartsWith("Resolving deltas: 100% (8/8), done." + Environment.NewLine, detail.Text);
            Assert.Contains("Receiving objects: 10% (1/10)", detail.Text);
            Assert.Equal("Fetching…", headline.Text);
            Assert.DoesNotContain("Fetching…", detail.Text);
            Assert.Equal(0, detail.CaretIndex);
            AssertFitsWithoutScrolling(window);

            for (var step = 0; step < 30; step++)
                vm.BusyText = $"Pack {step}: objects";
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var packed = detail.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(packed.Extent.Height > packed.Viewport.Height + 1,
                $"a long log should scroll inside the text, extent {packed.Extent.Height} viewport {packed.Viewport.Height}");
            Assert.False(VisibleScrollBars(window, insideText: false).Any());

            var kept = vm.ProgressDetail;
            vm.IsBusy = false;
            vm.BusyText = "";
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.False(button.IsVisible);
            Assert.True(flyout.IsOpen);
            Assert.DoesNotContain("busy", pill.Classes);
            Assert.Equal("Fetching…", vm.ProgressHeadline);
            Assert.Equal(kept, vm.ProgressDetail);
            Assert.Equal(kept, detail.Text);
            var close = window.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "ProgressFlyoutClose");
            var closeCorner = close.TranslatePoint(new Point(close.Bounds.Width, 0), card);
            Assert.NotNull(closeCorner);
            Assert.True(closeCorner.Value.X > card.Bounds.Width - 48, $"close button sits at x {closeCorner.Value.X}");
            Assert.True(closeCorner.Value.Y < 32, $"close button sits at y {closeCorner.Value.Y}");
            var closeAt = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window);
            Assert.NotNull(closeAt);
            window.MouseDown(closeAt.Value, MouseButton.Left);
            window.MouseUp(closeAt.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.False(flyout.IsOpen);

            vm.IsBusy = true;
            vm.BusyText = "Pushing…";
            Assert.Equal("Pushing…", vm.ProgressHeadline);
            Assert.Equal("", vm.ProgressDetail);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var again = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
            Assert.NotNull(again);
            window.MouseDown(again.Value, MouseButton.Left);
            window.MouseUp(again.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(flyout.IsOpen);
            window.MouseDown(new Point(8, 860), MouseButton.Left);
            window.MouseUp(new Point(8, 860), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.False(flyout.IsOpen);
            Assert.True(button.IsEffectivelyVisible);
            window.Close();
        }, CancellationToken.None);
    }

    private static TextBox? FindProgressBox(Window window)
    {
        var box = window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(item => item.Name == "ProgressDetailBox");
        if (box is not null)
            return box;
        if (Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return null;
        foreach (var open in desktop.Windows)
        {
            box = open.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(item => item.Name == "ProgressDetailBox");
            if (box is not null)
                return box;
        }

        return null;
    }

    private static void AssertFitsWithoutScrolling(Window window)
    {
        var presenter = window.GetVisualDescendants().OfType<FlyoutPresenter>().Single();
        var outer = presenter.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.Equal(ScrollBarVisibility.Disabled, outer.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Disabled, outer.VerticalScrollBarVisibility);
        Assert.True(outer.Extent.Width <= outer.Viewport.Width + 1,
            $"flyout extent {outer.Extent.Width} is wider than {outer.Viewport.Width}");
        Assert.True(outer.Extent.Height <= outer.Viewport.Height + 1,
            $"flyout extent {outer.Extent.Height} is taller than {outer.Viewport.Height}");
        var textScroll = window.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "ProgressDetailBox")
            .GetVisualDescendants().OfType<ScrollViewer>().Single();
        Assert.True(textScroll.Extent.Height <= textScroll.Viewport.Height + 1,
            $"text extent {textScroll.Extent.Height} viewport {textScroll.Viewport.Height}");
        Assert.True(textScroll.Extent.Width <= textScroll.Viewport.Width + 1,
            $"text extent {textScroll.Extent.Width} viewport {textScroll.Viewport.Width}");
        Assert.False(VisibleScrollBars(window, insideText: true).Any());
        Assert.False(VisibleScrollBars(window, insideText: false).Any());
    }

    private static IEnumerable<ScrollBar> VisibleScrollBars(Window window, bool insideText)
    {
        return window.GetVisualDescendants().OfType<ScrollBar>().Where(bar =>
            bar.IsVisible && bar.Bounds.Width > 1 && bar.Bounds.Height > 1 && InsideTextBox(bar) == insideText);
    }

    private static bool InsideTextBox(Visual visual)
    {
        for (var parent = visual.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
        {
            if (parent is TextBox)
                return true;
        }

        return false;
    }

    private sealed class Host : IWorkspaceHost
    {
        public IDialogService? Dialogs => null;
        public GitProcessRunner Runner { get; } = new();
        public string? GitExecutable => null;
        public string? MergeTool => null;
        public bool GitReady => false;
        public Task Activate(RepositoryViewModel tab) => Task.CompletedTask;
        public Task Close(RepositoryViewModel tab) => Task.CompletedTask;
        public void NoteLoaded(RepositoryViewModel tab) { }
        public void Save() { }
        public Task OpenRepositoryAsync(string path) => Task.CompletedTask;
    }
}
