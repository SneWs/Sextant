using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Sextant;

/// <summary>The latest GitHub release for this repository.</summary>
internal interface IReleaseCatalog
{
    Task<PublishedRelease?> LatestAsync(CancellationToken cancellationToken);
}

internal sealed record PublishedRelease(string Tag, string Version, string PageUrl, IReadOnlyList<ReleaseAsset> Assets);

internal sealed record ReleaseAsset(string Name, string Url);

internal enum ReleaseOs
{
    Mac,
    Windows,
    Linux,
}

internal enum LinuxPackageKind
{
    Deb,
    Rpm,
}

internal readonly record struct ReleaseMachine(ReleaseOs Os, Architecture Architecture, LinuxPackageKind? Package);

internal enum ReleaseCompare
{
    Newer,
    Same,
    Older,
    Unknown,
}

/// <summary>What Check for update tells the user, and which download it opens.</summary>
internal sealed record ReleaseOffer(string LatestVersion, string Url, string Confirm, string Message);

internal static class ReleaseCheck
{
    public const string ReleasesPage = "https://github.com/SneWs/Sextant/releases";

    public const string LatestApi = "https://api.github.com/repos/SneWs/Sextant/releases/latest";

    /// <summary>
    /// Unit tests load xUnit. A startup check from those sessions would call GitHub and could open a dialog.
    /// </summary>
    internal static bool IsTestProcess { get; } = DetectTestProcess();

    public static ReleaseMachine Current()
    {
        var os = OperatingSystem.IsMacOS()
            ? ReleaseOs.Mac
            : OperatingSystem.IsWindows()
                ? ReleaseOs.Windows
                : ReleaseOs.Linux;
        LinuxPackageKind? package = null;
        if (os == ReleaseOs.Linux)
            package = PackageFromOsRelease(ReadOsRelease());
        return new ReleaseMachine(os, RuntimeInformation.OSArchitecture, package);
    }

    internal static string? AssetName(ReleaseMachine machine) => machine.Os switch
    {
        ReleaseOs.Mac when machine.Architecture == Architecture.Arm64 => "Sextant-osx-arm64.tar",
        ReleaseOs.Windows when machine.Architecture == Architecture.X64 => "Sextant-win-x64.zip",
        ReleaseOs.Windows when machine.Architecture == Architecture.Arm64 => "Sextant-win-arm64.zip",
        ReleaseOs.Linux when machine.Package == LinuxPackageKind.Deb && machine.Architecture == Architecture.X64 => "Sextant-linux-x64.deb",
        ReleaseOs.Linux when machine.Package == LinuxPackageKind.Deb && machine.Architecture == Architecture.Arm64 => "Sextant-linux-arm64.deb",
        ReleaseOs.Linux when machine.Package == LinuxPackageKind.Rpm && machine.Architecture == Architecture.X64 => "Sextant-linux-x64.rpm",
        ReleaseOs.Linux when machine.Package == LinuxPackageKind.Rpm && machine.Architecture == Architecture.Arm64 => "Sextant-linux-arm64.rpm",
        _ => null,
    };

    internal static LinuxPackageKind? PackageFromOsRelease(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        string? id = null;
        string? like = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            var split = line.IndexOf('=');
            if (split <= 0)
                continue;
            var key = line[..split];
            var value = line[(split + 1)..].Trim().Trim('"');
            if (key == "ID")
                id = value;
            else if (key == "ID_LIKE")
                like = value;
        }

        if (PackageOf(id) is { } fromId)
            return fromId;
        if (like is null)
            return null;
        foreach (var part in like.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (PackageOf(part) is { } fromLike)
                return fromLike;
        }

