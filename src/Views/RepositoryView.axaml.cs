using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.ComponentModel;
using Sextant.ViewModels;

namespace Sextant.Views;

/// <summary>
/// Hosts the repository regions in the column layout the headless tests measure.
/// The main window uses the dock instead of this control.
/// </summary>
public partial class RepositoryView : UserControl
{
    private bool _widthsApplied;
    private bool _applyingWidths;
    private bool _panesWatched;
    private RepositoryViewModel? _watched;

    public RepositoryView()
    {
        InitializeComponent();
        NameShare.Publish(this);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        NameShare.Publish(this);
        if (DataContext is RepositoryViewModel vm && !_widthsApplied)
        {
            _widthsApplied = true;
            ApplyWidths(vm);
        }

        WatchPanes();
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
        if (_widthsApplied)
            PublishPanes();
        UnwatchPanes();
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
        if (_watched is not null)
            _watched.PropertyChanged += OnViewModelPropertyChanged;
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
        if (_widthsApplied && e.PropertyName is nameof(RepositoryViewModel.LocationsWidth) or nameof(RepositoryViewModel.GraphWidth) or nameof(RepositoryViewModel.FilesHeight) or nameof(RepositoryViewModel.ShowLocations))
            ApplyWidths(vm);
    }

    private ColumnDefinition LocationsColumn => Columns.ColumnDefinitions[0];

    private ColumnDefinition LocationsSplitter => Columns.ColumnDefinitions[1];

    private ColumnDefinition GraphColumn => Columns.ColumnDefinitions[2];

    private RowDefinition FilesRow => Details.RowDefinitions[1];

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

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        // Tunnel visits this control before the search box. Only the bubble may leave history,
        // and only when that box did not already take Escape.
        if (e.Handled || e.Route != RoutingStrategies.Bubble || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None)
            return;
        if (DataContext is not RepositoryViewModel vm || !vm.HasHistoryQuery)
            return;
        vm.ShowAllCommitsCommand.Execute(null);
        e.Handled = true;
    }
}
