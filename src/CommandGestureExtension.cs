using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Sextant;

/// <summary>A menu shortcut that uses Command on macOS and Control elsewhere.</summary>
public sealed class CommandGestureExtension : MarkupExtension
{
    public Key Key { get; set; }

    public bool Shift { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        AppGestures.CommandKey(Key, Shift ? KeyModifiers.Shift : KeyModifiers.None);
}