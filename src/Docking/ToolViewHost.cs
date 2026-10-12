using Avalonia.Controls;
using Avalonia.Data;
using Sextant.Git.Workspace;
using Sextant.Views;

namespace Sextant.Docking;

public sealed class ToolViewHost : Panel
{
    public ToolViewHost(RepositoryTool tool)
    {
        Control view = tool.ToolId switch
        {
            DockIds.Locations => new LocationsToolView(),
            DockIds.History => new HistoryToolView(),
            DockIds.Changes => new ChangesToolView(),
            DockIds.Files => new RepositoryFilesToolView(),
            DockIds.Commands => new CommandLogToolView(),
            DockIds.Diff or DockIds.Blame => new DiffDocumentView(),
            _ => new Panel(),
        };
        view.Bind(DataContextProperty, new Binding(nameof(RepositoryDock.Focused)) { Source = tool.Surface });
        Children.Add(view);
    }
}
