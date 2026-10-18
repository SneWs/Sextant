using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Sextant.Docking;
using Sextant.Git.Workspace;
using Sextant.ViewModels;

namespace Sextant.Views;

public static class WindowInput
{
    public static async void OnTunnel(MainViewModel vm, KeyEventArgs e)
    {
        if (AppGestures.Matches(e, Key.P))
        {
            vm.TogglePalette();
            e.Handled = true;
            return;
        }

        if (AppGestures.Matches(e, Key.O))
        {
            e.Handled = true;
            if (vm.CanUseGit)
                await vm.OpenFolderCommand.ExecuteAsync(null);
            return;
        }

        if (AppGestures.Matches(e, Key.P, KeyModifiers.Shift))
        {
            e.Handled = true;
            if (vm.PushCommand.CanExecute(null))
                vm.PushCommand.Execute(null);
            return;
        }

        if (AppGestures.Matches(e, Key.U, KeyModifiers.Shift))
        {
            e.Handled = true;
            if (vm.CanRunRepositoryCommands)
                vm.PullCommand.Execute(null);
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
            e.Handled = true;
            if (branchTab.CanRunCommands)
                await branchTab.CreateBranchCommand.ExecuteAsync(null);
            return;
        }

        if (AppGestures.Matches(e, Key.S))
        {
            e.Handled = true;
            if (vm.StashCommand.CanExecute(null))
                await vm.StashCommand.ExecuteAsync(null);
            return;
        }

        if (AppGestures.Matches(e, Key.S, KeyModifiers.Shift))
        {
            e.Handled = true;
            if (vm.PopLatestStashCommand.CanExecute(null))
                await vm.PopLatestStashCommand.ExecuteAsync(null);
            return;
        }

        if (AppGestures.Matches(e, Key.A, KeyModifiers.Shift))
        {
            e.Handled = true;
            if (vm.StageAllCommand.CanExecute(null))
                await vm.StageAllCommand.ExecuteAsync(null);
            return;
        }

        if (AppGestures.Matches(e, Key.R, KeyModifiers.Shift))
        {
            e.Handled = true;
            if (vm.UnstageAllCommand.CanExecute(null))
                await vm.UnstageAllCommand.ExecuteAsync(null);
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.Shift)
        {
            if (vm.ActiveTab is not { } tab)
                return;
            e.Handled = true;
            if (vm.PaletteOpen)
                vm.ClosePalette();
            // Capture the window before Show rebuilds the dock tree and detaches the source.
            var origin = TopLevel.GetTopLevel(e.Source as Visual);
            SelectWorkingCopy(tab, origin, vm.Dock);
            vm.Dock.Show(DockIds.Changes);
            ScrollHistoryToTop(origin, vm.Dock);
            ScheduleCommitFocus(origin, vm.Dock);
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            if (TakeDoubleEscape(vm) && vm.ActiveTab is { } home)
            {
                e.Handled = true;
                var origin = TopLevel.GetTopLevel(e.Source as Visual);
                RestoreHome(vm, home, origin);
                return;
            }

            if (vm.PaletteOpen)
            {
                vm.ClosePalette();
                e.Handled = true;
                return;
            }
        }

        if (AppGestures.Matches(e, Key.W))
        {
            e.Handled = true;
            if (vm.PaletteOpen)
                vm.ClosePalette();
            else
                await vm.CloseActiveCommand.ExecuteAsync(null);
            return;
        }

        // Command+Tab is the macOS application switcher, so next tab is Control+Tab everywhere.
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            await vm.NextTabCommand.ExecuteAsync(null);
            return;
        }

        if (!vm.PaletteOpen && e.KeyModifiers == AppGestures.Command && Digit(e.Key) is int digit)
        {
            e.Handled = true;
            await vm.ActivateDigit(digit);
            return;
        }

