using Sextant.Git.Parsing;

namespace Sextant.Git.Tests;

public class DiffFormatTests
{
    [Fact]
    public void Rules_match_an_extension_and_ignore_a_blank_row()
    {
        var error = DiffFormatRules.TryCollect(
            [
                ("", "", ""),
                ("JSON", "jq .", "jq -c ."),
                (" .txt ", "cat", ""),
            ],
            out var rules);
        Assert.Null(error);
        Assert.Equal(2, rules.Count);
        Assert.Equal(".json", rules[0].Extension);
        Assert.Equal("jq .", rules[0].Transform);
        Assert.Equal("jq -c .", rules[0].Restore);
        Assert.Equal(".txt", rules[1].Extension);
        Assert.Same(rules[0], DiffFormatRules.Match(rules, "dir/data.JSON"));
        Assert.Null(DiffFormatRules.Match(rules, "dir/data"));
        Assert.Equal("Add a transform or a restore command for .bin.", DiffFormatRules.TryCollect([(".bin", " ", "")], out _));
        Assert.Equal(".json is listed more than once.", DiffFormatRules.TryCollect([("json", "jq .", ""), (".JSON", "jq", "")], out _));
        Assert.Equal("The transform command must be a single line.", DiffFormatRules.TryCollect([(".json", "jq .\n.", "")], out _));
    }

