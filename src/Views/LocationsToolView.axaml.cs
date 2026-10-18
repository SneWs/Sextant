using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.ViewModels;

namespace Sextant.Views;

public partial class LocationsToolView : UserControl
{
    private RepositoryViewModel? _watched;

    public LocationsToolView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
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
        if (e.PropertyName == nameof(RepositoryViewModel.LocationFilterOpen) && vm.LocationFilterOpen)
        {
            Dispatcher.UIThread.Post(() =>
            {
                LocationFilterBox.Focus();
                LocationFilterBox.SelectAll();
            }, DispatcherPriority.Background);
        }
    }

    private void OnLocationFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || DataContext is not RepositoryViewModel vm)
            return;
        vm.ToggleLocationFilterCommand.Execute(null);
        e.Handled = true;
    }

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

    private void OnMenuOpening(object? sender, CancelEventArgs e) => MenuOpen.OnOpening(sender, e);
}
