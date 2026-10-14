using System.Runtime.InteropServices;
using Sextant;

namespace Sextant.Git.Tests;

public class ReleaseCheckTests
{
    [Fact]
    public void Versions_follow_the_release_order()
    {
        Assert.Equal(ReleaseCompare.Newer, ReleaseVersion.Compare("1.2.0", "1.1.9"));
        Assert.Equal(ReleaseCompare.Newer, ReleaseVersion.Compare("1.0.0", "1.0.0-beta"));
        Assert.Equal(ReleaseCompare.Newer, ReleaseVersion.Compare("1.0.0-beta.2", "1.0.0-beta.1"));
        Assert.Equal(ReleaseCompare.Newer, ReleaseVersion.Compare("1.0.0-beta.2", "1.0.0-beta"));
        Assert.Equal(ReleaseCompare.Newer, ReleaseVersion.Compare("1.0.0-rc.1", "1.0.0-beta.9"));
        Assert.Equal(ReleaseCompare.Newer, ReleaseVersion.Compare("0.1.3", "0.1.3-beta"));
        Assert.Equal(ReleaseCompare.Newer, ReleaseVersion.Compare("0.1.2-beta2", "0.1.2-beta"));
        Assert.Equal(ReleaseCompare.Same, ReleaseVersion.Compare("v1.2.3", "1.2.3"));
        Assert.Equal(ReleaseCompare.Same, ReleaseVersion.Compare("1.2", "1.2.0"));
        Assert.Equal(ReleaseCompare.Same, ReleaseVersion.Compare("1.0.0+sha", "1.0.0"));
        Assert.Equal(ReleaseCompare.Older, ReleaseVersion.Compare("0.0.1", "1.0.0"));
        Assert.Equal(ReleaseCompare.Unknown, ReleaseVersion.Compare("latest", "1.0.0"));
        Assert.Equal(ReleaseCompare.Unknown, ReleaseVersion.Compare("1.0.0", ""));
    }

    [Fact]
    public void Each_platform_picks_its_release_file()
    {
        Assert.Equal("Sextant-osx-arm64.tar", ReleaseCheck.AssetName(new ReleaseMachine(ReleaseOs.Mac, Architecture.Arm64, null)));
        Assert.Null(ReleaseCheck.AssetName(new ReleaseMachine(ReleaseOs.Mac, Architecture.X64, null)));
        Assert.Equal("Sextant-win-x64.zip", ReleaseCheck.AssetName(new ReleaseMachine(ReleaseOs.Windows, Architecture.X64, null)));
        Assert.Equal("Sextant-win-arm64.zip", ReleaseCheck.AssetName(new ReleaseMachine(ReleaseOs.Windows, Architecture.Arm64, null)));
        Assert.Null(ReleaseCheck.AssetName(new ReleaseMachine(ReleaseOs.Windows, Architecture.X86, null)));
        Assert.Equal(
            "Sextant-linux-x64.deb",
            ReleaseCheck.AssetName(new ReleaseMachine(ReleaseOs.Linux, Architecture.X64, LinuxPackageKind.Deb)));
        Assert.Equal(
            "Sextant-linux-arm64.deb",
            ReleaseCheck.AssetName(new ReleaseMachine(ReleaseOs.Linux, Architecture.Arm64, LinuxPackageKind.Deb)));
        Assert.Equal(
            "Sextant-linux-x64.rpm",
            ReleaseCheck.AssetName(new ReleaseMachine(ReleaseOs.Linux, Architecture.X64, LinuxPackageKind.Rpm)));
        Assert.Equal(
            "Sextant-linux-arm64.rpm",
            ReleaseCheck.AssetName(new ReleaseMachine(ReleaseOs.Linux, Architecture.Arm64, LinuxPackageKind.Rpm)));
        Assert.Null(ReleaseCheck.AssetName(new ReleaseMachine(ReleaseOs.Linux, Architecture.X64, null)));
    }

    [Fact]
    public void Linux_package_follows_os_release()
    {
        Assert.Equal(LinuxPackageKind.Deb, ReleaseCheck.PackageFromOsRelease("ID=ubuntu\nID_LIKE=debian\n"));
        Assert.Equal(LinuxPackageKind.Deb, ReleaseCheck.PackageFromOsRelease("ID=pop\nID_LIKE=\"ubuntu debian\"\n"));
        Assert.Equal(LinuxPackageKind.Rpm, ReleaseCheck.PackageFromOsRelease("ID=\"fedora\"\n"));
        Assert.Equal(LinuxPackageKind.Rpm, ReleaseCheck.PackageFromOsRelease("ID=opensuse-tumbleweed\nID_LIKE=\"opensuse suse\"\n"));
        Assert.Equal(LinuxPackageKind.Rpm, ReleaseCheck.PackageFromOsRelease("ID=almalinux\nID_LIKE=\"rhel centos fedora\"\n"));
        Assert.Null(ReleaseCheck.PackageFromOsRelease("ID=arch\nID_LIKE=archlinux\n"));
        Assert.Null(ReleaseCheck.PackageFromOsRelease(null));
    }

    [Fact]
    public void A_release_link_stays_on_the_github_release()
    {
        Assert.True(ReleaseCheck.IsReleaseUrl("https://github.com/SneWs/Sextant/releases"));
        Assert.True(ReleaseCheck.IsReleaseUrl("https://github.com/SneWs/Sextant/releases/tag/v0.1.3"));
        Assert.True(ReleaseCheck.IsReleaseUrl("https://github.com/SneWs/Sextant/releases/download/v0.1.3/Sextant-osx-arm64.tar"));
        Assert.False(ReleaseCheck.IsReleaseUrl("http://github.com/SneWs/Sextant/releases"));
        Assert.False(ReleaseCheck.IsReleaseUrl("https://github.com/other/Sextant/releases/latest"));
        Assert.False(ReleaseCheck.IsReleaseUrl("https://example.com/SneWs/Sextant/releases"));
        Assert.False(ReleaseCheck.IsReleaseUrl("https://user:pass@github.com/SneWs/Sextant/releases"));
    }

