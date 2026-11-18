using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.ComponentModel;
using Sextant.ViewModels;

namespace Sextant.Views;

public partial class RepositoryChrome : UserControl
{
    private RepositoryViewModel? _watched;

    public RepositoryChrome()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
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

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
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
        if (e.PropertyName == nameof(RepositoryViewModel.ShowHistorySearch) && vm.ShowHistorySearch)
        {
            Dispatcher.UIThread.Post(() =>
            {
                HistorySearchBox.Focus();
                HistorySearchBox.SelectAll();
            }, DispatcherPriority.Background);
        }

        if (e.PropertyName == nameof(RepositoryViewModel.ProgressDetail))
            RevealProgressStart();
    }

    private Flyout ProgressLog => (Flyout)FlyoutBase.GetAttachedFlyout(BranchPill)!;

    private void OnProgressClick(object? sender, RoutedEventArgs e)
    {
        if (!ProgressLog.IsOpen)
            ProgressLog.ShowAt(BranchPill);
    }

    private void OnProgressClose(object? sender, RoutedEventArgs e) => ProgressLog.Hide();

    private void OnProgressOpened(object? sender, EventArgs e) => RevealProgressStart();

    private void RevealProgressStart()
    {
        if (!ProgressLog.IsOpen)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (ProgressDetailBox is null)
                return;
            ProgressDetailBox.CaretIndex = 0;
            if (ProgressDetailBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
                scroll.Offset = new Vector(0, 0);
        }, DispatcherPriority.Background);
    }

    private void OnChromeKeyDown(object? sender, KeyEventArgs e)
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

        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None)
            return;
        vm.DismissHistorySearchCommand.Execute(null);
        e.Handled = true;
    }
}
