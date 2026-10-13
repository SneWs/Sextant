using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Sextant.ViewModels;

namespace Sextant.Views;

public partial class RepositoryFilesToolView : UserControl
{
    public RepositoryFilesToolView()
    {
        InitializeComponent();
    }

    private void OnRepositoryDirectoryExpand(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { DataContext: RepositoryFileTreeItem item } && DataContext is RepositoryViewModel vm)
            vm.ToggleRepositoryDirectory(item);
    }

    private void OnLocationExpandDoubleTapped(object? sender, TappedEventArgs e) => e.Handled = true;

    private void OnRepositoryFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;
        if (DataContext is RepositoryViewModel vm && vm.SelectedRepositoryFile is { IsDirectory: true } item)
        {
            vm.ToggleRepositoryDirectory(item);
            e.Handled = true;
        }
    }

    private void OnRepositoryFileKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not RepositoryViewModel vm || vm.SelectedRepositoryFile is not { } item)
            return;
        if (e.Key == Key.Right && item.IsDirectory && !item.IsExpanded
            || e.Key == Key.Left && item.IsDirectory && item.IsExpanded)
        {
            vm.ToggleRepositoryDirectory(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Left && item.Depth > 0)
        {
            var parent = item.Path[..item.Path.LastIndexOf('/')];
            vm.SelectedRepositoryFile = vm.RepositoryFileTree.FirstOrDefault(row => row.IsDirectory && row.Path == parent);
            e.Handled = true;
        }
    }

    private void OnMenuOpening(object? sender, CancelEventArgs e) => MenuOpen.OnOpening(sender, e);
}
