using System.Text;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;

namespace Sextant.ViewModels;

public partial class RepositoryViewModel
{
    [RelayCommand]
    public Task CollectGarbage()
    {
        var progress = Progress();
        return RunAsync("Collecting garbage…", ct => Session.CollectGarbageAsync(false, progress, ct));
    }

    [RelayCommand]
    public Task CollectGarbageAggressively()
    {
        var progress = Progress();
        return RunAsync("Collecting garbage aggressively…", ct => Session.CollectGarbageAsync(true, progress, ct));
    }

    [RelayCommand]
    public Task FetchAndPrune()
    {
        var progress = Progress();
        return RunAsync("Fetching and pruning…", ct => Session.FetchPruneAsync(progress, ct));
    }

    [RelayCommand]
    public Task DeleteMergedBranches()
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            IReadOnlyList<string> names;
            try
            {
                using var pending = new PendingRepositoryWork(this);
                names = await _session.MergedBranchNamesAsync(_lifetime.Token);
            }
            catch (GitCommandFailedException exception)
            {
                Fail(exception.Message);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (names.Count == 0)
            {
                Fail("No merged branches to delete.");
                return;
            }

            if (_session is null)
                return;
            var current = HeadLabel(_session.Snapshot().Branch);
            var message = Listed(
                $"Delete local branches merged into {current}? A branch checked out in a worktree stays.",
                names);
            var ok = await dialogs.ConfirmAsync("Delete merged branches", message, "Delete");
            if (!ok || _session is null)
                return;
            await RunAsync("Deleting merged branches…", ct => _session.DeleteBranchesAsync(names, ct));
        });
    }

    [RelayCommand]
    public Task CleanUntrackedFiles()
    {
        if (_host.Dialogs is not { } dialogs || _session is null || IsBusy)
            return Task.CompletedTask;
        return HoldFocus(async () =>
        {
            string preview;
            try
            {
                using var pending = new PendingRepositoryWork(this);
                preview = await _session.CleanPreviewAsync(_lifetime.Token);
            }
            catch (GitCommandFailedException exception)
            {
                Fail(exception.Message);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var paths = CleanPaths(preview);
            if (paths.Count == 0)
            {
                Fail("No untracked files to clean.");
                return;
            }

            var message = Listed(
                "Delete untracked files and directories? Ignored files stay. This cannot be undone.",
                paths);
            var ok = await dialogs.ConfirmAsync("Clean untracked files", message, "Clean");
            if (!ok || _session is null)
                return;
            var progress = Progress();
            await RunAsync("Cleaning untracked files…", ct => _session.CleanUntrackedAsync(progress, ct));
        });
    }

    private static List<string> CleanPaths(string preview)
    {
        var paths = new List<string>();
        var normalized = preview.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            const string prefix = "Would remove ";
            paths.Add(trimmed.StartsWith(prefix, StringComparison.Ordinal) ? trimmed[prefix.Length..] : trimmed);
        }

        return paths;
    }

    private static string Listed(string intro, IReadOnlyList<string> lines)
    {
        const int shown = 12;
        var builder = new StringBuilder(intro);
        var count = Math.Min(shown, lines.Count);
        for (var index = 0; index < count; index++)
        {
            builder.Append('\n');
            builder.Append(lines[index]);
        }

        if (lines.Count > shown)
        {
            builder.Append('\n');
            builder.Append(lines.Count - shown);
            builder.Append(" more");
        }

        return builder.ToString();
    }
}