    [Fact]
    public void The_latest_release_json_keeps_the_download_for_each_asset()
    {
        const string json = """
            {
              "tag_name": "v0.2.0",
              "html_url": "https://github.com/SneWs/Sextant/releases/tag/v0.2.0",
              "assets": [
                {
                  "name": "Sextant-osx-arm64.tar",
                  "browser_download_url": "https://github.com/SneWs/Sextant/releases/download/v0.2.0/Sextant-osx-arm64.tar"
                },
                {
                  "name": "notes.txt",
                  "browser_download_url": "https://example.com/notes.txt"
                }
              ]
            }
            """;

        var release = ReleaseCheck.Parse(json);
        Assert.NotNull(release);
        Assert.Equal("0.2.0", release.Version);
        Assert.Equal("https://github.com/SneWs/Sextant/releases/tag/v0.2.0", release.PageUrl);
        var asset = Assert.Single(release.Assets);
        Assert.Equal("Sextant-osx-arm64.tar", asset.Name);
    }

    [Fact]
    public void A_newer_release_offers_this_computers_download()
    {
        var release = Full("2.0.0");
        var mac = ReleaseCheck.Create(release, "1.0.0", new ReleaseMachine(ReleaseOs.Mac, Architecture.Arm64, null));
        Assert.NotNull(mac);
        Assert.Equal("Download", mac.Confirm);
        Assert.EndsWith("Sextant-osx-arm64.tar", mac.Url, StringComparison.Ordinal);
        Assert.Contains("Sextant.app", mac.Message, StringComparison.Ordinal);

        var intel = ReleaseCheck.Create(release, "1.0.0", new ReleaseMachine(ReleaseOs.Mac, Architecture.X64, null));
        Assert.NotNull(intel);
        Assert.Equal("Open releases", intel.Confirm);
        Assert.Equal(release.PageUrl, intel.Url);

        var windows = ReleaseCheck.Create(release, "1.0.0", new ReleaseMachine(ReleaseOs.Windows, Architecture.Arm64, null));
        Assert.NotNull(windows);
        Assert.EndsWith("Sextant-win-arm64.zip", windows.Url, StringComparison.Ordinal);
        Assert.Contains("replace this folder", windows.Message, StringComparison.Ordinal);

        var deb = ReleaseCheck.Create(release, "1.0.0", new ReleaseMachine(ReleaseOs.Linux, Architecture.X64, LinuxPackageKind.Deb));
        Assert.NotNull(deb);
        Assert.Contains("apt", deb.Message, StringComparison.Ordinal);

        var rpm = ReleaseCheck.Create(release, "1.0.0", new ReleaseMachine(ReleaseOs.Linux, Architecture.Arm64, LinuxPackageKind.Rpm));
        Assert.NotNull(rpm);
        Assert.Contains("dnf", rpm.Message, StringComparison.Ordinal);

        Assert.Null(ReleaseCheck.Create(release, "2.0.0", new ReleaseMachine(ReleaseOs.Mac, Architecture.Arm64, null)));
        Assert.Null(ReleaseCheck.Create(release, "3.0.0", new ReleaseMachine(ReleaseOs.Mac, Architecture.Arm64, null)));

        var windowsOnly = new PublishedRelease(
            "v2.0.0",
            "2.0.0",
            ReleaseCheck.ReleasesPage,
            [new ReleaseAsset("Sextant-win-x64.zip", "https://github.com/SneWs/Sextant/releases/download/v2.0.0/Sextant-win-x64.zip")]);
        var page = ReleaseCheck.Create(windowsOnly, "1.0.0", new ReleaseMachine(ReleaseOs.Mac, Architecture.Arm64, null));
        Assert.NotNull(page);
        Assert.Equal("Open releases", page.Confirm);
        Assert.Equal(ReleaseCheck.ReleasesPage, page.Url);
    }

    [Fact]
    public async Task The_release_client_stays_offline_during_tests()
    {
        Assert.True(ReleaseCheck.IsTestProcess);
        var catalog = new GitHubReleaseCatalog();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.LatestAsync(CancellationToken.None));
        Assert.Contains("tests", exception.Message, StringComparison.Ordinal);
    }

    private static PublishedRelease Full(string version)
    {
        var tag = "v" + version;
        string Url(string name) => "https://github.com/SneWs/Sextant/releases/download/" + tag + "/" + name;
        return new PublishedRelease(
            tag,
            version,
            "https://github.com/SneWs/Sextant/releases/tag/" + tag,
            [
                new ReleaseAsset("Sextant-osx-arm64.tar", Url("Sextant-osx-arm64.tar")),
                new ReleaseAsset("Sextant-win-x64.zip", Url("Sextant-win-x64.zip")),
                new ReleaseAsset("Sextant-win-arm64.zip", Url("Sextant-win-arm64.zip")),
                new ReleaseAsset("Sextant-linux-x64.deb", Url("Sextant-linux-x64.deb")),
                new ReleaseAsset("Sextant-linux-arm64.deb", Url("Sextant-linux-arm64.deb")),
                new ReleaseAsset("Sextant-linux-x64.rpm", Url("Sextant-linux-x64.rpm")),
                new ReleaseAsset("Sextant-linux-arm64.rpm", Url("Sextant-linux-arm64.rpm")),
            ]);
    }
}
