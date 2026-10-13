using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.ViewModels;

namespace Sextant.Views;

public partial class StagingToolView : UserControl
{
    private readonly record struct FileAnchor(bool Header, string Key, bool FromStaged, double Top);

    private RepositoryViewModel? _scrollVm;

    public StagingToolView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        HookFileScroll();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (IsLoaded)
            HookFileScroll();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        UnhookFileScroll();
        base.OnUnloaded(e);
    }

    private async void OnFilePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        if (e.Source is not Visual source)
            return;
        if (source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null)
            return;
        if (source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;
        if (DataContext is RepositoryViewModel vm)
            await vm.RevealSelectedFile();
    }

    private void HookFileScroll()
    {
        UnhookFileScroll();
        if (DataContext is not RepositoryViewModel vm)
            return;
        _scrollVm = vm;
        vm.PreserveFileScroll += OnPreserveFileScroll;
    }

    private void UnhookFileScroll()
    {
        if (_scrollVm is null)
            return;
        _scrollVm.PreserveFileScroll -= OnPreserveFileScroll;
        _scrollVm = null;
    }

    private void OnPreserveFileScroll(Action update)
    {
        var scroll = FileScroll();
        var anchors = CaptureFileAnchors(scroll);
        var offset = scroll?.Offset ?? default;
        var restoreAutoScroll = FileList.AutoScrollToSelectedItem;
        FileList.AutoScrollToSelectedItem = false;
        try
        {
            update();
        }
        finally
        {
            Dispatcher.UIThread.Post(
                () => FinishFileScroll(anchors, offset, restoreAutoScroll, pass: 0),
                DispatcherPriority.Loaded);
        }
    }

    private void FinishFileScroll(List<FileAnchor> anchors, Vector offset, bool restoreAutoScroll, int pass)
    {
        var scroll = FileScroll();
        if (scroll is null)
        {
            FileList.AutoScrollToSelectedItem = restoreAutoScroll;
            return;
        }

        if (pass == 0)
        {
            scroll.Offset = offset;
            Dispatcher.UIThread.Post(
                () => FinishFileScroll(anchors, offset, restoreAutoScroll, pass: 1),
                DispatcherPriority.Loaded);
            return;
        }

        var corrected = scroll.Offset;
        foreach (var anchor in anchors)
        {
            if (FindContainer(anchor) is not { } container)
                continue;
            var point = container.TranslatePoint(default, scroll);
            if (point is null)
                continue;
            var delta = point.Value.Y - anchor.Top;
            corrected = new Vector(scroll.Offset.X, Math.Max(0, scroll.Offset.Y + delta));
            if (Math.Abs(delta) > 0.5)
                scroll.Offset = corrected;
            else
                corrected = scroll.Offset;
            break;
        }

        FileList.AutoScrollToSelectedItem = restoreAutoScroll;
        var hold = corrected;
        Dispatcher.UIThread.Post(() =>
        {
            var later = FileScroll();
            if (later is not null)
                later.Offset = hold;
        }, DispatcherPriority.Background);
    }

    private List<FileAnchor> CaptureFileAnchors(ScrollViewer? scroll)
    {
        var anchors = new List<FileAnchor>();
        if (scroll is null)
            return anchors;
        foreach (var container in FileList.GetRealizedContainers().OfType<Control>())
        {
            if (FileList.ItemFromContainer(container) is not FileRowViewModel row)
                continue;
            var point = container.TranslatePoint(default, scroll);
            if (point is null)
                continue;
            var top = point.Value.Y;
            if (top + container.Bounds.Height <= 0 || top >= scroll.Bounds.Height)
                continue;
            anchors.Add(new FileAnchor(row.IsHeader, row.IsHeader ? row.Label : row.Path, row.FromStagedList, top));
        }

        anchors.Sort(static (left, right) => left.Top.CompareTo(right.Top));
        return anchors;
    }

    private Control? FindContainer(FileAnchor anchor)
    {
        foreach (var container in FileList.GetRealizedContainers().OfType<Control>())
        {
            if (FileList.ItemFromContainer(container) is not FileRowViewModel row)
                continue;
            if (row.IsHeader)
            {
                if (anchor.Header && row.Label == anchor.Key)
                    return container;
                continue;
            }

            if (!anchor.Header && row.Path == anchor.Key && row.FromStagedList == anchor.FromStaged)
                return container;
        }

        return null;
    }

    private ScrollViewer? FileScroll() =>
        FileList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void OnMenuOpening(object? sender, CancelEventArgs e) => MenuOpen.OnOpening(sender, e);
}
