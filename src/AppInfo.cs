using System.Reflection;

namespace Sextant;

public static class AppInfo
{
    public const string Copyright = "Copyright © 2026 Marcus Grenängen";

    public const string License =
        "Free to use when you build it from source, or when you install a release from GitHub";

    public static string Version
    {
        get
        {
            var info = typeof(AppInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            
            if (string.IsNullOrWhiteSpace(info))
                return "1.0.0";

            var plus = info.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? info[..plus] : info;
        }
    }
}
