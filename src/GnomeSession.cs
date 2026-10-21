namespace Sextant;

/// <summary>
/// Mutter will not decorate a native Wayland window, and a drawn title bar does not match the
/// desktop theme. A GNOME session stays on X11 so Mutter draws the frame. KDE and Cinnamon
/// decorate Wayland windows, so those sessions stay on Wayland. A GNOME session is named in
/// <c>XDG_CURRENT_DESKTOP</c> or <c>XDG_SESSION_DESKTOP</c> as GNOME or GNOME-something.
/// </summary>
public static class GnomeSession
{
    public static bool PrefersWayland(string? currentDesktop, string? sessionDesktop)
        => !IsGnome(currentDesktop, sessionDesktop);

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
