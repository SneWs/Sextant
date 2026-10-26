using Sextant.Git.Models;
using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class LineStageTests
{
    [Fact]
    public async Task Staging_one_added_line_leaves_the_other_unstaged()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "one\nalpha\nbeta\n");
        await using var session = await Open(repo);
        var diff = await session.WorkingDiffAsync("a.txt", staged: false, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(diff);
        Assert.Single(diff.Hunks);
        var beta = diff.Hunks[0].Lines.ToList().FindIndex(line => line.Kind == DiffLineKind.Added && line.Text == "beta");
        Assert.True(beta >= 0);
        await session.ApplyLineAsync(diff.RawPatch, 0, beta, reverse: false, CancellationToken.None);
        var staged = repo.RunCapture("diff", "--cached", "--", "a.txt");
        Assert.Contains("beta", staged, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha", staged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Staging_two_added_lines_leaves_the_other_unstaged()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "one\nalpha\nbeta\ngamma\n");
        await using var session = await Open(repo);
        var diff = await session.WorkingDiffAsync("a.txt", staged: false, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(diff);
        var hunk = Assert.Single(diff.Hunks);
        var alpha = IndexOf(hunk, "alpha");
        var beta = IndexOf(hunk, "beta");
        await session.ApplyLinesAsync(diff.RawPatch, 0, [alpha, beta], reverse: false, CancellationToken.None);
        Assert.Equal("one\nalpha\nbeta\n", Text(repo.RunCapture("show", ":a.txt")));
        Assert.Equal("one\nalpha\nbeta\ngamma\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")));
        var unstaged = repo.RunCapture("diff", "--", "a.txt");
        Assert.Contains("+gamma", unstaged, StringComparison.Ordinal);
        Assert.DoesNotContain("+alpha", unstaged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unstaging_two_added_lines_leaves_the_other_staged()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "one\nalpha\nbeta\ngamma\n");
        repo.Run("add", "a.txt");
        await using var session = await Open(repo);
        var diff = await session.WorkingDiffAsync("a.txt", staged: true, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(diff);
        var hunk = Assert.Single(diff.Hunks);
        await session.ApplyLinesAsync(diff.RawPatch, 0, [IndexOf(hunk, "alpha"), IndexOf(hunk, "beta")], reverse: true, CancellationToken.None);
        Assert.Equal("one\ngamma\n", Text(repo.RunCapture("show", ":a.txt")));
        Assert.Equal("one\nalpha\nbeta\ngamma\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")));
    }

    [Fact]
    public async Task Unstaging_the_text_line_leaves_the_blank_line_staged()
    {
        using var repo = StagedReadme();
        await using var session = await Open(repo);
        var diff = await session.WorkingDiffAsync("README.md", staged: true, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(diff);
        var hunk = Assert.Single(diff.Hunks);
        var blank = IndexOf(hunk, "");
        var text = IndexOf(hunk, "Adding more lines...!");
        Assert.True(blank < text);
        await session.ApplyLinesAsync(diff.RawPatch, 0, [text], reverse: true, CancellationToken.None);
        Assert.Equal(ReadmeHead + "\n", Text(repo.RunCapture("show", ":README.md")));
        Assert.Equal(ReadmeWork, Text(File.ReadAllText(Path.Combine(repo.Directory, "README.md"))));
        var staged = repo.RunCapture("diff", "--cached", "--", "README.md");
        Assert.DoesNotContain("Adding more lines", staged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unstaging_the_blank_line_leaves_the_text_line_staged()
    {
        using var repo = StagedReadme();
        await using var session = await Open(repo);
        var diff = await session.WorkingDiffAsync("README.md", staged: true, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(diff);
        var blank = IndexOf(Assert.Single(diff.Hunks), "");
        await session.ApplyLinesAsync(diff.RawPatch, 0, [blank], reverse: true, CancellationToken.None);
        Assert.Equal(ReadmeHead + "Adding more lines...!\n", Text(repo.RunCapture("show", ":README.md")));
        Assert.Equal(ReadmeWork, Text(File.ReadAllText(Path.Combine(repo.Directory, "README.md"))));
        Assert.Contains("+Adding more lines...!", repo.RunCapture("diff", "--cached", "--", "README.md"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unstaging_one_later_added_line_leaves_the_earlier_ones_staged()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        repo.WriteFile("a.txt", "one\nalpha\nbeta\ngamma\n");
        repo.Run("add", "a.txt");
        await using var session = await Open(repo);
        var diff = await session.WorkingDiffAsync("a.txt", staged: true, untracked: false, allowLarge: true, CancellationToken.None);
        Assert.NotNull(diff);
        var gamma = IndexOf(Assert.Single(diff.Hunks), "gamma");
        await session.ApplyLineAsync(diff.RawPatch, 0, gamma, reverse: true, CancellationToken.None);
        Assert.Equal("one\nalpha\nbeta\n", Text(repo.RunCapture("show", ":a.txt")));
        Assert.Equal("one\nalpha\nbeta\ngamma\n", File.ReadAllText(Path.Combine(repo.Directory, "a.txt")));
    }

    private const string ReadmeHead = "# This is a README ----------\nWith some more lines\nand more lines\n";

    private const string ReadmeWork = ReadmeHead + "\nAdding more lines...!\n";

    private static TempRepo StagedReadme()
    {
        var repo = new TempRepo();
        repo.WriteFile("README.md", ReadmeHead);
        repo.CommitAll("first");
        repo.WriteFile("README.md", ReadmeWork);
        repo.Run("add", "README.md");
        return repo;
    }

    private static int IndexOf(DiffHunk hunk, string text)
    {
        var index = hunk.Lines.ToList().FindIndex(line => line.Kind == DiffLineKind.Added && line.Text == text);
        Assert.True(index >= 0, text);
        return index;
    }

    private static string Text(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