        return null;
    }

    internal static bool IsReleaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            return false;
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return false;
        var path = uri.AbsolutePath;
        return path.Equals("/SneWs/Sextant/releases", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/SneWs/Sextant/releases/", StringComparison.OrdinalIgnoreCase);
    }

    internal static PublishedRelease? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("tag_name", out var tagElement)
            || tagElement.ValueKind != JsonValueKind.String)
            return null;
        var tag = tagElement.GetString();
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        var page = root.TryGetProperty("html_url", out var pageElement) && pageElement.ValueKind == JsonValueKind.String
            ? pageElement.GetString()
            : null;
        if (!IsReleaseUrl(page))
            page = ReleasesPage;
        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetArray) && assetArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetArray.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : null;
                var url = asset.TryGetProperty("browser_download_url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String
                    ? urlElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(name) || !IsReleaseUrl(url))
                    continue;
                assets.Add(new ReleaseAsset(name, url!));
            }
        }

        return new PublishedRelease(tag, AppInfo.DisplayVersion(tag), page!, assets);
    }

    internal static ReleaseOffer? Create(PublishedRelease release, string currentVersion, ReleaseMachine machine)
    {
        if (ReleaseVersion.Compare(release.Version, currentVersion) != ReleaseCompare.Newer)
            return null;
        var name = AssetName(machine);
        var asset = name is null
            ? null
            : release.Assets.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (asset is not null)
        {
            return new ReleaseOffer(
                release.Version,
                asset.Url,
                "Download",
                $"Sextant {release.Version} is available. This copy is {currentVersion}.\n\n{InstallSentence(asset.Name)}");
        }

        return new ReleaseOffer(
            release.Version,
            release.PageUrl,
            "Open releases",
            $"Sextant {release.Version} is available. This copy is {currentVersion}.\n\nOpen the releases page and choose the download for this computer. Settings and open tabs stay on disk.");
    }

    private static string InstallSentence(string fileName)
    {
        if (fileName.EndsWith(".tar", StringComparison.Ordinal))
            return $"Download {fileName}. Quit Sextant, extract it, and replace Sextant.app. Settings and open tabs stay on disk.";
        if (fileName.EndsWith(".zip", StringComparison.Ordinal))
            return $"Download {fileName}. Quit Sextant, extract it, and replace this folder. Settings and open tabs stay on disk.";
        if (fileName.EndsWith(".deb", StringComparison.Ordinal))
            return $"Download {fileName}, then install it with apt. Settings and open tabs stay on disk.";
        if (fileName.EndsWith(".rpm", StringComparison.Ordinal))
            return $"Download {fileName}, then install it with dnf. Settings and open tabs stay on disk.";
        return $"Download {fileName}. Quit Sextant and install that file. Settings and open tabs stay on disk.";
    }

    private static LinuxPackageKind? PackageOf(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        if (DebIds.Contains(id) || id.StartsWith("ubuntu", StringComparison.Ordinal))
            return LinuxPackageKind.Deb;
        if (RpmIds.Contains(id) || id.StartsWith("opensuse", StringComparison.Ordinal) || id.StartsWith("suse", StringComparison.Ordinal))
            return LinuxPackageKind.Rpm;
        return null;
    }

    private static readonly HashSet<string> DebIds = new(StringComparer.Ordinal)
    {
        "debian", "ubuntu", "linuxmint", "pop", "neon", "raspbian", "kali", "elementary",
    };

    private static readonly HashSet<string> RpmIds = new(StringComparer.Ordinal)
    {
        "fedora", "rhel", "centos", "rocky", "almalinux", "ol", "amzn", "sles",
    };

    private static string? ReadOsRelease()
    {
        try
        {
            return File.Exists("/etc/os-release") ? File.ReadAllText("/etc/os-release") : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool DetectTestProcess()
    {
        var entry = Assembly.GetEntryAssembly()?.GetName().Name;
        if (entry is "testhost" or "xunit.console" or "ReSharperTestRunner")
            return true;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name;
            if (name is "xunit.core" or "xunit.execution.dotnet" or "nunit.framework")
                return true;
        }

        return false;
    }
}

internal static class ReleaseVersion
{
    internal static ReleaseCompare Compare(string latest, string current)
    {
        if (!TryParse(latest, out var left) || !TryParse(current, out var right))
            return ReleaseCompare.Unknown;
        var compared = left.CompareTo(right);
        if (compared > 0)
            return ReleaseCompare.Newer;
        if (compared < 0)
            return ReleaseCompare.Older;
        return ReleaseCompare.Same;
    }

    private static bool TryParse(string text, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        text = text.Trim();
        var plus = text.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
            text = text[..plus];
        if (text.Length > 1 && (text[0] == 'v' || text[0] == 'V') && char.IsDigit(text[1]))
            text = text[1..];
        string? pre = null;
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            pre = text[(dash + 1)..];
            if (pre.Length == 0)
                return false;
            text = text[..dash];
        }

        var parts = text.Split('.');
        if (parts.Length is < 1 or > 3)
            return false;
        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0
                || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])
                || numbers[i] < 0)
                return false;
        }

        version = new SemVer(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    private readonly record struct SemVer(int Major, int Minor, int Patch, string? Pre) : IComparable<SemVer>
    {
        public int CompareTo(SemVer other)
        {
            var major = Major.CompareTo(other.Major);
            if (major != 0)
                return major;
            var minor = Minor.CompareTo(other.Minor);
            if (minor != 0)
                return minor;
            var patch = Patch.CompareTo(other.Patch);
            if (patch != 0)
                return patch;
            if (Pre is null && other.Pre is null)
                return 0;
            if (Pre is null)
                return 1;
            if (other.Pre is null)
                return -1;
            return ComparePre(Pre, other.Pre);
        }

        private static int ComparePre(string left, string right)
        {
            var a = left.Split('.');
            var b = right.Split('.');
            var count = Math.Min(a.Length, b.Length);
            for (var i = 0; i < count; i++)
            {
                var compared = CompareId(a[i], b[i]);
                if (compared != 0)
                    return compared;
            }

            return a.Length.CompareTo(b.Length);
        }

        private static int CompareId(string left, string right)
        {
            var leftNumber = IsNumber(left);
            var rightNumber = IsNumber(right);
            if (leftNumber && rightNumber)
            {
                return long.Parse(left, CultureInfo.InvariantCulture)
                    .CompareTo(long.Parse(right, CultureInfo.InvariantCulture));
            }

            if (leftNumber)
                return -1;
            if (rightNumber)
                return 1;
            return string.CompareOrdinal(left, right);
        }

        private static bool IsNumber(string text) =>
            text.Length > 0 && text.All(character => character is >= '0' and <= '9');
    }
}

internal sealed class GitHubReleaseCatalog : IReleaseCatalog
{
    public async Task<PublishedRelease?> LatestAsync(CancellationToken cancellationToken)
    {
        if (ReleaseCheck.IsTestProcess)
            throw new InvalidOperationException("The release check does not call GitHub during tests.");

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseCheck.LatestApi);
        request.Headers.TryAddWithoutValidation("User-Agent", "Sextant");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ReleaseCheck.Parse(json);
    }
}
