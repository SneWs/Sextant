using System.Text;
using Sextant.Git.Parsing;
using Sextant.Git.Repo;

namespace Sextant.Git.Tests;

public class DiffStatTests
{
    [Fact]
    public void Numstat_sums_text_lines_and_counts_a_binary_file()
    {
        var stdout = Encoding.UTF8.GetBytes("3\t1\ta.txt\0-\t-\tbin.dat\0");
        Assert.Equal(new DiffStat(2, 3, 1), NumStatParser.Parse(stdout));
        Assert.Equal("18 files changed", DiffStatText.Files(18));
        Assert.Equal("1 file changed", DiffStatText.Files(1));
        Assert.Equal("0 files changed", DiffStatText.Files(0));
        Assert.Equal("-0", DiffStatText.Removed(0));
        Assert.Equal("+937", DiffStatText.Added(937));
    }

    [Fact]
    public async Task Commit_stats_follow_the_first_parent_and_a_root_commit()
    {
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("root");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "feature");
        repo.WriteFile("f.txt", "hello\n");
        repo.CommitAll("feature");
        repo.Run("switch", trunk);
        repo.Run("merge", "--no-ff", "--no-edit", "feature");

        await using var session = await RepositorySession.OpenAsync(
            new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
        var commits = session.Snapshot().Commits;
        var root = commits.Single(row => row.Commit.Subject == "root").Commit;
        var merge = commits.Single(row => row.Commit.Subject.StartsWith("Merge branch", StringComparison.Ordinal)).Commit;
        Assert.Equal(new DiffStat(1, 1, 0), await session.DiffStatAsync(null, root.Sha, CancellationToken.None));
        Assert.Equal(new DiffStat(1, 1, 0), await session.DiffStatAsync(merge.Parents[0], merge.Sha, CancellationToken.None));
    }
}
