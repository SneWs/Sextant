using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
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
using System.Windows.Input;

namespace Sextant.Views;

public partial class DiffDocumentView : UserControl
{
    private RepositoryViewModel? _watched;
    private ObservableCollection<DiffRow>? _diffRows;
    private bool _sideScrollHooked;
    private bool _sideScrollQueued;
    private Dispatcher? _fontDispatcher;
    private bool _resetSideScroll;
    private double _charWidth;

    /// <summary>Shared horizontal offset of the two side-by-side columns, in pixels.</summary>
    public static readonly StyledProperty<double> SideShiftProperty =
        AvaloniaProperty.Register<DiffDocumentView, double>(nameof(SideShift));

    public double SideShift
    {
        get => GetValue(SideShiftProperty);
        set => SetValue(SideShiftProperty, value);
    }

    public DiffDocumentView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _fontDispatcher = Dispatcher.UIThread;
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
        // A view left attached by a torn-down headless session must not post into the
        // current session's dispatcher; its jobs would run on the wrong thread at teardown.
        if (!ReferenceEquals(Dispatcher.UIThread, _fontDispatcher))
            return;
        Dispatcher.UIThread.Post(RefreshFontLayout, DispatcherPriority.Background);
    }

    private void RefreshFontLayout()
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return;
        foreach (var panel in DiffList.GetVisualDescendants().OfType<DiffVirtualizingPanel>())
            panel.ResetHeights();
        UpdateSideScroll();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        HookSideScroll();
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
        _watched.JumpToFile += OnJumpToFile;
        _diffRows = _watched.DiffRows;
        _diffRows.CollectionChanged += OnDiffRowsChanged;
        _resetSideScroll = true;
        QueueSideScroll();
    }

    private void Unwatch()
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RepositoryViewModel.SideBySide) or nameof(RepositoryViewModel.ShowingDiff))
            QueueSideScroll();
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

    private void OnJumpToFile(string path, string? original) => ScrollDiffTo(path, original);

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
        if (!Dispatcher.UIThread.CheckAccess())
            return;
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

    private void OnMenuOpening(object? sender, CancelEventArgs e) => MenuOpen.OnOpening(sender, e);
}
