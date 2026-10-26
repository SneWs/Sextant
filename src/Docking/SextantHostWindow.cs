using Avalonia.Input;
using Avalonia.Interactivity;
using Dock.Avalonia.Controls;
using Sextant.Views;

namespace Sextant.Docking;

/// <summary>A floating dock window. It has no menu of its own; the main window keeps the native menu.</summary>
public sealed class SextantHostWindow : HostWindow
{
    private readonly RepositoryDock _dock;

    public SextantHostWindow(RepositoryDock dock)
    {
        _dock = dock;
        AddHandler(KeyDownEvent, OnTunnel, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnBubble, RoutingStrategies.Bubble);
    }

    private void OnTunnel(object? sender, KeyEventArgs e) => WindowInput.OnTunnel(_dock.Model, e);

    private void OnKeyUp(object? sender, KeyEventArgs e) => WindowInput.OnKeyUp(_dock.Model, e);

    private void OnBubble(object? sender, KeyEventArgs e) => WindowInput.OnHistoryEscape(_dock.Model, e);
}
