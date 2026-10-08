using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant;
using Sextant.Git.Parsing;
using Sextant.ViewModels;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Sextant.Views;

public partial class RepositoryView : UserControl
{
    private readonly record struct FileAnchor(bool Header, string Key, bool FromStaged, double Top);

    private bool _widthsApplied;
    private bool _scrollHooked;
    private RepositoryViewModel? _scrollVm;
    private RepositoryViewModel? _watched;
    private ObservableCollection<DiffRow>? _diffRows;
    private string? _jumpPath;
    private string? _jumpOriginal;
    private string _appliedCommandLog = "";
    private bool _sideScrollHooked;
    private bool _sideScrollQueued;
    private bool _resetSideScroll;
    private double _charWidth;

    /// <summary>Shared horizontal offset of the two side-by-side columns, in pixels.</summary>
    public static readonly StyledProperty<double> SideShiftProperty =
        AvaloniaProperty.Register<RepositoryView, double>(nameof(SideShift));

    public double SideShift
    {
        get => GetValue(SideShiftProperty);
        set => SetValue(SideShiftProperty, value);
    }

    public RepositoryView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DiffFont.Changed += OnFontChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DiffFont.Changed -= OnFontChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnFontChanged()
    {
        _charWidth = 0;
        Dispatcher.UIThread.Post(RefreshFontLayout, DispatcherPriority.Background);
    }

    private void RefreshFontLayout()
    {
        foreach (var panel in DiffList.GetVisualDescendants().OfType<DiffVirtualizingPanel>())
            panel.ResetHeights();
        UpdateSideScroll();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        HookSideScroll();
        if (DataContext is RepositoryViewModel vm && !_widthsApplied)
        {
            _widthsApplied = true;
            ApplyWidths(vm);
        }

        WatchPanes();
        AttachGraphScroll();
        GraphList.TemplateApplied += (_, _) => AttachGraphScroll();
        HookFileScroll();
        WatchViewModel();
        CommandLogSelectAllItem.InputGesture = AppGestures.CommandKey(Key.A);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (IsLoaded)
        {
            HookFileScroll();
            WatchViewModel();
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_widthsApplied)
            PublishPanes();
        UnwatchPanes();
        UnhookFileScroll();
        UnwatchViewModel();
        base.OnUnloaded(e);
    }

    private void WatchViewModel()
    {
        var next = DataContext as RepositoryViewModel;
        if (ReferenceEquals(_watched, next))
            return;
        UnwatchViewModel();
        _watched = next;
        if (_watched is null)
            return;
        _watched.PropertyChanged += OnViewModelPropertyChanged;
        _watched.JumpToFile += OnJumpToFile;
        _diffRows = _watched.DiffRows;
        _diffRows.CollectionChanged += OnDiffRowsChanged;
        ApplyCommandLog(_watched.CommandLog);
        _resetSideScroll = true;
        QueueSideScroll();
    }

