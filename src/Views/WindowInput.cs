using Avalonia.Input;
using Avalonia.Interactivity;
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
            if (vm.CanRunRepositoryCommands)
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

        if (e.Key == Key.Escape && vm.PaletteOpen)
        {
            vm.ClosePalette();
            e.Handled = true;
            return;
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
        if (e.Handled || e.Route != RoutingStrategies.Bubble || e.Key != Key.Escape || vm.ActiveTab is not { HasHistoryQuery: true } tab)
            return;
        tab.ShowAllCommitsCommand.Execute(null);
        e.Handled = true;
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
