namespace Sextant;

/// <summary>
/// GNOME's compositor does not advertise server-side decorations to a native Wayland window.
/// KDE and Cinnamon do, so those sessions keep the desktop's frame. A GNOME session is named in
/// <c>XDG_CURRENT_DESKTOP</c> or <c>XDG_SESSION_DESKTOP</c> as GNOME or GNOME-something.
/// </summary>
public static class GnomeSession
{
    public static bool IsGnome(string? currentDesktop, string? sessionDesktop)
        => Names(currentDesktop).Any(IsGnomeName) || Names(sessionDesktop).Any(IsGnomeName);

    private static bool IsGnomeName(string name)
        => name.Equals("gnome", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith("gnome-", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Names(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;
        foreach (var name in value.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return name;
    }
}
