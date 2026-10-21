using Avalonia;

namespace Sextant;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        LinuxDesktopEntry.EnsureInstalled();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();
        // GNOME only paints its own corners and caption buttons on an X11 window.
        if (!OperatingSystem.IsLinux() || GnomeSession.PrefersWayland(
                Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
                Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP")))
            builder = builder.UseWaylandWithFallback();
#if DEBUG
        builder = builder.WithDeveloperTools();
#endif
        return builder
            .WithInterFont()
            .LogToTrace();
    }
}