        if (e.Key == Key.F5)
        {
            e.Handled = true;
            await vm.RefreshActiveCommand.ExecuteAsync(null);
        }
    }

    public static void OnHistoryEscape(MainViewModel vm, KeyEventArgs e)
    {
        if (e.Handled || e.Route != RoutingStrategies.Bubble || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None
            || vm.ActiveTab is not { HasHistoryQuery: true } tab)
            return;
        tab.ShowAllCommitsCommand.Execute(null);
        e.Handled = true;
    }

    private static MainViewModel? _escapeOwner;
    private static long _escapeAt;
    private const int DoubleEscapeMilliseconds = 500;

    /// <summary>The second plain Escape within the double-press window restores the working layout.</summary>
    private static bool TakeDoubleEscape(MainViewModel vm)
    {
        var now = Environment.TickCount64;
        var doubled = ReferenceEquals(_escapeOwner, vm) && now - _escapeAt <= DoubleEscapeMilliseconds;
        _escapeOwner = vm;
        _escapeAt = now;
        return doubled;
    }

    /// <summary>
    /// History returns to its top row and its tab, the review group returns to its first tab,
    /// find boxes clear, and the commit message takes focus.
    /// </summary>
    private static void RestoreHome(MainViewModel vm, RepositoryViewModel tab, TopLevel? origin)
    {
        if (vm.PaletteOpen)
            vm.ClosePalette();
        var clear = tab.ClearFind();
        vm.Dock.SelectGrouped(DockIds.History);
        vm.Dock.SelectFirstReviewTab();
        SelectWorkingCopy(tab, origin, vm.Dock);
        vm.Dock.Show(DockIds.Changes);
        ScrollHistoryToTop(origin, vm.Dock);
        ScheduleCommitFocus(origin, vm.Dock);
        if (!clear.IsCompleted)
            _ = ScrollAfterFind(clear, tab, origin, vm.Dock);
    }

    private static async Task ScrollAfterFind(Task clear, RepositoryViewModel tab, TopLevel? origin, RepositoryDock dock)
    {
        try
        {
            await clear.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        SelectWorkingCopy(tab, origin, dock);
        ScrollHistoryToTop(origin, dock);
    }

    /// <summary>
    /// The history list's top row is the working copy. Selecting it shows the commit message field.
    /// A multi-select range is cleared so the details load is not held on the older commits.
    /// </summary>
    private static void SelectWorkingCopy(RepositoryViewModel tab, TopLevel? origin, RepositoryDock dock)
    {
        var working = tab.Rows.FirstOrDefault(row => row.IsWorkingCopy);
        if (working is null)
            return;
        tab.SelectedGraphRow = working;
        foreach (var list in NamedLists(origin, dock, "GraphList").ToList())
        {
            if (list.SelectedItems is not { } items)
                continue;
            if (items.Count == 1 && ReferenceEquals(items[0], working))
                continue;
            items.Clear();
            items.Add(working);
        }

        _ = tab.NoteGraphSelection([working]);
    }

    /// <summary>
    /// The commit box is built when the pane opens, and the working-copy field appears after the
    /// graph selection loads, so focus waits until that layout has run.
    /// </summary>
    private static void ScheduleCommitFocus(TopLevel? origin, RepositoryDock dock)
    {
        Dispatcher.UIThread.Post(() => Apply(origin, dock), DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(() => Apply(origin, dock), DispatcherPriority.ContextIdle);

        static void Apply(TopLevel? origin, RepositoryDock dock)
        {
            ScrollHistoryToTop(origin, dock);
            var box = CommitMessageBox(origin, dock);
            if (box is null)
                return;
            box.Focus();
        }
    }

    private static void ScrollHistoryToTop(TopLevel? origin, RepositoryDock dock)
    {
        foreach (var list in NamedLists(origin, dock, "GraphList").ToList())
        {
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroll is null || scroll.Offset.Y <= 0)
                continue;
            scroll.Offset = new Vector(scroll.Offset.X, 0);
        }
    }

    private static TextBox? CommitMessageBox(TopLevel? origin, RepositoryDock dock)
    {
        TextBox? hidden = null;
        foreach (var root in Roots(origin, dock))
        {
            foreach (var box in root.GetVisualDescendants().OfType<TextBox>())
            {
                if (box.Name != "CommitMessageInput")
                    continue;
                if (box.IsEffectivelyVisible)
                    return box;
                hidden ??= box;
            }
        }

        return hidden;
    }

    private static IEnumerable<ListBox> NamedLists(TopLevel? origin, RepositoryDock dock, string name)
    {
        foreach (var root in Roots(origin, dock))
        {
            foreach (var list in root.GetVisualDescendants().OfType<ListBox>())
            {
                if (list.Name == name)
                    yield return list;
            }
        }
    }

    private static IEnumerable<Visual> Roots(TopLevel? origin, RepositoryDock dock)
    {
        var seen = new HashSet<Visual>();
        if (origin is not null && seen.Add(origin))
            yield return origin;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                if (seen.Add(window))
                    yield return window;
            }
        }

        if (dock.Root?.Windows is null)
            yield break;
        foreach (var model in dock.Root.Windows)
        {
            if (model.Host is Visual host && seen.Add(host))
                yield return host;
        }
    }

    public static int? Digit(Key key) => key switch
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
