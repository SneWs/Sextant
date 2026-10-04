using Sextant;

namespace Sextant.Git.Tests;

public class LinuxDesktopEntryTests
{
    [Fact]
    public void Applications_directory_follows_xdg_data_home()
    {
        Assert.Equal(
            Path.Combine("/home/ada", ".local", "share", "applications"),
            LinuxDesktopEntry.ApplicationsDirectory(null, "/home/ada"));
        Assert.Equal(
            Path.Combine("/home/ada", ".local", "share", "applications"),
            LinuxDesktopEntry.ApplicationsDirectory("  ", "/home/ada"));
        Assert.Equal(
            Path.Combine("/data", "applications"),
            LinuxDesktopEntry.ApplicationsDirectory("/data", "/home/ada"));
    }

    [Fact]
    public void Exec_quotes_spaces_and_escapes_desktop_metacharacters()
    {
        Assert.Equal("\"/opt/My Apps/Sextant\"", LinuxDesktopEntry.QuoteExec("/opt/My Apps/Sextant"));
        Assert.Equal("\"/home/a\\$b/Sextant\"", LinuxDesktopEntry.QuoteExec("/home/a$b/Sextant"));
        Assert.Equal("\"/home/a\\`b/Sextant\"", LinuxDesktopEntry.QuoteExec("/home/a`b/Sextant"));
        Assert.Equal("\"/home/a\\\\b/Sextant\"", LinuxDesktopEntry.QuoteExec("/home/a\\b/Sextant"));
        Assert.Equal("\"/home/a\\\"b/Sextant\"", LinuxDesktopEntry.QuoteExec("/home/a\"b/Sextant"));
        Assert.Equal("\"/home/a%%b/Sextant\"", LinuxDesktopEntry.QuoteExec("/home/a%b/Sextant"));
    }

    [Fact]
    public void Launch_path_replaces_exec_tryexec_and_a_relative_icon()
    {
        var template = """
            [Desktop Entry]
            Name=Sextant
            Exec=Sextant
            TryExec=Sextant
            Icon=sextant.png
            Comment=kept

            """;
        var text = LinuxDesktopEntry.WithLaunchPath(template, "/opt/Sextant/Sextant", "/opt/Sextant/sextant.png");
        Assert.Contains("Name=Sextant", text, StringComparison.Ordinal);
        Assert.Contains("Comment=kept", text, StringComparison.Ordinal);
        Assert.Contains("Exec=\"/opt/Sextant/Sextant\"", text, StringComparison.Ordinal);
        Assert.Contains("TryExec=/opt/Sextant/Sextant", text, StringComparison.Ordinal);
        Assert.Contains("Icon=/opt/Sextant/sextant.png", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon=sextant.png", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Shipped_desktop_file_uses_the_launched_executable()
    {
        var template = ShippedTemplate();
        var text = LinuxDesktopEntry.WithLaunchPath(template, "/opt/Sextant/Sextant", "/opt/Sextant/sextant.png");
        Assert.Contains("Name=Sextant", text, StringComparison.Ordinal);
        Assert.Contains("Exec=\"/opt/Sextant/Sextant\"", text, StringComparison.Ordinal);
        Assert.Contains("TryExec=/opt/Sextant/Sextant", text, StringComparison.Ordinal);
        Assert.Contains("Icon=/opt/Sextant/sextant.png", text, StringComparison.Ordinal);
        Assert.Contains("StartupWMClass=Sextant", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Exec=Sextant", text, StringComparison.Ordinal);
    }

    private static string ShippedTemplate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "sextant.desktop");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new InvalidOperationException("src/sextant.desktop was not found above the test output.");
    }

    [Fact]
    public void Install_copies_once_and_leaves_an_existing_entry()
    {
        var root = Path.Combine(Path.GetTempPath(), "sextant-desktop-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "publish");
        var apps = Path.Combine(root, "applications");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, LinuxDesktopEntry.FileName), """
            [Desktop Entry]
            Name=Sextant
            Exec=Sextant
            TryExec=Sextant
            Icon=sextant.png

            """);
        File.WriteAllText(Path.Combine(source, LinuxDesktopEntry.IconFileName), "png");
        var first = Path.Combine(source, "Sextant");
        var second = Path.Combine(source, "other", "Sextant");
        try
        {
            Assert.False(LinuxDesktopEntry.TryInstall(Path.Combine(root, "missing"), first, apps));
            Assert.False(Directory.Exists(apps));
            Assert.True(LinuxDesktopEntry.TryInstall(source, first, apps));
            var installed = File.ReadAllText(Path.Combine(apps, LinuxDesktopEntry.FileName));
            Assert.Contains("Exec=\"" + first + "\"", installed, StringComparison.Ordinal);
            Assert.Contains("Icon=" + Path.Combine(source, LinuxDesktopEntry.IconFileName), installed, StringComparison.Ordinal);
            Assert.False(LinuxDesktopEntry.TryInstall(source, second, apps));
            Assert.Equal(installed, File.ReadAllText(Path.Combine(apps, LinuxDesktopEntry.FileName)));
            Assert.False(LinuxDesktopEntry.TryInstall(source, "/tmp/has\nnewline", apps));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
