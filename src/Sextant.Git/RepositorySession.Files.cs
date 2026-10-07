using System.Text;
using Sextant.Git.Parsing;

namespace Sextant.Git;

public sealed partial class RepositorySession
{
    public Task<IReadOnlyList<RepositoryFile>> RepositoryFilesAsync(CancellationToken cancellationToken) =>
        RunAsync(ct => _scheduler.ReadAsync(async token =>
        {
            var output = Checked(await ExecuteAsync(GitCommands.RepositoryFiles(_toplevel), null, token).ConfigureAwait(false));
            var paths = Encoding.UTF8.GetString(output.Stdout).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            var tracked = await ReadLfsAsync(paths, null, token).ConfigureAwait(false);
            return (IReadOnlyList<RepositoryFile>)paths.Select(path => new RepositoryFile(path, tracked.Contains(path))).ToArray();
        }, ct), cancellationToken);

    public Task<IReadOnlyList<LfsLock>> LfsLocksAsync(CancellationToken cancellationToken) =>
        RunAsync(ct => _scheduler.ReadAsync(async token =>
        {
            // The JSON mode in git-lfs can swallow server errors. Populate the cache with
            // the ordinary command first, which reports failures, then read that snapshot as JSON.
            Checked(await ExecuteAsync(GitCommands.LfsLocks(_toplevel), null, token).ConfigureAwait(false));
            var output = Checked(await ExecuteAsync(GitCommands.LfsLocksCached(_toplevel), null, token).ConfigureAwait(false));
            return LfsLockParser.Parse(output.Stdout);
        }, ct), cancellationToken);

    public Task LockLfsFileAsync(string path, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.LfsLock(_toplevel, path), null, cancellationToken);

    public Task UnlockLfsFileAsync(string path, bool force, CancellationToken cancellationToken) =>
        MutateAsync(GitCommands.LfsUnlock(_toplevel, path, force), null, cancellationToken);
}
