using Dock.Model.Mvvm.Controls;

namespace Sextant.Docking;

public sealed class RepositoryTool : Tool
{
    public RepositoryTool(RepositoryDock dock, string toolId, string title)
    {
        Surface = dock;
        ToolId = toolId;
        Id = toolId;
        Title = title;
        CanClose = true;
        CanFloat = true;
        CanPin = true;
    }

    public RepositoryDock Surface { get; }

    public string ToolId { get; }
}
