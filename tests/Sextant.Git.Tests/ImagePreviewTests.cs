using System.Text;

namespace Sextant.Git.Tests;

public class ImagePreviewTests
{
    private const string PngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Image_preview_loads_both_versions()
    {
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", "false");
        var first = Convert.FromBase64String(PngBase64);
        var second = (byte[])first.Clone();
        second[^1] ^= 0x5A;
        var path = Path.Combine(repo.Directory, "pic.png");
        File.WriteAllBytes(path, first);
        repo.CommitAll("image");
        var parent = repo.RunCapture("rev-parse", "HEAD").Trim();
        File.WriteAllBytes(path, second);

        await using var session = await Open(repo);
        var worktree = await session.PreviewImageAsync(
            new ImageRequest("pic.png", "", null, false, true),
            CancellationToken.None);
        Assert.NotNull(worktree);
        Assert.Equal(first, worktree.Before);
        Assert.Equal(second, worktree.After);
        Assert.Equal("", worktree.BeforeNotice);
        Assert.Equal("", worktree.AfterNotice);

        repo.CommitAll("image changed");
        var head = repo.RunCapture("rev-parse", "HEAD").Trim();
        var committed = await session.PreviewImageAsync(
            new ImageRequest("pic.png", parent, head, false, false),
            CancellationToken.None);
        Assert.NotNull(committed);
        Assert.Equal(first, committed.Before);
        Assert.Equal(second, committed.After);
    }

    [Fact]
    public async Task Image_preview_loads_svg_and_tiff()
    {
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", "false");
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"><rect width=\"8\" height=\"8\" fill=\"#00ff00\"/></svg>\n";
        repo.WriteFile("mark.svg", svg);
        var tiff = new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00 };
        File.WriteAllBytes(Path.Combine(repo.Directory, "scan.tiff"), tiff);
        repo.CommitAll("pictures");
        var head = repo.RunCapture("rev-parse", "HEAD").Trim();

        await using var session = await Open(repo);
        var svgPreview = await session.PreviewImageAsync(
            new ImageRequest("mark.svg", null, head, false, false),
            CancellationToken.None);
        Assert.NotNull(svgPreview);
        Assert.Null(svgPreview.Before);
        Assert.Contains("fill=\"#00ff00\"", Encoding.UTF8.GetString(svgPreview.After!), StringComparison.Ordinal);

        var tiffPreview = await session.PreviewImageAsync(
            new ImageRequest("scan.tiff", null, head, false, false),
            CancellationToken.None);
        Assert.NotNull(tiffPreview);
        Assert.Equal(tiff, tiffPreview.After);
    }

    [Fact]
    public async Task Image_pointer_over_cap_does_not_smudge()
    {
        using var repo = new TempRepo();
        repo.Run("config", "core.autocrlf", "false");
        repo.WriteFile(".gitattributes", "*.png filter=lfs diff=lfs merge=lfs -text\n");
        var huge = HistoryLimits.MaxPreviewBytes + 1;
        repo.WriteFile("pic.png", Pointer('c', huge));
        repo.CommitAll("huge pointer");
        var head = repo.RunCapture("rev-parse", "HEAD").Trim();
        var file = Path.Combine(repo.Directory, "pic.png");
        var onDisk = File.ReadAllBytes(file);

        await using var session = await Open(repo);
        var before = session.Snapshot().Commands.Count;
        var preview = await session.PreviewImageAsync(
            new ImageRequest("pic.png", head, null, false, true),
            CancellationToken.None);
        Assert.NotNull(preview);
        Assert.Null(preview.Before);
        Assert.Null(preview.After);
        Assert.Contains("8 MB", preview.BeforeNotice, StringComparison.Ordinal);
        Assert.Contains("8 MB", preview.AfterNotice, StringComparison.Ordinal);
        var added = session.Snapshot().Commands.Skip(before);
        Assert.DoesNotContain(added, command => command.Arguments.Contains("--filters"));
        Assert.DoesNotContain(added, command => command.Arguments.Contains("lfs"));
        Assert.Equal(onDisk, File.ReadAllBytes(file));
    }

    private static string Pointer(char oid, long size) =>
        "version https://git-lfs.github.com/spec/v1\noid sha256:" + new string(oid, 64) + "\nsize " + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";

    private static Task<RepositorySession> Open(TempRepo repo) =>
        RepositorySession.OpenAsync(new GitProcessRunner(), repo.Git, repo.Directory, CancellationToken.None);
}
