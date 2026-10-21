using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Sextant;

/// <summary>
/// Drawn title bar used when GNOME will not decorate the window. Other desktops keep the compositor's frame.
/// </summary>
public class GnomeWindowChrome : Styles
{
    public const string ButtonLayoutKey = "GnomeButtonLayout";

    public GnomeWindowChrome(IServiceProvider? serviceProvider = null)
    {
        AvaloniaXamlLoader.Load(serviceProvider, this);
    }

    public static void ApplyCurrent(Application app)
    {
        if (!OperatingSystem.IsLinux())
            return;
        Apply(
            app,
            Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
            Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP"),
            GnomeButtonLayout.FromGsettings());
    }

    public static void Apply(Application app, string? currentDesktop, string? sessionDesktop, string? buttonLayout)
    {
        if (!GnomeSession.IsGnome(currentDesktop, sessionDesktop))
            return;
        if (app.Styles.OfType<GnomeWindowChrome>().Any())
            return;
        app.Resources[ButtonLayoutKey] = GnomeButtonLayout.Normalize(buttonLayout);
        app.Styles.Add(new GnomeWindowChrome());
    }
}
