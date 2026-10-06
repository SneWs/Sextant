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

            return DisplayVersion(info);
        }
    }

    /// <summary>
    /// The version shown in About. A leading <c>v</c> is only the workflow tag prefix and is not part of the version.
    /// A <c>+</c> source revision is not shown.
    /// </summary>
    internal static string DisplayVersion(string informational)
    {
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
            informational = informational[..plus];
        if (informational.Length > 1
            && (informational[0] == 'v' || informational[0] == 'V')
            && char.IsDigit(informational[1]))
            informational = informational[1..];
        return informational;
    }
}
