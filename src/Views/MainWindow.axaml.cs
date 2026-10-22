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
using System.Runtime.InteropServices;

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
        RepositoryTabs.AddHandler(PointerPressedEvent, OnRepositoryTabPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnWindowPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        // A taskbar click activates the window while it is still minimized. Avalonia
        // runs that activation, and the dock focus inside it, before Windows finishes
        // the restore. The icon then needs a second click. Ignore the iconic message.
        if (OperatingSystem.IsWindows())
            Win32Properties.AddWndProcHookCallback(this, IgnoreIconicActivation);
    }

    private MainViewModel? _watched;
    private bool _syncingTabs;
    private RepositoryViewModel? _dragTab;
    private Point _dragStart;
    private bool _dragMoved;

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
        if (_syncingTabs || _dragMoved || sender is not TabStrip strip || strip.SelectedItem is not RepositoryViewModel tab || DataContext is not MainViewModel vm)
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

    private void OnRepositoryTabPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || CloseButton(e.Source) || TabOf(e.Source) is not { } tab)
            return;
        _dragTab = tab;
        _dragStart = e.GetPosition(this);
        _dragMoved = false;
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragTab is null || DataContext is not MainViewModel vm)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndDrag();
            return;
        }

        var point = e.GetPosition(this);
        if (!_dragMoved)
        {
            if (Math.Abs(point.X - _dragStart.X) < 6 && Math.Abs(point.Y - _dragStart.Y) < 6)
                return;
            _dragMoved = true;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.SizeWestEast);
        }

        var index = TabIndexAt(e.GetPosition(RepositoryTabs));
        if (index >= 0)
            vm.MoveTab(_dragTab, index);
        SyncRepositoryTabs();
        NudgeTabScroll(e.GetPosition(TabScroll));
        e.Handled = true;
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragTab is null)
            return;
        if (_dragMoved)
            e.Handled = true;
        EndDrag();
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    private void EndDrag()
    {
        if (_dragTab is null)
            return;
        var moved = _dragMoved;
        _dragTab = null;
        _dragMoved = false;
        if (!moved)
            return;
        Cursor = null;
        if (DataContext is MainViewModel vm)
            vm.Save();
    }

    private void NudgeTabScroll(Point point)
    {
        var offset = TabScroll.Offset;
        if (point.X < 24)
            TabScroll.Offset = new Vector(Math.Max(0, offset.X - 16), offset.Y);
        else if (point.X > TabScroll.Bounds.Width - 24)
            TabScroll.Offset = new Vector(offset.X + 16, offset.Y);
    }

    private int TabIndexAt(Point point)
    {
        if (DataContext is not MainViewModel vm || vm.Tabs.Count == 0)
            return -1;
        var first = TabBounds(0);
        var last = TabBounds(vm.Tabs.Count - 1);
        if (first is null || last is null)
            return -1;
        if (point.X < first.Value.X)
            return 0;
        if (point.X >= last.Value.X + last.Value.Width)
            return vm.Tabs.Count - 1;
        for (var i = 0; i < vm.Tabs.Count; i++)
        {
            if (TabBounds(i) is not Rect bounds)
                continue;
            if (point.X >= bounds.X && point.X < bounds.X + bounds.Width)
                return i;
        }

        return -1;
    }

    private Rect? TabBounds(int index)
    {
        if (RepositoryTabs.ContainerFromIndex(index) is not Visual visual)
            return null;
        var origin = visual.TranslatePoint(default, RepositoryTabs);
        return origin is null ? null : new Rect(origin.Value, visual.Bounds.Size);
    }

    private static RepositoryViewModel? TabOf(object? source)
    {
        var visual = source as Visual;
        while (visual is not null)
        {
            if (visual is TabStripItem { DataContext: RepositoryViewModel tab })
                return tab;
            visual = visual.GetVisualParent();
        }

        return null;
    }

    private static bool CloseButton(object? source)
    {
        var visual = source as Visual;
        while (visual is not null)
        {
            if (visual is Button)
                return true;
            visual = visual.GetVisualParent();
        }

        return false;
    }

    private async void OnWindowActivated(object? sender, EventArgs e) => await RefreshAfterActivationAsync();

    /// <summary>
    /// Refreshes the open repositories. A minimized window waits until it is visible:
    /// refreshing here would run inside the iconic activation and cancel the restore.
    /// A visible activation stays synchronous so a dialog can still hold the refresh
    /// until its confirmed command has run.
    /// </summary>
    internal Task RefreshAfterActivationAsync()
    {
        if (WindowState == WindowState.Minimized || DataContext is not MainViewModel vm)
            return Task.CompletedTask;
        return vm.OnWindowActivated();
    }

    // WM_ACTIVATE packs WA_* in the low word and fMinimized in the high word.
    internal static bool IsIconicActivation(uint msg, nint wParam)
    {
        const uint activate = 0x0006;
        if (msg != activate)
            return false;
        var word = unchecked((uint)wParam);
        var activating = (word & 0xFFFF) != 0;
        var iconic = word >> 16 != 0;
        return activating && iconic;
    }

    private static nint IgnoreIconicActivation(nint hWnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (!IsIconicActivation(msg, wParam))
            return 0;
        handled = true;
        return 0;
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
        _ = vm.WatchForReleaseAsync();
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
        DisarmPaletteEnter();
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
            FocusPalette();
    }

    // The key that opens the palette is still in progress, and the box stays hidden
    // until the next layout. Focus after that layout, then again after idle in case
    // layout moves focus. Select all only while the query is still empty.
    private void FocusPalette()
    {
        Dispatcher.UIThread.Post(() => Apply(select: true), DispatcherPriority.Background);
        Dispatcher.UIThread.Post(() => Apply(select: false), DispatcherPriority.ContextIdle);

        void Apply(bool select)
        {
            if (DataContext is not MainViewModel { PaletteOpen: true })
                return;
            if (!PaletteBox.Focus())
                return;
            if (select || string.IsNullOrEmpty(PaletteBox.Text))
                PaletteBox.SelectAll();
        }
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

    private EventHandler<KeyEventArgs>? _paletteEnterUp;

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            // The command can open a dialog whose OK button treats Enter as a click.
            // Running on key-down lets that same key, still held, dismiss the dialog.
            if (_paletteEnterUp is not null || !vm.PaletteOpen)
                return;
            _paletteEnterUp = OnPaletteEnterUp;
            AddHandler(KeyUpEvent, _paletteEnterUp, RoutingStrategies.Tunnel, handledEventsToo: true);
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

    private async void OnPaletteEnterUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        DisarmPaletteEnter();
        e.Handled = true;
        if (DataContext is MainViewModel { PaletteOpen: true } vm)
            await vm.RunPaletteCommand.ExecuteAsync(null);
    }

    private void DisarmPaletteEnter()
    {
        if (_paletteEnterUp is null)
            return;
        RemoveHandler(KeyUpEvent, _paletteEnterUp);
        _paletteEnterUp = null;
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
        {
            RememberWindow();
            if (e.OldValue is WindowState.Minimized && WindowState != WindowState.Minimized)
                Dispatcher.UIThread.Post(ResumeAfterMinimize, DispatcherPriority.Background);
        }
    }

    // The ignored activation can leave Windows treating the window as active while
    // Avalonia still has it inactive, and it does not send another message once the
    // window is visible. Ask again after the restore, then refresh.
    private async void ResumeAfterMinimize()
    {
        if (WindowState == WindowState.Minimized)
            return;
        if (!IsActive)
        {
            Activate();
            if (!IsActive && WindowState != WindowState.Minimized)
                DeliverVisibleActivation();
        }

        if (WindowState != WindowState.Minimized)
            await RefreshAfterActivationAsync();
    }

    private void DeliverVisibleActivation()
    {
        var platform = TryGetPlatformHandle();
        if (platform is null || !string.Equals(platform.HandleDescriptor, "HWND", StringComparison.Ordinal) || platform.Handle == 0)
            return;
        const uint activate = 0x0006;
        const int active = 1;
        SendMessage(platform.Handle, activate, active, 0);
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

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
