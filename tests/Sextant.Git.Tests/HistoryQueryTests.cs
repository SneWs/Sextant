using Sextant.Git.Parsing;

namespace Sextant.Git.Tests;

public class HistoryQueryTests
{
    [Fact]
    public void History_query_reads_branch_author_and_sha()
    {
        var branch = HistoryQueryParser.Parse("branch:\"feature work\" author:Ada fix");
        Assert.Equal("feature work", branch.Revision);
        Assert.Equal("Ada", branch.Author);
        Assert.Equal("fix", branch.Grep);
        Assert.False(branch.MatchSubjectOrAuthor);

        var plain = HistoryQueryParser.Parse("icon fix");
        Assert.True(plain.MatchSubjectOrAuthor);
        Assert.Equal("icon fix", plain.Grep);
        Assert.Equal("icon fix", plain.Author);

        var sha = HistoryQueryParser.Parse("abc1234");
        Assert.True(sha.ShaLookup);
        Assert.Equal("abc1234", sha.Revision);
        Assert.True(HistoryQueryParser.Parse("   ").IsEmpty);

        var fbx = HistoryQueryParser.Parse("file:*.fbx");
        Assert.Equal("*.fbx", fbx.Path);
        Assert.True(fbx.FilePattern);
        Assert.Equal(":(glob,icase)**/*.fbx", fbx.LogPath);
        Assert.Equal("file:*.fbx", fbx.Describe());

        Assert.Equal(":(glob,icase)**/CodeFile*Asset.cs", HistoryQueryParser.Parse("file:CodeFile*Asset.cs").LogPath);
        Assert.Equal(":(glob,icase)**/SomeFile.md", HistoryQueryParser.Parse("file:SomeFile.md").LogPath);
        Assert.Equal(":(glob,icase)**/*.fbx", HistoryQueryParser.Parse("*.fbx").LogPath);
        Assert.Equal("*.fbx", HistoryQueryParser.Parse("*.fbx").Path);
        Assert.True(HistoryQueryParser.Parse("*.fbx").FilePattern);
        Assert.Equal(":(glob,icase)**/Some*File.cs", HistoryQueryParser.Parse("Some*File.cs").LogPath);
        Assert.Equal(":(glob,icase)**/MyFile.cs", HistoryQueryParser.Parse("MyFile.cs").LogPath);
        Assert.Equal("file:MyFile.cs", HistoryQueryParser.Parse("MyFile.cs").Describe());
        var withBranch = HistoryQueryParser.Parse("branch:main *.fbx");
        Assert.Equal("main", withBranch.Revision);
        Assert.Equal("*.fbx", withBranch.Path);
        Assert.Null(withBranch.Grep);
        var words = HistoryQueryParser.Parse("icon fix");
        Assert.Equal("icon fix", words.Grep);
        Assert.Null(words.Path);
        var version = HistoryQueryParser.Parse("1.2.3");
        Assert.Equal("1.2.3", version.Grep);
        Assert.Null(version.Path);
        Assert.Equal(":(icase)docs/SomeFile.md", HistoryQueryParser.Parse("file:docs/SomeFile.md").LogPath);
        Assert.Equal(":(glob,icase)Assets/Models/*.fbx", HistoryQueryParser.Parse("file:Assets\\Models\\*.fbx").LogPath);
        Assert.Equal("My File.md", HistoryQueryParser.Parse("file:\"My File.md\"").Path);

        var mixed = HistoryQueryParser.Parse("branch:main file:*.fbx author:Ada fix");
        Assert.Equal("main", mixed.Revision);
        Assert.Equal("Ada", mixed.Author);
        Assert.Equal("fix", mixed.Grep);
        Assert.Equal("*.fbx", mixed.Path);
        Assert.False(mixed.MatchSubjectOrAuthor);

        var history = HistoryQuery.ForPath("a.txt");
        Assert.False(history.FilePattern);
        Assert.Equal("a.txt", history.LogPath);
        Assert.Equal("File a.txt", history.Describe());
    }
}
