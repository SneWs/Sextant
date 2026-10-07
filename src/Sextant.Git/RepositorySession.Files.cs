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

    public Task RemoveRepositoryFileAsync(string path, CancellationToken cancellationToken) =>
        ChangeWorktreeAsync(async token =>
        {
            var full = RepoPath.CombineUnder(_toplevel, path);
            if (full is null)
                throw new RepositoryActionException("That file path is outside the repository or cannot be removed safely.");
            if (Directory.Exists(full))
                throw new RepositoryActionException("That path is a directory. Remove individual files instead.");

            var index = Checked(await ExecuteAsync(GitCommands.RepositoryFileIndex(_toplevel, path), null, token).ConfigureAwait(false));
            var entries = Encoding.UTF8.GetString(index.Stdout).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in entries)
            {
                var tab = entry.IndexOf('\t');
                if (tab < 0 || entry[(tab + 1)..] != path)
                    throw new RepositoryActionException("That path is not an individual repository file.");
                if (entry.StartsWith("160000 ", StringComparison.Ordinal))
                    throw new RepositoryActionException("Submodules cannot be removed with Remove file.");
            }

            if (entries.Length > 0)
            {
                Checked(await ExecuteAsync(GitCommands.RemoveRepositoryFile(_toplevel, path), null, token).ConfigureAwait(false));
                return;
            }

            var untracked = Checked(await ExecuteAsync(GitCommands.RepositoryFileUntracked(_toplevel, path), null, token).ConfigureAwait(false));
            var paths = Encoding.UTF8.GetString(untracked.Stdout).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (paths.Length != 1 || paths[0] != path)
                throw new RepositoryActionException("That file is no longer in the repository file list. Reactivate the Files tab before removing it.");
            Checked(await ExecuteAsync(GitCommands.RemoveUntrackedRepositoryFile(_toplevel, path), null, token).ConfigureAwait(false));
        }, cancellationToken);
}
