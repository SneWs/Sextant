using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant;
using Sextant.Docking;
using Sextant.Services;
using Sextant.ViewModels;
using System.ComponentModel;

namespace Sextant.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        if (Application.Current is { } app)
            DockChrome.Ensure(app);
        InitializeComponent();
        AddHandler(KeyDownEvent, OnTunnelKey, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnBubbleKey, RoutingStrategies.Bubble);
        SizeChanged += (_, _) => RememberWindow();
        PositionChanged += (_, _) => RememberWindow();
        PropertyChanged += OnWindowPropertyChanged;
        DataContextChanged += (_, _) => AttachViewModel();
        Activated += OnWindowActivated;
    }

    private MainViewModel? _watched;
    private bool _syncingTabs;

    private void AttachViewModel()
    {
        if (_watched is not null)
            _watched.PropertyChanged -= OnViewModelPropertyChanged;
        _watched = DataContext as MainViewModel;
        if (_watched is not null)
            _watched.PropertyChanged += OnViewModelPropertyChanged;
        SyncRepositoryTabs();
    }

    private void OnRepositoryTabSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingTabs || sender is not TabStrip strip || strip.SelectedItem is not RepositoryViewModel tab || DataContext is not MainViewModel vm)
            return;
        if (ReferenceEquals(vm.ActiveTab, tab) || (!strip.IsPointerOver && !strip.IsKeyboardFocusWithin))
            return;
        _ = vm.Activate(tab);
    }

    private void SyncRepositoryTabs()
    {
        if (RepositoryTabs is null || DataContext is not MainViewModel vm)
            return;
        if (ReferenceEquals(RepositoryTabs.SelectedItem, vm.ActiveTab))
            return;
        _syncingTabs = true;
        try
        {
            RepositoryTabs.SelectedItem = vm.ActiveTab;
        }
        finally
        {
            _syncingTabs = false;
        }
    }

    private async void OnWindowActivated(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await vm.OnWindowActivated();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        BringOnScreen();
        if (DataContext is not MainViewModel vm)
            return;
        vm.ExitRequested += OnExitRequested;
        vm.Attach(new AvaloniaDialogService(this));
        vm.Dock.Screens = WorkAreas;
        vm.Dock.RehomeOffScreenFloats();
        await vm.InitializeAsync();
    }

    private IReadOnlyList<(int X, int Y, int Width, int Height)> WorkAreas()
    {
        var screens = Screens?.All;
        if (screens is null || screens.Count == 0)
            return [];
        return screens.Select(screen =>
        {
            var area = screen.WorkingArea;
            return (area.X, area.Y, area.Width, area.Height);
        }).ToList();
    }

    private void OnExitRequested(object? sender, EventArgs e) => Close();

    private bool _closing;
    private bool _shutdownComplete;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _shutdownComplete || DataContext is not MainViewModel vm)
            return;
        e.Cancel = true;
        if (_closing)
            return;
        _closing = true;
        CommitWindow();
        IsEnabled = false;
        await vm.Shutdown();
        _shutdownComplete = true;
        Dispatcher.UIThread.Post(Close);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ExitRequested -= OnExitRequested;
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        base.OnClosed(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveTab))
            SyncRepositoryTabs();
        if (e.PropertyName == nameof(MainViewModel.PaletteOpen) && sender is MainViewModel { PaletteOpen: true })
            PaletteBox.Focus();
    }

    private void OnTunnelKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            WindowInput.OnTunnel(vm, e);
    }

    private void OnBubbleKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            WindowInput.OnHistoryEscape(vm, e);
    }

    private async void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await vm.RunPaletteCommand.ExecuteAsync(null);
        }
        else if (e.Key == Key.Down)
        {
            vm.MovePalette(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            vm.MovePalette(-1);
            e.Handled = true;
        }
    }

    private async void OnPaletteChosen(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || e.Source is not Visual source)
            return;
        var row = source as ListBoxItem ?? source.FindAncestorOfType<ListBoxItem>();
        if (row?.DataContext is not PaletteItem chosen)
            return;
        vm.SelectedPalette = chosen;
        e.Handled = true;
        await vm.RunPaletteCommand.ExecuteAsync(null);
    }

    private void OnPaletteBackdrop(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == sender && DataContext is MainViewModel vm)
            vm.ClosePalette();
    }

    private void OnPaletteCardPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private bool _layoutApplied;
    private bool _applyingLayout;
    private int _remember;

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == DataContextProperty && e.NewValue is MainViewModel vm)
            ApplySavedWindow(vm);
        else if (e.Property == WindowStateProperty)
            RememberWindow();
    }

    private void ApplySavedWindow(MainViewModel vm)
    {
        if (_layoutApplied)
            return;
        _layoutApplied = true;
        _applyingLayout = true;
        try
        {
            if (vm.WindowWidth >= MinWidth && vm.WindowHeight >= MinHeight)
            {
                Width = vm.WindowWidth;
                Height = vm.WindowHeight;
            }

            if (vm.WindowX is int x && vm.WindowY is int y)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint(x, y);
            }

            if (vm.WindowMaximized)
                WindowState = WindowState.Maximized;
        }
        finally
        {
            _applyingLayout = false;
        }
    }

    private void RememberWindow()
    {
        if (_applyingLayout || DataContext is not MainViewModel vm)
            return;
        if (WindowState == WindowState.Maximized)
        {
            vm.WindowMaximized = true;
            _remember++;
            return;
        }

        if (WindowState != WindowState.Normal)
            return;
        // Maximizing moves the window to the screen corner before the state flips.
        // Wait a turn, and keep the previous normal bounds if that is what happened.
        var request = ++_remember;
        Dispatcher.UIThread.Post(() =>
        {
            if (request != _remember)
                return;
            CommitWindow();
        }, DispatcherPriority.Background);
    }

    private void CommitWindow()
    {
        _remember++;
        if (_applyingLayout || DataContext is not MainViewModel vm || WindowState != WindowState.Normal)
            return;
        if (Width < MinWidth || Height < MinHeight || CoversWorkArea())
            return;
        vm.WindowMaximized = false;
        vm.WindowWidth = Width;
        vm.WindowHeight = Height;
        vm.WindowX = Position.X;
        vm.WindowY = Position.Y;
    }

    private void BringOnScreen()
    {
        if (WindowState == WindowState.Maximized)
            return;
        if (DataContext is not MainViewModel vm || vm.WindowX is null)
            return;
        if (IntersectsWorkArea(Position.X, Position.Y, Width, Height))
            return;
        var screen = Screens?.Primary;
        if (screen is null)
            return;
        var area = screen.WorkingArea;
        Position = new PixelPoint(area.X + 32, area.Y + 32);
        vm.WindowX = Position.X;
        vm.WindowY = Position.Y;
    }

    private bool IntersectsWorkArea(int x, int y, double width, double height)
    {
        var screens = Screens?.All;
        if (screens is null || screens.Count == 0)
            return true;
        var scale = RenderScaling;
        if (scale <= 0 || double.IsNaN(scale))
            scale = 1;
        var pixelWidth = Math.Max(1, (int)Math.Round(width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Round(height * scale));
        foreach (var screen in screens)
        {
            var area = screen.WorkingArea;
            var left = Math.Max(area.X, x);
            var top = Math.Max(area.Y, y);
            var right = Math.Min(area.X + area.Width, x + pixelWidth);
            var bottom = Math.Min(area.Y + area.Height, y + pixelHeight);
            if (right - left >= 48 && bottom - top >= 48)
                return true;
        }

        return false;
    }

    private bool CoversWorkArea()
    {
        var screen = Screens?.ScreenFromVisual(this) ?? Screens?.Primary;
        if (screen is null)
            return false;
        var scale = RenderScaling;
        if (scale <= 0 || double.IsNaN(scale))
            scale = 1;
        var area = screen.WorkingArea;
        return Width * scale >= area.Width - 8 && Height * scale >= area.Height - 8;
    }
}