    [Fact]
    public void Command_split_keeps_quotes_and_the_file_token()
    {
        Assert.True(DiffFormatRules.TrySplit("jq .", out var plain, out var error));
        Assert.Null(error);
        Assert.Equal(["jq", "."], plain);
        Assert.True(DiffFormatRules.TrySplit("\"C:\\Program Files\\jq\\jq.exe\" . \"$FILE\"", out var quoted, out _));
        Assert.Equal(["C:\\Program Files\\jq\\jq.exe", ".", "$FILE"], quoted);
        Assert.False(DiffFormatRules.TrySplit("jq \"oops", out _, out var broken));
        Assert.Contains("quote", broken, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rendered_patch_uses_the_repository_path()
    {
        var patch = DiffFormatPatch.Render("data.json", """
            diff --git a/tmp/before b/tmp/after
            --- a/tmp/before
            +++ b/tmp/after
            @@ -1 +1 @@
            -old
            +new
            """, created: false, deleted: false);
        Assert.Contains("diff --git a/data.json b/data.json", patch, StringComparison.Ordinal);
        Assert.Contains("-old", patch, StringComparison.Ordinal);
        Assert.Contains("+new", patch, StringComparison.Ordinal);
        Assert.DoesNotContain("tmp/before", patch, StringComparison.Ordinal);

        var sides = DiffFormatPatch.Sides("""
            diff --git a/old.json b/new.json
            rename from old.json
            rename to new.json
            --- a/old.json
            +++ b/new.json
            """, "new.json");
        Assert.Equal("old.json", sides.BeforePath);
        Assert.Equal("new.json", sides.AfterPath);
        Assert.False(sides.BeforeMissing);
    }

    [Fact]
    public async Task Tool_reads_stdin_and_a_file_token()
    {
        var prefix = await RunToolAsync("IN:", "alpha"u8.ToArray(), useFile: false);
        Assert.Equal("IN:alpha", prefix);
        var fromFile = await RunToolAsync("FILE:", "beta"u8.ToArray(), useFile: true);
        Assert.Equal("FILE:beta", fromFile);
    }

    [Fact]
    public void Settings_round_trip_keeps_file_type_tools()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sextant-fmt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WorkspaceStore(directory);
            store.SaveSettings(new AppSettings
            {
                DiffFormats =
                [
                    new DiffFormatRule { Extension = ".json", Transform = "jq .", Restore = "jq -c ." },
                ],
            });
            var loaded = store.LoadSettings();
            var rule = Assert.Single(loaded.DiffFormats);
            Assert.Equal(".json", rule.Extension);
            Assert.Equal("jq .", rule.Transform);
            Assert.Equal("jq -c .", rule.Restore);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Working_diff_uses_the_transform_and_leaves_other_files_alone()
    {
        using var repo = new TempRepo();
        using var tool = new FormatTool();
        repo.WriteFile("data.json", "{\"a\":1}\n");
        repo.WriteFile("note.txt", "plain\n");
        repo.CommitAll("first");
        repo.WriteFile("data.json", "{\"a\":2}\n");
        repo.WriteFile("note.txt", "changed\n");
        await using var session = await Open(repo);
        session.UseDiffFormats(
        [
            new DiffFormatRule { Extension = ".json", Transform = tool.Command("FORMATTED:"), Restore = tool.Command("RESTORED:") },
        ]);

        var json = await session.WorkingDiffAsync("data.json", staged: false, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(json);
        Assert.Contains("FORMATTED", json.RawPatch, StringComparison.Ordinal);
        Assert.Contains("{\"a\":2}", json.RawPatch, StringComparison.Ordinal);
        Assert.Contains("data.json", json.FormattedPaths);
        Assert.Empty(json.FormatNotes);

        var text = await session.WorkingDiffAsync("note.txt", staged: false, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(text);
        Assert.DoesNotContain("FORMATTED", text.RawPatch, StringComparison.Ordinal);
        Assert.Contains("changed", text.RawPatch, StringComparison.Ordinal);
        Assert.Empty(text.FormattedPaths);
    }

    [Fact]
    public async Task Binary_file_is_diffed_from_the_tool_output()
    {
        using var repo = new TempRepo();
        using var tool = new FormatTool();
        var before = new byte[] { (byte)'a', 0, (byte)'b' };
        var after = new byte[] { (byte)'a', 0, (byte)'c' };
        File.WriteAllBytes(Path.Combine(repo.Directory, "model.bin"), before);
        repo.CommitAll("first");
        File.WriteAllBytes(Path.Combine(repo.Directory, "model.bin"), after);
        await using var session = await Open(repo);
        session.UseDiffFormats([new DiffFormatRule { Extension = ".bin", Transform = tool.Command("DECODED:") }]);

        var diff = await session.WorkingDiffAsync("model.bin", staged: false, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(diff);
        Assert.False(diff.IsBinary);
        Assert.Contains("DECODED:", diff.RawPatch, StringComparison.Ordinal);
        Assert.Contains("model.bin", diff.FormattedPaths);
    }

    [Fact]
    public async Task Failed_tool_keeps_the_original_diff()
    {
        using var repo = new TempRepo();
        repo.WriteFile("data.json", "{\"a\":1}\n");
        repo.CommitAll("first");
        repo.WriteFile("data.json", "{\"a\":2}\n");
        await using var session = await Open(repo);
        session.UseDiffFormats([new DiffFormatRule { Extension = ".json", Transform = "sextant-missing-formatter" }]);

        var diff = await session.WorkingDiffAsync("data.json", staged: false, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(diff);
        Assert.Contains("{\"a\":2}", diff.RawPatch, StringComparison.Ordinal);
        Assert.Empty(diff.FormattedPaths);
        var note = Assert.Single(diff.FormatNotes);
        Assert.Contains("formatter", note.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Saving_a_merge_runs_the_restore_command()
    {
        using var repo = new TempRepo();
        using var tool = new FormatTool();
        StartJsonMerge(repo);
        await using var session = await Open(repo);
        await Assert.ThrowsAsync<GitCommandFailedException>(() => session.MergeAsync("other", CancellationToken.None));
        session.UseDiffFormats(
        [
            new DiffFormatRule
            {
                Extension = ".json",
                Transform = tool.Command("FORMATTED:"),
                Restore = tool.Command("RESTORED:"),
            },
        ]);

        var conflict = await session.ConflictAsync("data.json", allowLarge: true, CancellationToken.None);
        Assert.NotNull(conflict);
        Assert.True(conflict.Formatted);
        Assert.Contains("restore", conflict.FormatNotice, StringComparison.OrdinalIgnoreCase);
        var piece = Assert.Single(conflict.Pieces);
        Assert.Contains("FORMATTED:", piece.Ours, StringComparison.Ordinal);
        Assert.Contains("FORMATTED", piece.Theirs, StringComparison.Ordinal);

        var original = File.ReadAllText(Path.Combine(repo.Directory, "data.json"));
        var refused = await Assert.ThrowsAsync<RepositoryActionException>(() =>
            session.SaveResolutionAsync("data.json", "<<<<<<< still\n", CancellationToken.None));
        Assert.Contains("left unchanged", refused.Message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(Path.Combine(repo.Directory, "data.json")));

        await session.SaveResolutionAsync("data.json", "chosen\n", CancellationToken.None);
        var saved = File.ReadAllText(Path.Combine(repo.Directory, "data.json"));
        Assert.StartsWith("RESTORED:", saved, StringComparison.Ordinal);
        Assert.Contains("chosen", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("<<<<<<<", saved, StringComparison.Ordinal);
        Assert.Contains(session.Snapshot().Entries, entry => entry.Path == "data.json" && entry.Staged);
    }

    private static void StartJsonMerge(TempRepo repo)
    {
        repo.WriteFile("data.json", "{\"a\":0}\n");
        repo.CommitAll("base");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "other");
        repo.WriteFile("data.json", "{\"a\":2}\n");
        repo.CommitAll("other");
        repo.Run("switch", trunk);
        repo.WriteFile("data.json", "{\"a\":1}\n");
        repo.CommitAll("main");
    }

    private static async Task<string> RunToolAsync(string prefix, byte[] input, bool useFile)
    {
        using var tool = new FormatTool();
        var command = useFile ? tool.FileCommand(prefix) : tool.Command(prefix);
        var run = await DiffFormatTool.RunAsync(command, input, null, null, CancellationToken.None, ".json");
        Assert.True(run.Ok, run.Error);
        return System.Text.Encoding.UTF8.GetString(run.Output);
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);

    private sealed class FormatTool : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "sextant-tool-" + Guid.NewGuid().ToString("N"));

        public FormatTool()
        {
            Directory.CreateDirectory(_directory);
            if (OperatingSystem.IsWindows())
            {
                File.WriteAllText(Path.Combine(_directory, "format.ps1"), """
                    $stdin = [Console]::OpenStandardInput()
                    $memory = New-Object System.IO.MemoryStream
                    $stdin.CopyTo($memory)
                    $bytes = $memory.ToArray()
                    $prefix = [System.Text.Encoding]::UTF8.GetBytes($args[0])
                    $stdout = [Console]::OpenStandardOutput()
                    $stdout.Write($prefix, 0, $prefix.Length)
                    if ($bytes.Length -gt 0) { $stdout.Write($bytes, 0, $bytes.Length) }
                    """);
                File.WriteAllText(Path.Combine(_directory, "file.ps1"), """
                    $bytes = [System.IO.File]::ReadAllBytes($args[1])
                    $prefix = [System.Text.Encoding]::UTF8.GetBytes($args[0])
                    $stdout = [Console]::OpenStandardOutput()
                    $stdout.Write($prefix, 0, $prefix.Length)
                    if ($bytes.Length -gt 0) { $stdout.Write($bytes, 0, $bytes.Length) }
                    """);
            }
            else
            {
                var script = Path.Combine(_directory, "format.sh");
                File.WriteAllText(script, """
                    #!/bin/sh
                    printf '%s' "$1"
                    if [ -n "$2" ]; then cat "$2"; else cat; fi
                    """);
                File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        public string Command(string prefix) =>
            OperatingSystem.IsWindows()
                ? "powershell.exe -NoProfile -File " + Quote(Path.Combine(_directory, "format.ps1")) + " " + Quote(prefix)
                : Quote(Path.Combine(_directory, "format.sh")) + " " + Quote(prefix);

        public string FileCommand(string prefix) =>
            OperatingSystem.IsWindows()
                ? "powershell.exe -NoProfile -File " + Quote(Path.Combine(_directory, "file.ps1")) + " " + Quote(prefix) + " \"$FILE\""
                : Quote(Path.Combine(_directory, "format.sh")) + " " + Quote(prefix) + " \"$FILE\"";

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directory))
                    Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
