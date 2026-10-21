using System.ComponentModel;
using Avalonia.Controls;
using Sextant.ViewModels;

namespace Sextant.Views;

/// <summary>Context menus in the repository surface share one opening check.</summary>
static class MenuOpen
{
    public static void OnOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.PlacementTarget is not Control target)
            return;
        menu.DataContext = target.DataContext;
        if (target.DataContext is LocationItem item && !HasLocationMenu(item))
            e.Cancel = true;
        if (target.DataContext is DiffFileRow { FileMenu: null })
            e.Cancel = true;
        if (target.DataContext is RepositoryFileTreeItem { IsDirectory: true })
            e.Cancel = true;
    }

    private static bool HasLocationMenu(LocationItem item) =>
        item.ShowCheckout || item.ShowMerge || item.ShowRebase || item.ShowDelete || item.ShowSetUpstream || item.ShowReveal
        || item.ShowHide || item.ShowAllBranches
        || item.ShowRename || item.ShowPop || item.ShowApply || item.ShowDrop || item.ShowOpen || item.ShowTag
        || item.ShowRemove || item.ShowLock || item.ShowUnlock || item.ShowMove;
}
