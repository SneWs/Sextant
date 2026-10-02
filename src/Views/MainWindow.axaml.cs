using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant;
using Sextant.Services;
using Sextant.ViewModels;
using System.ComponentModel;

namespace Sextant.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnTunnelKey, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnWindowPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        TabsList.LayoutUpdated += (_, _) => PlacePaneRule();
        TabScroll.ScrollChanged += (_, _) => PlacePaneRule();
        SizeChanged += (_, _) => RememberWindow();
        PositionChanged += (_, _) => RememberWindow();
        PropertyChanged += OnWindowPropertyChanged;
        Activated += (_, _) => (DataContext as MainViewModel)?.OnWindowActivated();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        BringOnScreen();
        if (DataContext is not MainViewModel vm)
            return;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.Attach(new AvaloniaDialogService(this));
        _ = vm.InitializeAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _dragTab = null;
        CommitWindow();
        if (DataContext is MainViewModel vm)
        {
            ReadWidths();
            vm.Shutdown();
        }

        base.OnClosed(e);
    }

    private void ReadWidths()
    {
        foreach (var view in this.GetVisualDescendants().OfType<RepositoryView>())
            view.ReadWidths();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PaletteOpen) && sender is MainViewModel { PaletteOpen: true })
            PaletteBox.Focus();
        if (e.PropertyName is nameof(MainViewModel.ActiveTab) or nameof(MainViewModel.HasActiveTab))
            PlacePaneRule();
    }

    private bool _placingPaneRule;

    /// <summary>
    /// The repository pane's top rule runs the full width and stops under the selected tab,
    /// so that tab's side borders meet the rule and its bottom stays open.
    /// </summary>
    private void PlacePaneRule()
    {
        if (_placingPaneRule || PaneHost is null)
            return;
        var hostWidth = PaneHost.Bounds.Width;
        if (hostWidth <= 0)
            return;
        _placingPaneRule = true;
        try
        {
            double left;
            double rightStart;
            if (!TryActiveTabSpan(out var x, out var tabWidth))
            {
                left = hostWidth;
                rightStart = hostWidth;
            }
            else
            {
                var innerLeft = x + 1;
                var innerRight = x + tabWidth - 1;
                left = Math.Clamp(innerLeft, 0, hostWidth);
                rightStart = Math.Clamp(innerRight, 0, hostWidth);
                if (rightStart < left)
                    rightStart = left;
            }

            SetRule(PaneRuleLeft, 0, left);
            SetRule(PaneRuleRight, rightStart, Math.Max(0, hostWidth - rightStart));
        }
        finally
        {
            _placingPaneRule = false;
        }
    }

    private bool TryActiveTabSpan(out double x, out double width)
    {
        x = 0;
        width = 0;
        if (DataContext is not MainViewModel vm || vm.ActiveTab is null)
            return false;
        var index = vm.Tabs.IndexOf(vm.ActiveTab);
        if (index < 0 || TabsList.ContainerFromIndex(index) is not Visual container)
            return false;
        var chrome = container.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("tab"));
        if (chrome is null || chrome.Bounds.Width <= 0)
            return false;
        var origin = chrome.TranslatePoint(default, PaneHost);
        if (origin is null)
            return false;
        x = origin.Value.X;
        width = chrome.Bounds.Width;
        return true;
    }

    private static void SetRule(Border rule, double x, double width)
    {
        if (Math.Abs(rule.Margin.Left - x) > 0.5)
            rule.Margin = new Thickness(x, 0, 0, 0);
        if (double.IsNaN(rule.Width) || Math.Abs(rule.Width - width) > 0.5)
            rule.Width = width;
    }

    private void OnTunnelKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        if (AppGestures.Matches(e, Key.P))
        {
            vm.TogglePalette();
            e.Handled = true;
            return;
        }

        if (AppGestures.Matches(e, Key.O))
        {
            if (vm.CanUseGit)
                vm.OpenFolderCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (AppGestures.Matches(e, Key.F) && vm.ActiveTab is { } searchTab)
        {
            searchTab.ToggleHistorySearchCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (AppGestures.Matches(e, Key.B) && vm.ActiveTab is { } branchTab)
        {
            if (branchTab.CanRunCommands)
                branchTab.CreateBranchCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (AppGestures.Matches(e, Key.S, KeyModifiers.Shift) && vm.ActiveTab is { } stashTab)
        {
            if (stashTab.CanRunCommands)
                stashTab.StashCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && vm.PaletteOpen)
        {
            vm.ClosePalette();
            e.Handled = true;
            return;
        }

        if (AppGestures.Matches(e, Key.W))
        {
            if (vm.PaletteOpen)
                vm.ClosePalette();
            else
                vm.CloseActive();
            e.Handled = true;
            return;
        }

        // Command+Tab is the macOS application switcher, so next tab is Control+Tab everywhere.
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Control)
        {
            vm.NextTab();
            e.Handled = true;
            return;
        }

        if (!vm.PaletteOpen && e.KeyModifiers == AppGestures.Command && Digit(e.Key) is int digit)
        {
            vm.ActivateDigit(digit);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            vm.RefreshActive();
            e.Handled = true;
        }
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        if (e.Key == Key.Enter)
        {
            _ = vm.RunPaletteAsync();
            e.Handled = true;
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

    private void OnPaletteChosen(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            _ = vm.RunPaletteAsync();
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
    private RepositoryViewModel? _dragTab;
    private Point _dragStart;
    private bool _dragMoved;

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

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Panel { DataContext: RepositoryViewModel tab })
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsCloseButton(e.Source))
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
            FinishTabGesture(activateIfClick: true);
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

        var index = TabIndexAt(e.GetPosition(TabsList));
        if (index >= 0)
            vm.MoveTab(_dragTab, index);
        NudgeTabScroll(e.GetPosition(TabScroll));
        e.Handled = true;
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragTab is null)
            return;
        if (_dragMoved)
            e.Handled = true;
        FinishTabGesture(activateIfClick: true);
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
    }

    private void FinishTabGesture(bool activateIfClick)
    {
        if (_dragTab is null)
            return;
        var tab = _dragTab;
        var moved = _dragMoved;
        EndDrag();
        if (activateIfClick && !moved && DataContext is MainViewModel vm)
            vm.Activate(tab);
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
        Cursor = new Cursor(StandardCursorType.Arrow);
        if (moved && DataContext is MainViewModel vm)
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
        if (TabsList.ContainerFromIndex(index) is not Visual visual)
            return null;
        var origin = visual.TranslatePoint(default, TabsList);
        return origin is null ? null : new Rect(origin.Value, visual.Bounds.Size);
    }

    private static bool IsCloseButton(object? source)
    {
        var visual = source as Visual;
        while (visual is not null)
        {
            if (visual is Button { Content: "×" })
                return true;
            visual = visual.GetVisualParent();
        }

        return false;
    }

    private static int? Digit(Key key) => key switch
    {
        Key.D0 or Key.NumPad0 => 0,
        Key.D1 or Key.NumPad1 => 1,
        Key.D2 or Key.NumPad2 => 2,
        Key.D3 or Key.NumPad3 => 3,
        Key.D4 or Key.NumPad4 => 4,
        Key.D5 or Key.NumPad5 => 5,
        Key.D6 or Key.NumPad6 => 6,
        Key.D7 or Key.NumPad7 => 7,
        Key.D8 or Key.NumPad8 => 8,
        Key.D9 or Key.NumPad9 => 9,
        _ => null,
    };
}
