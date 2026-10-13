using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Sextant;
using Sextant.ViewModels;

namespace Sextant.Views;

public partial class ChangesToolView : UserControl
{
    public ChangesToolView()
    {
        InitializeComponent();
        // The TextBox class handler eats every Enter (AcceptsReturn) and marks it handled before
        // any bubbling KeyDown handler runs, so Ctrl/Cmd+Enter must be taken on the tunneling route.
        CommitMessageInput.AddHandler(
            InputElement.KeyDownEvent,
            OnCommitKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void OnCommitKeyDown(object? sender, KeyEventArgs e)
    {
        if (AppGestures.Matches(e, Key.Enter)
            && DataContext is RepositoryViewModel vm
            && vm.CommitCommand.CanExecute(null))
        {
            vm.CommitCommand.Execute(null);
            e.Handled = true;
        }
    }
}
