using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace Sextant.Docking;

public sealed class SextantDockFactory : Factory
{
    private readonly RepositoryDock _dock;

    public SextantDockFactory(RepositoryDock dock)
    {
        _dock = dock;
        DefaultHostWindowLocator = () => new SextantHostWindow(dock);
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = DefaultHostWindowLocator,
        };
    }

    public DocumentDock? Home { get; set; }

    public override bool OnDockableClosing(IDockable? dockable)
    {
        if (_dock.Rebuilding)
            return true;
        return base.OnDockableClosing(dockable);
    }

    public override void OnDockableClosed(IDockable? dockable)
    {
        base.OnDockableClosed(dockable);
        if (_dock.Rebuilding)
            return;
        if (dockable is RepositoryTool tool)
            _dock.NoteToolClosed(tool);
    }

    public override void OnActiveDockableChanged(IDockable? dockable)
    {
        base.OnActiveDockableChanged(dockable);
        if (_dock.Rebuilding || _dock.Silence)
            return;
        _dock.NoteVisibility();
        _dock.NoteReview(dockable);
        _dock.NoteLocks(dockable);
    }

    public override void OnDockableAdded(IDockable? dockable)
    {
        base.OnDockableAdded(dockable);
        _dock.FitChangesHost(dockable);
    }

    public override void OnDockableRemoved(IDockable? dockable)
    {
        base.OnDockableRemoved(dockable);
        _dock.FitChangesHost(dockable);
    }

    public override void OnDockableMoved(IDockable? dockable)
    {
        base.OnDockableMoved(dockable);
        _dock.FitChangesHost(dockable);
    }

    public override void OnDockableDocked(IDockable? dockable, DockOperation operation)
    {
        base.OnDockableDocked(dockable, operation);
        _dock.FitChangesHost(dockable);
    }

    public override bool OnWindowClosing(IDockWindow? window)
    {
        if (!_dock.Rebuilding && window?.Layout is IDock layout)
        {
            foreach (var tool in Collect(layout).OfType<RepositoryTool>().ToList())
                _dock.Rehome(tool);
        }

        return base.OnWindowClosing(window);
    }

    public static IEnumerable<IDockable> Collect(IDockable? node)
    {
        if (node is null)
            yield break;
        yield return node;
        if (node is not IDock dock || dock.VisibleDockables is null)
            yield break;
        foreach (var child in dock.VisibleDockables)
        {
            foreach (var nested in Collect(child))
                yield return nested;
        }
    }
}
