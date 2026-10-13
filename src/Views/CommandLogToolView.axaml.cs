using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant;
using Sextant.ViewModels;

namespace Sextant.Views;

public partial class CommandLogToolView : UserControl
{
    private string _appliedCommandLog = "";
    private RepositoryViewModel? _watched;

    public CommandLogToolView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        CommandLogSelectAllItem.InputGesture = AppGestures.CommandKey(Key.A);
        Watch();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (IsLoaded)
            Watch();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        Unwatch();
        base.OnUnloaded(e);
    }

    private void Watch()
    {
        var next = DataContext as RepositoryViewModel;
        if (ReferenceEquals(_watched, next))
            return;
        Unwatch();
        _watched = next;
        if (_watched is null)
            return;
        _watched.PropertyChanged += OnViewModelPropertyChanged;
        ApplyCommandLog(_watched.CommandLog);
    }

    private void Unwatch()
    {
        if (_watched is null)
            return;
        _watched.PropertyChanged -= OnViewModelPropertyChanged;
        _watched = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not RepositoryViewModel vm)
            return;
        if (e.PropertyName == nameof(RepositoryViewModel.CommandLog))
            ApplyCommandLog(vm.CommandLog);
        else if (e.PropertyName == nameof(RepositoryViewModel.CommandsOpen) && vm.CommandsOpen)
            Dispatcher.UIThread.Post(ScrollCommandLogToEnd, DispatcherPriority.Loaded);
    }

    private void ApplyCommandLog(string text)
    {
        if (_appliedCommandLog == text && (CommandLogBox.Text ?? "") == text)
            return;
        var box = CommandLogBox;
        var previous = box.Text ?? "";
        var start = box.SelectionStart;
        var end = box.SelectionEnd;
        var selected = start != end;
        var pinned = IsCommandLogPinned();
        var scroll = CommandLogScroll();
        var offset = scroll?.Offset ?? default;
        _appliedCommandLog = text;
        box.Text = text;
        if (selected && end <= text.Length && text.StartsWith(previous, StringComparison.Ordinal))
        {
            box.SelectionStart = start;
            box.SelectionEnd = end;
            HoldCommandLogScroll(offset);
            return;
        }

        if (!pinned && previous.Length > 0 && scroll is not null)
        {
            HoldCommandLogScroll(offset);
            return;
        }

        ScrollCommandLogToEnd();
    }

    private void HoldCommandLogScroll(Vector offset)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var later = CommandLogScroll();
            if (later is not null)
                later.Offset = offset;
        }, DispatcherPriority.Loaded);
    }

    private void ScrollCommandLogToEnd()
    {
        var box = CommandLogBox;
        box.CaretIndex = box.Text?.Length ?? 0;
        var lines = box.GetLineCount();
        if (lines > 0)
            box.ScrollToLine(lines - 1);
        Dispatcher.UIThread.Post(() =>
        {
            var later = CommandLogBox;
            if ((later.Text ?? "") != _appliedCommandLog)
                return;
            later.CaretIndex = _appliedCommandLog.Length;
            var count = later.GetLineCount();
            if (count > 0)
                later.ScrollToLine(count - 1);
        }, DispatcherPriority.Loaded);
    }

    private bool IsCommandLogPinned()
    {
        var scroll = CommandLogScroll();
        if (scroll is null || scroll.Extent.Height <= scroll.Viewport.Height + 1)
            return true;
        return scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 8;
    }

    private ScrollViewer? CommandLogScroll() =>
        CommandLogBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void OnCommandLogMenuOpening(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout flyout)
            return;
        foreach (var item in flyout.Items.OfType<MenuItem>())
        {
            if (item.Header is "Copy")
                item.IsEnabled = CommandLogBox.CanCopy;
            else if (item.Header is "Select all")
                item.IsEnabled = (CommandLogBox.Text?.Length ?? 0) > 0;
        }
    }
}
