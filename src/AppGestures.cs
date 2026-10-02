using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Sextant;

static class AppGestures
{
    public static KeyModifiers Command { get; } =
        OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    public static KeyGesture CommandKey(Key key, KeyModifiers extra = KeyModifiers.None) =>
        new(key, Command | extra);

    public static bool Matches(KeyEventArgs e, Key key, KeyModifiers extra = KeyModifiers.None) =>
        e.Key == key && e.KeyModifiers == (Command | extra);
}

/// <summary>A menu shortcut that uses Command on macOS and Control elsewhere.</summary>
public sealed class CommandGestureExtension : MarkupExtension
{
    public Key Key { get; set; }

    public bool Shift { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        AppGestures.CommandKey(Key, Shift ? KeyModifiers.Shift : KeyModifiers.None);
}
