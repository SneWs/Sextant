using Sextant.Git.Parsing;
using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class HistorySearchTests
{
    [Fact]
    public async Task Search_finds_a_subject_and_an_author()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("alpha unique");
        repo.Run("config", "user.name", "Ada Lovelace");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("ordinary message");

        await using var session = await Open(repo);
        await session.SetHistoryAsync(HistoryQueryParser.Parse("alpha unique"), CancellationToken.None);
        var bySubject = session.Snapshot();
        Assert.Contains(bySubject.Commits, row => row.Commit.Subject == "alpha unique");
        Assert.DoesNotContain(bySubject.Commits, row => row.Commit.Subject == "ordinary message");

        await session.SetHistoryAsync(HistoryQueryParser.Parse("Ada"), CancellationToken.None);
        var byAuthor = session.Snapshot();
        Assert.Contains(byAuthor.Commits, row => row.Commit.Subject == "ordinary message");
        Assert.DoesNotContain(byAuthor.Commits, row => row.Commit.Subject == "alpha unique");

        var sha = bySubject.Commits[0].Commit.Sha;
        await session.SetHistoryAsync(HistoryQueryParser.Parse(sha), CancellationToken.None);
        var bySha = session.Snapshot();
        Assert.Single(bySha.Commits);
        Assert.Equal(sha, bySha.Commits[0].Commit.Sha);
        Assert.True(bySha.HistoryEnded);

        await session.SetHistoryAsync(null, CancellationToken.None);
        Assert.Null(session.Snapshot().HistoryLabel);
        Assert.Equal(2, session.Snapshot().Commits.Count);
    }

    [Fact]
    public async Task File_history_lists_commits_that_touch_the_path()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("touch a");
        repo.WriteFile("b.txt", "other\n");
        repo.CommitAll("touch b");
        repo.WriteFile("a.txt", "two\n");
        repo.CommitAll("touch a again");

        await using var session = await Open(repo);
        await session.SetHistoryAsync(HistoryQuery.ForPath("a.txt"), CancellationToken.None);
        var state = session.Snapshot();
        Assert.Equal(2, state.Commits.Count);
        Assert.Contains(state.Commits, row => row.Commit.Subject == "touch a");
        Assert.Contains(state.Commits, row => row.Commit.Subject == "touch a again");
        Assert.Equal("File a.txt", state.HistoryLabel);
        Assert.True(state.HasHistoryQuery);

        await session.SetHistoryAsync(null, CancellationToken.None);
        var cleared = session.Snapshot();
        Assert.False(cleared.HasHistoryQuery);
        Assert.Null(cleared.HistoryLabel);
        Assert.Equal(3, cleared.Commits.Count);
    }

    [Fact]
    public async Task File_pattern_limits_history_to_matching_paths()
    {
        using var repo = new TempRepo();
        repo.WriteFile("Assets/Models/hero.fbx", "mesh\n");
        repo.CommitAll("hero");
        repo.WriteFile("src/Code/CodeFilePlayerAsset.cs", "player\n");
        repo.CommitAll("player");
        repo.WriteFile("src/Code/Other.cs", "other\n");
        repo.CommitAll("other");
        repo.WriteFile("docs/SomeFile.md", "notes\n");
        repo.CommitAll("notes");
        repo.WriteFile("docs/SomeFile.md.bak", "bak\n");
        repo.CommitAll("decoy");
        repo.WriteFile("readme.txt", "read\n");
        repo.CommitAll("readme");
        repo.WriteFile("lib/SomeCoolFile.cs", "cool\n");
        repo.CommitAll("cool");
        repo.WriteFile("nested/dir/MyFile.cs", "mine\n");
        repo.CommitAll("mine");
        repo.WriteFile("other/place/MyFile.cs", "also\n");
        repo.CommitAll("also");
        repo.WriteFile("src/NotSomeFile.cs", "nope\n");
        repo.CommitAll("not some");

        await using var session = await Open(repo);

        async Task<string[]> Subjects(string text)
        {
            await session.SetHistoryAsync(HistoryQueryParser.Parse(text), CancellationToken.None);
            return session.Snapshot().Commits.Select(row => row.Commit.Subject).ToArray();
        }

        Assert.Equal(["hero"], await Subjects("file:*.fbx"));
        Assert.Equal("file:*.fbx", session.Snapshot().HistoryLabel);
        Assert.Equal(["hero"], await Subjects("*.fbx"));
        Assert.Equal("file:*.fbx", session.Snapshot().HistoryLabel);
        Assert.Equal(["hero"], await Subjects("*.FBX"));
        Assert.Equal(["hero"], await Subjects("file:*.FBX"));
        Assert.Equal(["player"], await Subjects("file:CodeFile*Asset.cs"));
        Assert.Equal(["cool"], await Subjects("Some*File.cs"));
        Assert.Equal(["also", "mine"], await Subjects("MyFile.cs"));
        Assert.Equal(["also", "mine"], await Subjects("myfile.cs"));
        Assert.Equal(["not some", "also", "mine", "cool", "other", "player"], await Subjects("*.cs"));
        Assert.Equal(["notes"], await Subjects("file:SomeFile.md"));
        Assert.Equal(["notes"], await Subjects("SomeFile.md"));
        Assert.Equal(["notes"], await Subjects("file:somefile.md"));
        Assert.Equal(["notes"], await Subjects("file:docs/SomeFile.md"));
        Assert.Equal(["notes"], await Subjects("file:docs/*.md"));
        Assert.Equal(["hero"], await Subjects("hero"));
    }

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
