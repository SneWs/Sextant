using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.ViewModels;

namespace Sextant.Views;

public partial class HistoryToolView : UserControl
{
    private bool _scrollHooked;
    private RepositoryViewModel? _watched;

    public HistoryToolView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AttachGraphScroll();
        GraphList.TemplateApplied += (_, _) => AttachGraphScroll();
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
        _watched.Rows.CollectionChanged += OnGraphRowsChanged;
        QueueFileCounts(_watched);
    }

    private void Unwatch()
    {
        if (_watched is null)
            return;
        _watched.PropertyChanged -= OnViewModelPropertyChanged;
        _watched.Rows.CollectionChanged -= OnGraphRowsChanged;
        _watched = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not RepositoryViewModel vm)
            return;
        if (e.PropertyName == nameof(RepositoryViewModel.RepositoryFilesTabOn) && vm.HistoryTabOn)
            QueueFileCounts(vm);
    }

    private int _fileCountsQueued;

    private void OnGraphRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_watched is null)
            return;
        QueueFileCounts(_watched);
    }

    /// <summary>One layout pass asks for the visible rows. A graph rebuild used to post once per commit.</summary>
    private void QueueFileCounts(RepositoryViewModel vm)
    {
        if (Interlocked.Exchange(ref _fileCountsQueued, 1) == 1)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _fileCountsQueued, 0);
            if (!ReferenceEquals(_watched, vm))
                return;
            RequestRowFileCounts(vm);
        }, DispatcherPriority.Loaded);
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

    private async void OnGraphScroll(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll)
            return;
        if (DataContext is RepositoryViewModel vm)
        {
            RequestRowFileCounts(vm);
            if (scroll.Extent.Height <= scroll.Viewport.Height)
                return;
            if (scroll.Offset.Y + scroll.Viewport.Height < scroll.Extent.Height - 48)
                return;
            await vm.LoadMoreFromScrollAsync();
        }
    }

    /// <summary>Asks the view model for the changed-file counts of the rows the graph has realized.</summary>
    private void RequestRowFileCounts(RepositoryViewModel vm)
    {
        var rows = GraphList.GetRealizedContainers().OfType<Control>()
            .Select(GraphList.ItemFromContainer)
            .OfType<GraphRowViewModel>()
            .ToList();
        if (rows.Count > 0)
            vm.RequestFileCounts(rows);
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

    private void OnMenuOpening(object? sender, CancelEventArgs e) => MenuOpen.OnOpening(sender, e);
}
