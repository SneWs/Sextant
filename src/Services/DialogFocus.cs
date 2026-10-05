using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;

namespace Sextant.Services;

/// <summary>
/// Dialogs open with focus on the window, so the first field is focused on the next turn.
/// </summary>
internal static class DialogFocus
{
    public static void WhenShown(Window window) =>
        WhenShown(window, () => window.Content as Control);

    public static void WhenShown(Window window, Func<Control?> root)
    {
        var activated = false;
        window.Opened += (_, _) => Schedule();
        window.Activated += (_, _) =>
        {
            if (activated)
                return;
            activated = true;
            Schedule();
        };

        void Schedule()
        {
            // Background runs once the window is up. A later idle pass runs after the
            // platform gives the new window keyboard focus, which would otherwise clear the field.
            Dispatcher.UIThread.Post(() => Apply(root()), DispatcherPriority.Background);
            Dispatcher.UIThread.Post(() => Apply(root()), DispatcherPriority.ContextIdle);
        }
    }

    public static InputElement? FirstField(Control? root)
    {
        if (root is null)
            return null;
        return Walk(root, root);
    }

    private static void Apply(Control? root)
    {
        var field = FirstField(root);
        if (field is null)
            return;
        // A list does not take focus until it is marked focusable, unlike a text box.
        if (field is ListBox list)
            list.Focusable = true;
        if (!field.Focus())
            return;
        if (field is TextBox box)
            box.SelectAll();
    }

    private static InputElement? Walk(Control node, Control scope)
    {
        if (!node.IsVisible)
            return null;
        if (node is RadioButton radio)
        {
            if (!radio.IsEnabled)
                return null;
            return Checked(scope, radio.GroupName) ?? radio;
        }

        if (IsField(node))
            return node.IsEnabled ? node : null;

        if (node is Button)
            return null;

        foreach (var child in node.GetLogicalChildren().OfType<Control>())
        {
            var found = Walk(child, scope);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static RadioButton? Checked(Control scope, string? group)
    {
        foreach (var radio in Radios(scope))
        {
            if (radio.IsVisible && radio.IsEnabled && radio.IsChecked == true && radio.GroupName == group)
                return radio;
        }

        return null;
    }

    private static IEnumerable<RadioButton> Radios(Control node)
    {
        if (!node.IsVisible)
            yield break;
        if (node is RadioButton radio)
        {
            yield return radio;
            yield break;
        }

        foreach (var child in node.GetLogicalChildren().OfType<Control>())
        {
            foreach (var nested in Radios(child))
                yield return nested;
        }
    }

    private static bool IsField(Control control) =>
        control is TextBox or ComboBox or ListBox or CheckBox;
}