    private void UnwatchViewModel()
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnViewModelPropertyChanged;
            _watched.JumpToFile -= OnJumpToFile;
            _watched = null;
        }

        if (_diffRows is not null)
        {
            _diffRows.CollectionChanged -= OnDiffRowsChanged;
            _diffRows = null;
        }
    }

    private void OnDiffHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || !e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;
        ICommand? command = border.DataContext switch
        {
            DiffFileRow { CanFold: true } row => row.ToggleCommand,
            _ => null,
        };
        if (command is null || !command.CanExecute(null))
            return;
        command.Execute(null);
        e.Handled = true;
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

    private void OnJumpToFile(string path, string? original)
    {
        _jumpPath = path;
        _jumpOriginal = original;
        ScrollDiffTo(path, original);
    }

    private void OnDiffRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
            _resetSideScroll = true;
        QueueSideScroll();
    }

    private void ScrollDiffTo(string path, string? original)
    {
        if (FindHeader(path, original) is not { } row)
            return;
        DiffList.ScrollIntoView(row);
        Dispatcher.UIThread.Post(() => AlignDiff(row, 0), DispatcherPriority.Loaded);
    }

    private void AlignDiff(DiffFileRow row, int pass)
    {
        var scroll = DiffList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var container = DiffList.ContainerFromItem(row) as Control;
        if (scroll is null || container is null)
        {
            if (pass < 6)
                Dispatcher.UIThread.Post(() => AlignDiff(row, pass + 1), DispatcherPriority.Loaded);
            return;
        }

        var point = container.TranslatePoint(default, scroll);
        if (point is null)
            return;
        var y = Math.Max(0, scroll.Offset.Y + point.Value.Y);
        if (Math.Abs(scroll.Offset.Y - y) > 0.5)
            scroll.Offset = new Vector(scroll.Offset.X, y);
        if (pass < 2)
            Dispatcher.UIThread.Post(() => AlignDiff(row, pass + 1), DispatcherPriority.Background);
    }

    private DiffFileRow? FindHeader(string path, string? original)
    {
        foreach (var item in DiffList.Items)
        {
            if (item is DiffFileRow row && MatchesFile(row.Path.Length > 0 ? row.Path : row.Label, path, original)
                && (row.StagingFile is null || DataContext is not RepositoryViewModel { SelectedFile: { } selected }
                    || row.StagingFile.FromStagedList == selected.FromStagedList))
                return row;
        }

        return null;
    }

    private static bool MatchesFile(string candidate, string path, string? original) =>
        DiffParser.SameFile(candidate, path)
        || (original is { Length: > 0 } old && DiffParser.SameFile(candidate, old));

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not RepositoryViewModel vm)
            return;
        if (e.PropertyName == nameof(RepositoryViewModel.ShowHistorySearch) && vm.ShowHistorySearch)
        {
            Dispatcher.UIThread.Post(() =>
            {
                HistorySearchBox.Focus();
                HistorySearchBox.SelectAll();
            }, DispatcherPriority.Background);
        }
        else if (e.PropertyName == nameof(RepositoryViewModel.LocationFilterOpen) && vm.LocationFilterOpen)
        {
            Dispatcher.UIThread.Post(() =>
            {
                LocationFilterBox.Focus();
                LocationFilterBox.SelectAll();
            }, DispatcherPriority.Background);
        }
        else if (e.PropertyName == nameof(RepositoryViewModel.CommandLog))
            ApplyCommandLog(vm.CommandLog);
        else if (e.PropertyName == nameof(RepositoryViewModel.CommandsOpen) && vm.CommandsOpen)
            Dispatcher.UIThread.Post(ScrollCommandLogToEnd, DispatcherPriority.Loaded);
        else if (_widthsApplied && e.PropertyName is nameof(RepositoryViewModel.LocationsWidth) or nameof(RepositoryViewModel.GraphWidth) or nameof(RepositoryViewModel.FilesHeight) or nameof(RepositoryViewModel.ShowLocations))
            ApplyWidths(vm);
        else if (e.PropertyName is nameof(RepositoryViewModel.SideBySide) or nameof(RepositoryViewModel.ShowingDiff))
            QueueSideScroll();
    }

    private void HookSideScroll()
    {
        if (_sideScrollHooked)
            return;
        _sideScrollHooked = true;
        DiffList.AddHandler(InputElement.PointerWheelChangedEvent, OnDiffWheel, RoutingStrategies.Tunnel);
        DiffList.SizeChanged += (_, _) => QueueSideScroll();
        SideBar.PropertyChanged += (_, change) =>
        {
            if (change.Property == RangeBase.ValueProperty)
                SideShift = SideBar.Value;
        };
    }

    private void OnDiffWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!SideBar.IsVisible)
            return;
        double delta;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            delta = e.Delta.Y;
        else if (Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
            delta = e.Delta.X;
        else
            return;
        SideBar.Value = Math.Clamp(SideBar.Value - delta * 48, SideBar.Minimum, SideBar.Maximum);
        e.Handled = true;
    }

    private void QueueSideScroll()
    {
        if (_sideScrollQueued)
            return;
        _sideScrollQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _sideScrollQueued = false;
            UpdateSideScroll();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// The list measures each row at the pane width, so a folded line is clipped.
    /// One bar slides both columns by the same amount.
    /// </summary>
    private void UpdateSideScroll()
    {
        if (DataContext is not RepositoryViewModel { SideBySide: true, ShowingDiff: true })
        {
            HideSideScroll();
            return;
        }

        var pane = (DiffList.Bounds.Width - 20) / 2;
        if (pane < 1)
            return;

        var content = LongestSide(_diffRows) * CharWidth + 24;
        var max = Math.Max(0, content - pane);
        SideBar.Maximum = max;
        SideBar.ViewportSize = pane;
        SideBar.LargeChange = Math.Max(CharWidth, pane * 0.9);
        SideBar.SmallChange = CharWidth * 4;
        if (_resetSideScroll)
        {
            _resetSideScroll = false;
            SideBar.Value = 0;
        }
        else if (SideBar.Value > SideBar.Maximum)
        {
            SideBar.Value = SideBar.Maximum;
        }

        SideShift = SideBar.Value;
        SideBar.IsVisible = SideBar.Maximum > 1;
    }

    private void HideSideScroll()
    {
        SideBar.IsVisible = false;
        if (SideBar.Value != 0)
            SideBar.Value = 0;
        if (SideShift != 0)
            SideShift = 0;
    }

    private double CharWidth
    {
        get
        {
            if (_charWidth > 0)
                return _charWidth;
            var text = new FormattedText(
                "0000000000",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(DiffFont.Current),
                DiffFont.Size,
                Brushes.Black);
            _charWidth = text.Width / 10;
            if (_charWidth < 1)
                _charWidth = 7.2;
            return _charWidth;
        }
    }

    private static int LongestSide(ObservableCollection<DiffRow>? rows)
    {
        if (rows is null)
            return 0;
        var columns = 0;
        foreach (var row in rows)
        {
            if (row is DiffSideRow side)
            {
                if (side.Left.Length > columns)
                    columns = side.Left.Length;
                if (side.Right.Length > columns)
                    columns = side.Right.Length;
                continue;
            }

            if (row is not DiffEditorRow editor || !editor.SideBySide)
                continue;
            foreach (var line in editor.Lines)
            {
                if (line.Text.Length > columns)
                    columns = line.Text.Length;
            }

            foreach (var line in editor.RightLines)
            {
                if (line.Text.Length > columns)
                    columns = line.Text.Length;
            }
        }

        return columns;
    }

    private ColumnDefinition LocationsColumn => Columns.ColumnDefinitions[0];

    private ColumnDefinition LocationsSplitter => Columns.ColumnDefinitions[1];

    private ColumnDefinition GraphColumn => Columns.ColumnDefinitions[2];

    private RowDefinition FilesRow => Details.RowDefinitions[2];

    public void ReadWidths()
    {
        if (_applyingWidths || DataContext is not RepositoryViewModel vm)
            return;
        var changed = false;
        if (LocationsColumn.Width.GridUnitType == GridUnitType.Pixel && LocationsColumn.Width.Value >= 140 && vm.LocationsWidth != LocationsColumn.Width.Value)
        {
            vm.LocationsWidth = LocationsColumn.Width.Value;
            changed = true;
        }

        if (GraphColumn.Width.GridUnitType == GridUnitType.Pixel && GraphColumn.Width.Value >= 240 && vm.GraphWidth != GraphColumn.Width.Value)
        {
            vm.GraphWidth = GraphColumn.Width.Value;
            changed = true;
        }

        if (FilesRow.Height.GridUnitType == GridUnitType.Pixel && FilesRow.Height.Value >= 80 && vm.FilesHeight != FilesRow.Height.Value)
        {
            vm.FilesHeight = FilesRow.Height.Value;
            changed = true;
        }

        if (changed)
            vm.NotePaneEdit();
    }

    private bool _applyingWidths;

    private void ApplyWidths(RepositoryViewModel vm)
    {
        _applyingWidths = true;
        try
        {
            if (vm.ShowLocations)
            {
                LocationsColumn.MinWidth = 140;
                if (vm.LocationsWidth >= 140)
                    LocationsColumn.Width = new GridLength(vm.LocationsWidth);
                LocationsSplitter.Width = new GridLength(4);
            }
            else
            {
                LocationsColumn.MinWidth = 0;
                LocationsColumn.Width = new GridLength(0);
                LocationsSplitter.Width = new GridLength(0);
            }

            if (vm.GraphWidth >= 240)
                GraphColumn.Width = new GridLength(vm.GraphWidth);
            if (vm.FilesHeight >= 80)
                FilesRow.Height = new GridLength(vm.FilesHeight);
        }
        finally
        {
            _applyingWidths = false;
        }
    }

    private bool _panesWatched;

    private void WatchPanes()
    {
        if (_panesWatched)
            return;
        _panesWatched = true;
        LocationsColumn.PropertyChanged += OnPaneChanged;
        GraphColumn.PropertyChanged += OnPaneChanged;
        FilesRow.PropertyChanged += OnPaneChanged;
    }

    private void UnwatchPanes()
    {
        if (!_panesWatched)
            return;
        _panesWatched = false;
        LocationsColumn.PropertyChanged -= OnPaneChanged;
        GraphColumn.PropertyChanged -= OnPaneChanged;
        FilesRow.PropertyChanged -= OnPaneChanged;
    }

    private void OnPaneChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ColumnDefinition.WidthProperty && e.Property != RowDefinition.HeightProperty)
            return;
        PublishPanes();
    }

    private void PublishPanes()
    {
        if (!_widthsApplied || _applyingWidths)
            return;
        ReadWidths();
    }

    private void AttachGraphScroll()
    {
        if (_scrollHooked)
            return;
        var scroll = GraphList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null)
            return;
        _scrollHooked = true;
        scroll.ScrollChanged += OnGraphScroll;
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

    private async void OnGraphScroll(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll || scroll.Extent.Height <= scroll.Viewport.Height)
            return;
        if (scroll.Offset.Y + scroll.Viewport.Height < scroll.Extent.Height - 48)
            return;
        if (DataContext is RepositoryViewModel vm)
            await vm.LoadMoreFromScrollAsync();
    }

    private void OnCommitKeyDown(object? sender, KeyEventArgs e)
    {
        if (AppGestures.Matches(e, Key.Enter) && DataContext is RepositoryViewModel vm)
        {
            vm.CommitCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        // Tunnel visits this control before the search box. Only the bubble may leave history,
        // and only when that box did not already take Escape.
        if (e.Handled || e.Route != RoutingStrategies.Bubble || e.Key != Key.Escape)
            return;
        if (DataContext is not RepositoryViewModel vm || !vm.HasHistoryQuery)
            return;
        vm.ShowAllCommitsCommand.Execute(null);
        e.Handled = true;
    }

    private void OnHistoryKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not RepositoryViewModel vm)
            return;
        if (e.Key == Key.Enter)
        {
            vm.SearchHistoryCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
            return;
        vm.DismissHistorySearchCommand.Execute(null);
        e.Handled = true;
    }

    private void OnLocationFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not RepositoryViewModel vm)
            return;
        vm.ToggleLocationFilterCommand.Execute(null);
        e.Handled = true;
    }

    private async void OnGraphSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not RepositoryViewModel vm || sender is not ListBox list)
            return;
        var rows = new List<GraphRowViewModel>();
        if (list.SelectedItems is not null)
        {
            foreach (var item in list.SelectedItems)
            {
                if (item is GraphRowViewModel row)
                    rows.Add(row);
            }
        }

        await vm.NoteGraphSelection(rows);
    }

    private static bool HasLocationMenu(LocationItem item) =>
        item.ShowCheckout || item.ShowMerge || item.ShowRebase || item.ShowDelete || item.ShowSetUpstream || item.ShowReveal
        || item.ShowHide || item.ShowAllBranches
        || item.ShowRename || item.ShowPop || item.ShowApply || item.ShowDrop || item.ShowOpen || item.ShowTag;

    private void OnLocationExpand(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: LocationItem item } && DataContext is RepositoryViewModel vm)
            vm.ToggleLocation(item);
    }

    private void OnLocationExpandDoubleTapped(object? sender, TappedEventArgs e) => e.Handled = true;

    private void OnLocationTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source)
            return;
        if (source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;
        var row = source as ListBoxItem ?? source.FindAncestorOfType<ListBoxItem>();
        if (row?.DataContext is not LocationItem { ShowTag: true, Oid: { Length: > 0 } } item)
            return;
        item.RevealCommand.Execute(null);
    }

    private async void OnLocationDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not RepositoryViewModel vm || vm.SelectedLocation is not { } item)
            return;
        if (item.HasChildren)
            vm.ToggleLocation(item);
        else
            await vm.ActivateLocation(item);
    }

    private void OnRepositoryDirectoryExpand(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: RepositoryFileTreeItem item } && DataContext is RepositoryViewModel vm)
            vm.ToggleRepositoryDirectory(item);
    }

    private void OnRepositoryFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;
        if (DataContext is RepositoryViewModel vm && vm.SelectedRepositoryFile is { IsDirectory: true } item)
        {
            vm.ToggleRepositoryDirectory(item);
            e.Handled = true;
        }
    }

    private void OnRepositoryFileKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not RepositoryViewModel vm || vm.SelectedRepositoryFile is not { } item)
            return;
        if (e.Key == Key.Right && item.IsDirectory && !item.IsExpanded
            || e.Key == Key.Left && item.IsDirectory && item.IsExpanded)
        {
            vm.ToggleRepositoryDirectory(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Left && item.Depth > 0)
        {
            var parent = item.Path[..item.Path.LastIndexOf('/')];
            vm.SelectedRepositoryFile = vm.RepositoryFileTree.FirstOrDefault(row => row.IsDirectory && row.Path == parent);
            e.Handled = true;
        }
    }

    private void OnMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.PlacementTarget is not Control target)
            return;
        menu.DataContext = target.DataContext;
        if (target.DataContext is LocationItem item && !HasLocationMenu(item))
            e.Cancel = true;
        if (target.DataContext is DiffFileRow { FileMenu: null })
            e.Cancel = true;
        if (target.DataContext is RepositoryFileTreeItem { IsDirectory: true })
            e.Cancel = true;
    }
}
