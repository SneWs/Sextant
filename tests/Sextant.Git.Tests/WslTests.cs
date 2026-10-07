using System.Text;

namespace Sextant.Git.Tests;

public class WslTests
{
    [Fact]
    public void List_parses_the_verbose_table_and_keeps_user_distributions()
    {
        var text = """
              NAME                   STATE           VERSION
            * Ubuntu                 Running         2
              docker-desktop         Stopped         2
              Debian                 Stopped         1
              Ubuntu 22.04           Running         2
            """;
        var parsed = WslList.Parse(text);
        Assert.Equal(4, parsed.Count);
        Assert.Equal("Ubuntu", parsed[0].Name);
        Assert.True(parsed[0].IsDefault);
        Assert.Equal(2, parsed[0].Version);
        Assert.Equal("Ubuntu 22.04", parsed[3].Name);

        var users = WslList.UserDistros(parsed);
        Assert.Equal(["Ubuntu", "Ubuntu 22.04"], users.Select(distro => distro.Name));
    }

    [Fact]
    public void Infrastructure_distributions_are_offered_when_they_are_the_only_wsl2_install()
    {
        var users = WslList.UserDistros(
        [
            new WslDistro("docker-desktop", "Running", true, 2),
            new WslDistro("Alpine", "Stopped", false, 1),
        ]);
        Assert.Equal(["docker-desktop"], users.Select(distro => distro.Name));
    }

    [Fact]
    public void Utf16_list_output_decodes_before_parsing()
    {
        var text = "  NAME    STATE    VERSION\n* Ubuntu    Running    2\n";
        var bytes = new byte[text.Length * 2];
        Encoding.Unicode.GetBytes(text, bytes);
        var decoded = WslList.Decode(bytes);
        var parsed = WslList.Parse(decoded);
        Assert.Single(parsed);
        Assert.Equal("Ubuntu", parsed[0].Name);
        Assert.Equal(2, parsed[0].Version);
    }

    [Fact]
    public void Windows_and_linux_paths_round_trip_for_one_distribution()
    {
        var linux = "/home/user/repo";
        var windows = WslPath.ToWindows("Ubuntu", linux);
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\user\repo", windows);
        Assert.True(WslPath.TryParseUnc(@"\\wsl$\Ubuntu\home\user\repo", out var distribution, out var parsed));
        Assert.Equal("Ubuntu", distribution);
        Assert.Equal(linux, parsed);
        Assert.Equal(windows, WslPath.CanonicalWindows(@"\\wsl$\Ubuntu\home\user\repo"));
        Assert.Equal(linux, WslPath.ToLinux("Ubuntu", windows));
        Assert.Equal("/mnt/c/Users/me/Temp/msg", WslPath.ToLinux("Ubuntu", @"C:\Users\me\Temp\msg"));
        Assert.Equal(@"C:\Users\me\Temp\msg", WslPath.ToWindows("Ubuntu", "/mnt/c/Users/me/Temp/msg"));
    }

    [Fact]
    public void Launch_runs_the_distribution_git_and_translates_paths()
    {
        var request = new GitRequest
        {
            Executable = @"C:\Program Files\Git\cmd\git.exe",
            Arguments = ["-C", @"\\wsl.localhost\Ubuntu\home\user\repo", "commit", "-F", @"C:\Users\me\Temp\msg"],
            WorkingDirectory = @"\\wsl.localhost\Ubuntu\home\user\repo",
            Environment = new Dictionary<string, string>
            {
                ["GIT_EDITOR"] = "true",
                ["GIT_SEQUENCE_EDITOR"] = "\"C:/Users/me/Temp/seq.sh\"",
                ["PATH"] = @"C:\Windows",
            },
            Wsl = new WslGit("Ubuntu", "/usr/bin/git", @"C:\Windows\System32\wsl.exe"),
        };

        var launched = WslLaunch.Prepare(request);
        Assert.Equal(@"C:\Windows\System32\wsl.exe", launched.Executable);
        Assert.Null(launched.WorkingDirectory);
        Assert.Equal(
            [
                "-d", "Ubuntu",
                "--cd", "/home/user/repo",
                "-e", "/usr/bin/env",
                "GIT_TERMINAL_PROMPT=0",
                "GIT_EDITOR=true",
                "GIT_SEQUENCE_EDITOR=\"/mnt/c/Users/me/Temp/seq.sh\"",
                "/usr/bin/git",
                "-C", "/home/user/repo",
                "commit", "-F", "/mnt/c/Users/me/Temp/msg",
            ],
            launched.Arguments);
        Assert.DoesNotContain(launched.Arguments, argument => argument.StartsWith("PATH=", StringComparison.Ordinal));
    }

    [Fact]
    public void Launcher_note_reads_utf16_wsl_failures_and_ignores_git_output()
    {
        var message = "There is no distribution with the supplied name.\r\nError code: Wsl/Service/WSL_E_DISTRO_NOT_FOUND\r\n";
        var utf16 = Encoding.Unicode.GetBytes(message);
        Assert.Contains("WSL_E_DISTRO_NOT_FOUND", WslLaunch.LauncherNote(utf16), StringComparison.Ordinal);
        Assert.Null(WslLaunch.LauncherNote("A\0B"u8.ToArray()));
        Assert.Null(WslLaunch.LauncherNote("not a git repository"u8.ToArray()));
    }

    [Fact]
    public void Rebase_editor_uses_the_shell_for_a_translated_script()
    {
        var git = Path.Combine(Path.GetTempPath(), "sextant-wsl-rebase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(git);
        try
        {
            var command = RebaseEditor.Create(
                git,
                [new RebaseStep("abc", "subject", RebaseVerb.Reword, "new message")],
                path => "/mnt/c/tmp/" + Path.GetFileName(path));
            Assert.StartsWith("sh \"", command, StringComparison.Ordinal);
            Assert.Contains("/mnt/c/tmp/seq.sh", command, StringComparison.Ordinal);
            var directory = File.ReadAllText(Path.Combine(git, RebaseEditor.PointerName)).Trim();
            var todo = File.ReadAllText(Path.Combine(directory, "todo"));
            Assert.Contains("exec git commit --amend -F '/mnt/c/tmp/0'", todo, StringComparison.Ordinal);
        }
        finally
        {
            RebaseEditor.Cleanup(git);
            Directory.Delete(git, recursive: true);
        }
    }
}
