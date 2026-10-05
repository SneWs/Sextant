using Sextant.Git.Parsing;

namespace Sextant.Git.Tests;

public class BlameTests
{
    [Fact]
    public void Blame_parser_reads_porcelain_groups()
    {
        var text = """
            abcdef1234567890abcdef1234567890abcdef12 1 1 1
            author Ada
            author-mail <ada@example.com>
            author-time 1700000000
            author-tz +0000
            committer Ada
            committer-mail <ada@example.com>
            committer-time 1700000000
            committer-tz +0000
            summary First
            filename a.txt
            	hello
            0000000000000000000000000000000000000000 2 2 1
            author Not Committed Yet
            author-mail <not.committed.yet>
            author-time 1700000001
            author-tz +0000
            committer Not Committed Yet
            committer-mail <not.committed.yet>
            committer-time 1700000001
            committer-tz +0000
            summary 
            filename a.txt
            	world
            """;
        var lines = BlameParser.Parse(text);
        Assert.Equal(2, lines.Count);
        Assert.Equal("Ada", lines[0].Author);
        Assert.Equal("hello", lines[0].Text);
        Assert.False(lines[0].Uncommitted);
        Assert.Equal("world", lines[1].Text);
        Assert.True(lines[1].Uncommitted);
        Assert.Equal("Binary file.", BlameParser.Notice("fatal: file a.png is binary\n"));
        Assert.Equal("This file is not in this revision.", BlameParser.Notice("fatal: no such path 'c.txt' in HEAD\n"));
        Assert.Equal("Git could not blame this file.", BlameParser.Notice("  \n"));
    }

    [Fact]
    public async Task Blame_names_the_commit_that_wrote_the_line()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");
        var sha = repo.RunCapture("rev-parse", "HEAD").Trim();
        await using var session = await Open(repo);
        var blame = await session.BlameAsync(sha, "a.txt", allowLarge: true, CancellationToken.None);
        Assert.NotNull(blame);
        Assert.False(blame.IsTooLarge);
        Assert.Contains(blame.Lines, line => line.Text == "one" && line.Sha.StartsWith(sha[..7], StringComparison.Ordinal));

        repo.WriteFile("b.txt", "bee\n");
        repo.CommitAll("second");
        var again = repo.RunCapture("rev-parse", "HEAD").Trim();
        var left = session.ReadBlameAsync(again, "a.txt", allowLarge: true, CancellationToken.None);
        var right = session.ReadBlameAsync(again, "b.txt", allowLarge: true, CancellationToken.None);
        await Task.WhenAll(left, right);
        Assert.Contains((await left).Lines, line => line.Text == "one");
        Assert.Contains((await right).Lines, line => line.Text == "bee");

        repo.WriteFile("c.txt", "new\n");
        var missing = await session.ReadBlameAsync(again, "c.txt", allowLarge: true, CancellationToken.None);
        Assert.Equal("This file is not in this revision.", missing.Error);
        Assert.Empty(missing.Lines);
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
