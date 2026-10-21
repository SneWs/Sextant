using System.Diagnostics;
using Sextant.Git.AskPass;
using Sextant.Git.Wsl;

namespace Sextant.Git.Tests;

public class WslAskPassTests
{
    [Fact]
    public void Wsl_script_quotes_the_helper_and_does_not_rely_on_linux_environment()
    {
        var script = WslAskPass.Script("/mnt/c/Program Files/Sextant.AskPass.exe", "sextant-askpass-abc");
        Assert.Contains("#!/bin/sh", script, StringComparison.Ordinal);
        Assert.Contains("repo=${SEXTANT_ASKPASS_REPO:-}", script, StringComparison.Ordinal);
        Assert.Contains("exec '/mnt/c/Program Files/Sextant.AskPass.exe' --pipe 'sextant-askpass-abc' --kind \"$kind\" --repo \"$repo\" \"$@\"", script, StringComparison.Ordinal);
        Assert.Equal("'a'\\''b'", WslAskPass.ShQuote("a'b"));
    }

    [Fact]
    public void Unpackaged_askpass_stays_beside_the_app()
    {
        var path = @"C:\Apps\Sextant\Sextant.AskPass.exe";
        Assert.Equal(path, AskPassPackageCopy.Stage(path, Path.Combine(Path.GetTempPath(), "sextant-askpass-unused")));
    }

    [Fact]
    public void Store_askpass_is_copied_out_of_windowsapps_for_wsl()
    {
        var root = Path.Combine(Path.GetTempPath(), "sextant-askpass-" + Guid.NewGuid().ToString("n"));
        var package = Path.Combine(root, "Program Files", "WindowsApps", "20817M.Grenangen.SextantGit_1.0.8.0_x64__nr8zhaqknvty0");
        var cache = Path.Combine(root, "cache");
        try
        {
            Directory.CreateDirectory(package);
            var stale = Path.Combine(cache, "old-package");
            Directory.CreateDirectory(stale);
            File.WriteAllText(Path.Combine(stale, "stale.txt"), "x");
            var executable = Path.Combine(package, "Sextant.AskPass.exe");
            File.WriteAllText(executable, "exe");
            File.WriteAllText(Path.Combine(package, "Sextant.AskPass.dll"), "dll");
            File.WriteAllText(Path.Combine(package, "Sextant.AskPass.runtimeconfig.json"), "{}");
            File.WriteAllText(Path.Combine(package, "hostfxr.dll"), "host");
            File.WriteAllText(Path.Combine(package, "Sextant.exe"), "app");
            File.WriteAllText(Path.Combine(package, "Sextant.AskPass.deps.json"),
                """
                {
                  "targets": {
                    ".NETCoreApp,Version=v10.0/win-x64": {
                      "Sextant.AskPass/1.0.8": {
                        "runtime": { "Sextant.AskPass.dll": {} },
                        "native": { "hostfxr.dll": {} }
                      }
                    }
                  }
                }
                """);

            var staged = AskPassPackageCopy.Stage(executable, cache);
            var stagedDir = Path.GetDirectoryName(staged)!;
            Assert.Equal(Path.Combine(cache, "20817M.Grenangen.SextantGit_1.0.8.0_x64__nr8zhaqknvty0", "Sextant.AskPass.exe"), staged);
            Assert.Equal("host", File.ReadAllText(Path.Combine(stagedDir, "hostfxr.dll")));
            Assert.Equal("{}", File.ReadAllText(Path.Combine(stagedDir, "Sextant.AskPass.runtimeconfig.json")));
            Assert.False(File.Exists(Path.Combine(stagedDir, "Sextant.exe")));
            Assert.False(Directory.Exists(stale));

            File.Delete(Path.Combine(package, "hostfxr.dll"));
            Assert.Equal(staged, AskPassPackageCopy.Stage(executable, cache));
            Assert.Equal("host", File.ReadAllText(Path.Combine(stagedDir, "hostfxr.dll")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Wsl_launch_forwards_ssh_askpass_and_not_path()
    {
        var environment = AskPassEnvironment.ForWslGit("/tmp/sextant-askpass-pipe");
        environment["PATH"] = @"C:\Windows";
        environment[AskPassEnvironment.RepositoryVariable] = "/home/user/repo";
        var request = new GitRequest
        {
            Executable = @"C:\Program Files\Git\cmd\git.exe",
            Arguments = ["fetch"],
            WorkingDirectory = "/home/user/repo",
            Environment = environment,
            Wsl = new WslGit("Ubuntu", "/usr/bin/git", "wsl.exe"),
        };

        var launched = WslLaunch.Prepare(request);
        Assert.Contains("SSH_ASKPASS=/tmp/sextant-askpass-pipe", launched.Arguments);
        Assert.Contains("SSH_ASKPASS_REQUIRE=force", launched.Arguments);
        Assert.Contains("SEXTANT_ASKPASS_REPO=/home/user/repo", launched.Arguments);
        Assert.DoesNotContain(launched.Arguments, argument => argument.StartsWith("PATH=", StringComparison.Ordinal));
        Assert.DoesNotContain(launched.Arguments, argument => argument.StartsWith("GIT_ASKPASS=", StringComparison.Ordinal));
        Assert.DoesNotContain("DISPLAY", string.Join(' ', launched.Arguments), StringComparison.Ordinal);
    }
}
