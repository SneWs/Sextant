using System.Text;

namespace Sextant.Git.Wsl;

public sealed record WslDistro(string Name, string State, bool IsDefault, int Version);

/// <summary>
/// Reads <c>wsl.exe --list --verbose</c>. That command writes UTF-16 to a pipe.
/// </summary>
public static class WslList
{
    public static bool IsWindows11() =>
        OperatingSystem.IsWindows() && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    public static bool IsInfrastructure(string name) =>
        name.Equals("docker-desktop", StringComparison.OrdinalIgnoreCase)
        || name.Equals("docker-desktop-data", StringComparison.OrdinalIgnoreCase);

    public static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
            return "";
        string text;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        else if (LooksLikeUtf16(bytes))
            text = Encoding.Unicode.GetString(bytes);
        else
            text = Encoding.UTF8.GetString(bytes);
        return text.Replace("\0", "", StringComparison.Ordinal).Trim();
    }

    public static IReadOnlyList<WslDistro> Parse(string text)
    {
        var distros = new List<WslDistro>();
        var skippedHeader = false;
        foreach (var raw in text.Replace("\0", "", StringComparison.Ordinal).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (!skippedHeader && !char.IsDigit(line[^1]))
            {
                skippedHeader = true;
                continue;
            }

            skippedHeader = true;
            var isDefault = line.StartsWith('*');
            if (isDefault)
                line = line[1..].Trim();
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !int.TryParse(parts[^1], out var version))
                continue;
            var name = string.Join(' ', parts[..^2]);
            if (name.Length == 0)
                continue;
            distros.Add(new WslDistro(name, parts[^2], isDefault, version));
        }

        return distros;
    }

    /// <summary>
    /// WSL2 distributions a person would keep a repository in. Docker's own
    /// distributions stay out unless they are the only WSL2 installs.
    /// </summary>
    public static IReadOnlyList<WslDistro> UserDistros(IReadOnlyList<WslDistro> all)
    {
        var version2 = all.Where(distro => distro.Version >= 2).ToList();
        var users = version2.Where(distro => !IsInfrastructure(distro.Name)).ToList();
        var chosen = users.Count > 0 ? users : version2;
        return chosen
            .OrderByDescending(distro => distro.IsDefault)
            .ThenBy(distro => distro.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool LooksLikeUtf16(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes.Length % 2 != 0)
            return false;
        var zeros = 0;
        var samples = Math.Min(bytes.Length, 64);
        var checkedBytes = 0;
        for (var i = 1; i < samples; i += 2)
        {
            checkedBytes++;
            if (bytes[i] == 0)
                zeros++;
        }

        return checkedBytes > 0 && zeros * 2 >= checkedBytes;
    }
}
