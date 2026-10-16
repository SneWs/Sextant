using System.Text;
using Sextant.Git.Models;
using Sextant.Git.Parsing;

namespace Sextant.Git.Repo;

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

    /// <summary>
    /// Active locks split into ours and theirs. A server that does not answer the locking API fails this call.
    /// A successful answer means force unlock is part of that API.
    /// </summary>
    public Task<LfsLockListing> LfsLocksVerifiableAsync(CancellationToken cancellationToken) =>
        RunAsync(ct => _scheduler.ReadAsync(async token =>
        {
            Checked(await ExecuteAsync(GitCommands.LfsLocksVerify(_toplevel), null, token).ConfigureAwait(false));
            var output = Checked(await ExecuteAsync(GitCommands.LfsLocksVerifyCached(_toplevel), null, token).ConfigureAwait(false));
            return LfsLockParser.ParseVerifiable(output.Stdout);
        }, ct), cancellationToken);

    /// <summary>
    /// True when this repository has a local LFS address, a local LFS filter command,
    /// or a <c>filter=lfs</c> attribute. Paths come from the repository file list.
    /// A failed probe is false, so a lookup that cannot tell does not call Git LFS.
    /// </summary>
    public Task<bool> UsesLfsAsync(IReadOnlyList<string>? paths, CancellationToken cancellationToken) =>
        RunAsync(ct => _scheduler.ReadAsync(token => UsesLfsCoreAsync(paths, token), ct), cancellationToken);

    /// <summary>
    /// Same probe as <see cref="UsesLfsAsync"/>, using only <see cref="ExecuteAsync"/>.
    /// Checkout already holds the write lock, so this must not take the scheduler again.
    /// Pass null to discover attribute files from the index and the worktree.
    /// </summary>
    private async Task<bool> UsesLfsCoreAsync(IReadOnlyList<string>? paths, CancellationToken cancellationToken)
    {
        var config = await ExecuteAsync(GitCommands.LocalConfigList(_toplevel), null, cancellationToken).ConfigureAwait(false);
        Track(config);
        if (config.ExitCode != 0)
            return false;
        if (LfsRepo.ConfigEnables(ConfigParser.Parse(config.Stdout)))
            return true;

        foreach (var path in await AttributeCandidatePathsAsync(paths, cancellationToken).ConfigureAwait(false))
        {
            if (!IsAttributesPath(path))
                continue;
            if (await AttributesPathEnablesAsync(path, cancellationToken).ConfigureAwait(false))
                return true;
        }

        return await InfoAttributesEnableAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> AttributeCandidatePathsAsync(IReadOnlyList<string>? paths, CancellationToken cancellationToken)
    {
        if (paths is not null)
            return paths;
        var output = await ExecuteAsync(GitCommands.RepositoryFiles(_toplevel), null, cancellationToken).ConfigureAwait(false);
        Track(output);
        if (output.ExitCode != 0)
            return [];
        return Encoding.UTF8.GetString(output.Stdout).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private async Task<bool> AttributesPathEnablesAsync(string path, CancellationToken cancellationToken)
    {
        if (TryReadAttributes(RepoPath.CombineUnder(_toplevel, path), out var enables) && enables)
            return true;
        if (path.Contains('\0') || path.Contains('\n') || path.Contains('\r'))
            return false;
        var output = await ExecuteAsync(GitCommands.CatFileIndex(_toplevel, path), null, cancellationToken).ConfigureAwait(false);
        Track(output);
        return output.ExitCode == 0 && LfsRepo.AttributesEnable(Encoding.UTF8.GetString(output.Stdout));
    }

    private async Task<bool> InfoAttributesEnableAsync(CancellationToken cancellationToken)
    {
        var output = await ExecuteAsync(GitCommands.RevParseGitPath(_toplevel, "info/attributes"), null, cancellationToken).ConfigureAwait(false);
        Track(output);
        if (output.ExitCode != 0)
            return false;
        var printed = Encoding.UTF8.GetString(output.Stdout).Trim();
        if (printed.Length == 0 || printed.Contains('\n') || printed.Contains('\0'))
            return false;
        var host = HostPath(printed);
        var full = Path.IsPathRooted(host) ? host : RepoPath.CombineUnder(_toplevel, host);
        return TryReadAttributes(full, out var enables) && enables;
    }

    private static bool IsAttributesPath(string path)
    {
        var gitPath = path.Replace('\\', '/');
        return gitPath.Equals(".gitattributes", StringComparison.Ordinal)
            || gitPath.EndsWith("/.gitattributes", StringComparison.Ordinal);
    }

    private static bool TryReadAttributes(string? full, out bool enables)
    {
        enables = false;
        if (string.IsNullOrEmpty(full) || !File.Exists(full))
            return false;
        try
        {
            enables = LfsRepo.AttributesEnable(File.ReadAllText(full));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

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
