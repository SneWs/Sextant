using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Sextant.Controls;

/// <summary>
/// Shows one clay still. Hold Ctrl or Cmd to turn a mesh: drag rotates, the wheel zooms,
/// and a double-click returns the first angle. A plain wheel scrolls the diff, and a plain
/// click does not capture the pointer, so the row does not keep focus while the list moves.
/// A new angle is drawn off the UI thread. Pictures without a mesh stay still.
/// </summary>
public sealed class FbxOrbitView : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<FbxOrbitView, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<FbxPreview.Orbit?> OrbitProperty =
        AvaloniaProperty.Register<FbxOrbitView, FbxPreview.Orbit?>(nameof(Orbit));

    static FbxOrbitView()
    {
        // A picture that arrives after the first measure has to grow the row. Orbit frames
        // stay the same pixel size, so turning the model does not measure again.
        AffectsMeasure<FbxOrbitView>(SourceProperty);
    }

    private Bitmap? _frame;
    private Point _last;
    private float _yaw = FbxPreview.DefaultYaw;
    private float _pitch = FbxPreview.DefaultPitch;
    private float _zoom = FbxPreview.DefaultZoom;
    private int _request;
    private bool _drawing;
    private bool _dragging;
    private bool _armed;
    private bool _attached = true;
    private IPointer? _captured;
    private IInputElement? _focusRestore;
    private TopLevel? _keys;

    private const string TurnTip =
        "Hold Ctrl or Cmd to turn. Drag rotates, the wheel zooms, and a double-click resets the view.";

    public FbxOrbitView()
    {
        Focusable = false;
        DoubleTapped += (_, eventArgs) => ResetView(eventArgs);
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public FbxPreview.Orbit? Orbit
    {
        get => GetValue(OrbitProperty);
        set => SetValue(OrbitProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
            InvalidateVisual();
        if (change.Property == OrbitProperty)
            ResetAngles();
    }

    protected override Size MeasureOverride(Size available)
    {
        var bitmap = _frame ?? Source;
        if (bitmap is null)
            return default;
        var maxWidth = double.IsInfinity(available.Width) ? bitmap.PixelSize.Width : available.Width;
        var maxHeight = double.IsInfinity(available.Height) ? bitmap.PixelSize.Height : Math.Min(available.Height, 420);
        var scale = Math.Min(maxWidth / bitmap.PixelSize.Width, maxHeight / bitmap.PixelSize.Height);
        if (scale <= 0 || double.IsInfinity(scale))
            return default;
        return new Size(bitmap.PixelSize.Width * scale, bitmap.PixelSize.Height * scale);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        // A drawn fill is what Avalonia hit-tests. Transparent still receives the drag.
        context.FillRectangle(Brushes.Transparent, bounds);
        var bitmap = _frame ?? Source;
        if (bitmap is null)
            return;
        var scale = Math.Min(bounds.Width / bitmap.PixelSize.Width, bounds.Height / bitmap.PixelSize.Height);
        if (scale <= 0 || double.IsInfinity(scale))
            return;
        var width = bitmap.PixelSize.Width * scale;
        var height = bitmap.PixelSize.Height * scale;
        var x = (bounds.Width - width) / 2;
        var y = (bounds.Height - height) / 2;
        context.DrawImage(bitmap, new Rect(x, y, width, height));
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        NoteModifiers(e.KeyModifiers);
        UpdateCursor();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_dragging)
            Cursor = new Cursor(StandardCursorType.Arrow);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        NoteModifiers(e.KeyModifiers);
        if (Orbit is not { CanTurn: true } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        // Swallow the click. A plain press must not leave the diff row focused, or the
        // virtualized list keeps that row on screen. Capture only while Ctrl or Cmd is held.
        e.Handled = true;
        if (!_armed)
        {
            RestoreFocus();
            return;
        }
        _dragging = true;
        _last = e.GetPosition(this);
        _captured = e.Pointer;
        e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        NoteModifiers(e.KeyModifiers);
        if (!_dragging || Orbit is not { CanTurn: true })
            return;
        var point = e.GetPosition(this);
        var delta = point - _last;
        _last = point;
        _yaw += (float)delta.X * 0.01f;
        _pitch = Math.Clamp(_pitch - (float)delta.Y * 0.01f, FbxPreview.MinPitch, FbxPreview.MaxPitch);
        e.Handled = true;
        RequestFrame();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
            return;
        ReleaseDrag();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        NoteModifiers(e.KeyModifiers);
        if (!_armed || Orbit is not { CanTurn: true })
            return;
        var step = Math.Clamp(e.Delta.Y, -4, 4);
        if (step == 0)
            return;
        _zoom = Math.Clamp(_zoom * MathF.Pow(1.1f, (float)step), 0.35f, 6f);
        e.Handled = true;
        RequestFrame();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = true;
        base.OnAttachedToVisualTree(e);
        _keys = TopLevel.GetTopLevel(this);
        _keys?.AddHandler(KeyDownEvent, OnArmKey, RoutingStrategies.Tunnel);
        _keys?.AddHandler(KeyUpEvent, OnArmKey, RoutingStrategies.Tunnel);
        _keys?.AddHandler(PointerPressedEvent, RememberFocus, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_keys is not null)
        {
            _keys.RemoveHandler(KeyDownEvent, OnArmKey);
            _keys.RemoveHandler(KeyUpEvent, OnArmKey);
            _keys.RemoveHandler(PointerPressedEvent, RememberFocus);
            _keys = null;
        }

        _attached = false;
        ReleaseDrag();
        _request++;
        DropFrame();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_dragging)
            return;
        _dragging = false;
        _captured = null;
        UpdateCursor();
    }

    private void RememberFocus(object? sender, PointerPressedEventArgs e)
    {
        // The tunnel reaches the window before Avalonia focuses the row under the pointer.
        if (!ReferenceEquals(e.Source, this))
            return;
        _focusRestore = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
    }

    private void RestoreFocus()
    {
        if (!_attached)
            return;
        var manager = TopLevel.GetTopLevel(this)?.FocusManager;
        var current = manager?.GetFocusedElement();
        if (manager is null || current is not Visual focused)
            return;
        if (!ReferenceEquals(focused, this)
            && !this.IsVisualAncestorOf(focused)
            && !focused.IsVisualAncestorOf(this))
            return;
        if (ReferenceEquals(current, _focusRestore))
            return;
        manager.Focus(_focusRestore);
    }

    private void OnArmKey(object? sender, KeyEventArgs e)
    {
        var armed = Armed(e.KeyModifiers);
        if (e.RoutedEvent == KeyUpEvent && IsArmKey(e.Key))
        {
            // A key-up can still report the modifier that just went up. Drop that bit.
            var leftover = e.KeyModifiers;
            if (e.Key is Key.LeftCtrl or Key.RightCtrl)
                leftover &= ~KeyModifiers.Control;
            else
                leftover &= ~KeyModifiers.Meta;
            armed = Armed(leftover);
        }

        NoteArmed(armed);
    }

    private void NoteModifiers(KeyModifiers modifiers) => NoteArmed(Armed(modifiers));

    private void NoteArmed(bool armed)
    {
        if (armed == _armed)
            return;
        _armed = armed;
        if (_dragging && !armed)
            ReleaseDrag();
        else if (IsPointerOver)
            UpdateCursor();
    }

    private static bool Armed(KeyModifiers modifiers) =>
        (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

    private static bool IsArmKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LWin or Key.RWin;

    private void UpdateCursor()
    {
        if (_dragging)
            return;
        var hand = _armed && IsPointerOver && Orbit is { CanTurn: true };
        Cursor = new Cursor(hand ? StandardCursorType.Hand : StandardCursorType.Arrow);
    }

    private void ReleaseDrag()
    {
        var wasDragging = _dragging;
        _dragging = false;
        var pointer = _captured;
        _captured = null;
        if (pointer?.Captured == this)
            pointer.Capture(null);
        UpdateCursor();
        if (wasDragging)
            RestoreFocus();
    }

    private void ResetView(TappedEventArgs e)
    {
        // The taps that got us here already recorded the modifier. Trust that if this
        // event does not repeat it, so a double-click with Ctrl or Cmd still resets.
        if (Armed(e.KeyModifiers))
            _armed = true;
        if (!_armed || Orbit is not { CanTurn: true })
            return;
        e.Handled = true;
        if (e.Pointer.Captured == this)
            e.Pointer.Capture(null);
        _dragging = false;
        _captured = null;
        ResetAngles();
        RestoreFocus();
    }

    private void ResetAngles()
    {
        _request++;
        _yaw = FbxPreview.DefaultYaw;
        _pitch = FbxPreview.DefaultPitch;
        _zoom = FbxPreview.DefaultZoom;
        _dragging = false;
        DropFrame();
        UpdateCursor();
        ToolTip.SetTip(this, Orbit is { CanTurn: true } ? TurnTip : null);
        InvalidateVisual();
    }

    private bool IsHome =>
        Math.Abs(_yaw - FbxPreview.DefaultYaw) < 0.0001f
        && Math.Abs(_pitch - FbxPreview.DefaultPitch) < 0.0001f
        && Math.Abs(_zoom - FbxPreview.DefaultZoom) < 0.0001f;

    private void RequestFrame()
    {
        if (Orbit is not { CanTurn: true } orbit)
            return;
        var request = ++_request;
        if (IsHome)
        {
            DropFrame();
            InvalidateVisual();
            return;
        }

        if (_drawing)
            return;
        _drawing = true;
        var yaw = _yaw;
        var pitch = _pitch;
        var zoom = _zoom;
        _ = DrawFrame(orbit, request, yaw, pitch, zoom);
    }

    private async Task DrawFrame(FbxPreview.Orbit orbit, int request, float yaw, float pitch, float zoom)
    {
        byte[]? png = null;
        try
        {
            png = await Task.Run(() => orbit.Render(yaw, pitch, zoom)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            png = null;
        }

        await Dispatcher.UIThread.InvokeAsync(() => ApplyFrame(orbit, request, png));
    }

    private void ApplyFrame(FbxPreview.Orbit orbit, int request, byte[]? png)
    {
        _drawing = false;
        if (!_attached || png is null || !ReferenceEquals(Orbit, orbit) || IsHome)
        {
            if (_attached && ReferenceEquals(Orbit, orbit) && !IsHome && _request != request)
                RequestFrame();
            return;
        }

        Bitmap? bitmap;
        try
        {
            using var stream = new MemoryStream(png, writable: false);
            bitmap = new Bitmap(stream);
        }
        catch (Exception)
        {
            if (_request != request)
                RequestFrame();
            return;
        }

        var previous = _frame;
        _frame = bitmap;
        previous?.Dispose();
        InvalidateVisual();
        if (_request != request)
            RequestFrame();
    }

    private void DropFrame()
    {
        _frame?.Dispose();
        _frame = null;
    }
}
