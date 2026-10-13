using System.Reflection;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace Sextant.Views;

/// <summary>
/// Copies named controls from child views into the host namescope.
/// FindControl does not cross a child UserControl namescope, and a completed
/// scope rejects Register. The dictionary is filled directly so names stay findable.
/// </summary>
static class NameShare
{
    private static readonly FieldInfo? InnerField = typeof(NameScope).GetField(
        "_inner",
        BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Publish(Control host)
    {
        if (host.FindNameScope() is not NameScope scope || InnerField?.GetValue(scope) is not Dictionary<string, object> names)
            return;
        // Logical children exist as soon as the views are built. Visual children appear
        // later, once templates apply, which is after some tests call FindControl.
        foreach (var control in host.GetLogicalDescendants().OfType<Control>())
            Remember(names, control);
        foreach (var control in host.GetVisualDescendants().OfType<Control>())
            Remember(names, control);
    }

    private static void Remember(Dictionary<string, object> names, Control control)
    {
        if (string.IsNullOrEmpty(control.Name) || names.ContainsKey(control.Name))
            return;
        names.Add(control.Name, control);
    }
}
