using System.Text;
using Sextant.Git.Diff;
using Sextant.Git.Models;
using Sextant.Git.Parsing;

namespace Sextant.Git.Repo;

public sealed partial class RepositorySession
{
    private readonly List<DiffFormatRule> _diffFormats = [];

    public void UseDiffFormats(IReadOnlyList<DiffFormatRule>? rules)
    {
        var copy = DiffFormatRules.Normalize(rules);
        lock (_stateLock)
        {
            _diffFormats.Clear();
            _diffFormats.AddRange(copy);
        }
    }

    private List<DiffFormatRule> Formats()
    {
        lock (_stateLock)
            return _diffFormats.ToList();
    }

    private async Task<DiffDocument> ApplyFormatsAsync(
        DiffDocument document,
        string? path,
        string? beforeRevision,
        string? afterRevision,
        bool afterIsWorktree,
        bool ignoreWhitespace,
        bool allowLarge,
        CancellationToken cancellationToken)
    {
        var rules = Formats();
        if (rules.Count == 0 || document.IsTooLarge || string.IsNullOrEmpty(document.RawPatch))
            return document;

        var files = DiffParser.ParseFiles(document.RawPatch);
        if (files.Count == 0)
            return document;

        var formatted = new List<string>();
        var notes = new List<DiffFormatNote>();
        var builder = new StringBuilder();
        var changed = false;
        var count = 0;
        foreach (var file in files)
        {
            var slice = file.Document.RawPatch;
            var filePath = string.IsNullOrEmpty(file.Path) ? path ?? "" : file.Path;
            var sides = DiffFormatPatch.Sides(slice, filePath);
            var beforeRule = DiffFormatRules.Match(rules, sides.BeforeMissing ? "" : sides.BeforePath);
            var afterRule = DiffFormatRules.Match(rules, sides.AfterMissing ? "" : sides.AfterPath);
            var matched = HasTransform(beforeRule) || HasTransform(afterRule);
            if (!matched)
            {
                AppendSlice(builder, slice);
                continue;
            }

            if (count >= DiffFormatRules.MaxFiles)
            {
                notes.Add(new DiffFormatNote(filePath, "This file was not formatted. Open it on its own to run its tool."));
                AppendSlice(builder, slice);
                changed = true;
                continue;
            }

            count++;
            var before = sides.BeforeMissing
                ? new FormatBytes([])
                : await ReadFormatBytesAsync(sides.BeforePath, beforeRevision, worktree: false, cancellationToken).ConfigureAwait(false);
            var after = sides.AfterMissing
                ? new FormatBytes([])
                : await ReadFormatBytesAsync(sides.AfterPath, afterIsWorktree ? null : afterRevision, afterIsWorktree, cancellationToken).ConfigureAwait(false);
            if (before.Problem is not null || after.Problem is not null)
            {
                notes.Add(new DiffFormatNote(filePath, before.Problem ?? after.Problem!));
                AppendSlice(builder, slice);
                changed = true;
                continue;
            }

            var beforeRun = await TransformSideAsync(beforeRule, sides.BeforePath, before.Data ?? [], cancellationToken).ConfigureAwait(false);
            var afterRun = await TransformSideAsync(afterRule, sides.AfterPath, after.Data ?? [], cancellationToken).ConfigureAwait(false);
            if (!beforeRun.Ok || !afterRun.Ok)
            {
                var detail = !beforeRun.Ok ? beforeRun.Error : afterRun.Error;
                notes.Add(new DiffFormatNote(filePath, "The formatter failed, so the original diff is shown. " + detail));
                AppendSlice(builder, slice);
                changed = true;
                continue;
            }

            var patch = await DiffFormattedAsync(
                filePath,
                beforeRun.Output,
                afterRun.Output,
                ignoreWhitespace,
                cancellationToken).ConfigureAwait(false);
            if (patch is null)
            {
                notes.Add(new DiffFormatNote(filePath, "The formatted diff could not be read, so the original diff is shown."));
                AppendSlice(builder, slice);
                changed = true;
                continue;
            }

            AppendSlice(builder, patch);
            formatted.Add(filePath);
            changed = true;
        }

        if (!changed)
            return document;

        var next = FromPatch(builder.ToString(), allowLarge);
        return next with
        {
            LfsFiles = document.LfsFiles,
            FormattedPaths = formatted,
            FormatNotes = notes,
        };
    }

    private async Task<DiffFormatRun> TransformSideAsync(
        DiffFormatRule? rule,
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        if (!HasTransform(rule) || bytes.Length == 0)
            return new DiffFormatRun(true, bytes, "", [], 0, TimeSpan.Zero);
        var run = await DiffFormatTool.RunAsync(
            rule!.Transform,
            bytes,
            _toplevel,
            _executable,
            cancellationToken,
            DiffFormatRules.ExtensionOf(path)).ConfigureAwait(false);
        TrackFormat(run);
        return run;
    }

    private async Task<string?> DiffFormattedAsync(
        string path,
        byte[] before,
        byte[] after,
        bool ignoreWhitespace,
        CancellationToken cancellationToken)
    {
        var created = before.Length == 0 && after.Length > 0;
        var deleted = before.Length > 0 && after.Length == 0;
        if (before.Length == 0 && after.Length == 0)
            return DiffFormatPatch.Render(path, "", created: false, deleted: false);

        var directory = Path.Combine(Path.GetTempPath(), "sextant-format-diff-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var beforePath = before.Length == 0 ? "/dev/null" : Path.Combine(directory, "before");
            var afterPath = after.Length == 0 ? "/dev/null" : Path.Combine(directory, "after");
            if (before.Length > 0)
                await File.WriteAllBytesAsync(beforePath, before, cancellationToken).ConfigureAwait(false);
            if (after.Length > 0)
                await File.WriteAllBytesAsync(afterPath, after, cancellationToken).ConfigureAwait(false);

            var output = await ExecuteAsync(
                GitCommands.DiffNoIndex(_toplevel, beforePath, afterPath, ignoreWhitespace, forceText: true),
                null,
                cancellationToken).ConfigureAwait(false);
            var failed = output.ExitCode != 0
                && (output.ExitCode != 1 || output.Stdout.Length == 0 || output.StandardError.Contains("fatal:", StringComparison.OrdinalIgnoreCase));
            if (failed)
            {
                Track(output);
                return null;
            }

            Track(output);
            return DiffFormatPatch.Render(path, _encoding.GetString(output.Stdout), created, deleted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private async Task<ConflictDocument> FormattedConflictAsync(
        string path,
        byte[] worktree,
        byte[] ours,
        byte[] theirs,
        byte[] baseBytes,
        DiffFormatRule rule,
        CancellationToken cancellationToken)
    {
        if (OverFormatLimit(worktree) || OverFormatLimit(ours) || OverFormatLimit(theirs) || OverFormatLimit(baseBytes))
            throw new RepositoryActionException("This file is larger than 8 MB, so its tool was not run.");

        var oursText = await TransformConflictSideAsync(rule, path, ours, cancellationToken).ConfigureAwait(false);
        var theirsText = await TransformConflictSideAsync(rule, path, theirs, cancellationToken).ConfigureAwait(false);
        var baseText = await TransformConflictSideAsync(rule, path, baseBytes, cancellationToken).ConfigureAwait(false);
        var workText = Encoding.UTF8.GetString(worktree);
        string result;
        if (!ConflictParser.ContainsMarkers(workText))
            result = await TransformConflictSideAsync(rule, path, worktree, cancellationToken).ConfigureAwait(false);
        else
            result = oursText;

        if (ContainsNul(Encoding.UTF8.GetBytes(oursText))
            || ContainsNul(Encoding.UTF8.GetBytes(theirsText))
            || ContainsNul(Encoding.UTF8.GetBytes(result)))
            return ConflictDocument.Binary;

        var piece = new ConflictPiece(true, "", oursText, theirsText, baseText, result);
        var notice = rule.Restore.Length > 0
            ? "Shown through the " + rule.Extension + " tool. Saving runs the restore command."
            : "Shown through the " + rule.Extension + " tool. Saving writes this text as it is.";
        return new ConflictDocument(false, false, true, [piece])
        {
            Formatted = true,
            FormatNotice = notice,
        };
    }

    private async Task<string> TransformConflictSideAsync(
        DiffFormatRule rule,
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        if (bytes.Length == 0)
            return "";
        var run = await DiffFormatTool.RunAsync(
            rule.Transform,
            bytes,
            _toplevel,
            _executable,
            cancellationToken,
            rule.Extension).ConfigureAwait(false);
        TrackFormat(run);
        if (!run.Ok)
            throw new RepositoryActionException("The formatter failed. " + run.Error);
        if (run.Output.Length > HistoryLimits.MaxPreviewBytes)
            throw new RepositoryActionException("The formatter wrote more than 8 MB.");
        return Encoding.UTF8.GetString(run.Output);
    }

    private async Task<byte[]?> RestoreAsync(string path, string text, CancellationToken cancellationToken)
    {
        var rule = DiffFormatRules.Match(Formats(), path);
        if (rule is null || rule.Restore.Length == 0)
            return null;
        if (ConflictParser.ContainsMarkers(text))
        {
            if (rule.Transform.Length > 0)
                throw new RepositoryActionException("Conflict markers are still in the file. The original file was left unchanged. Resolve the markers, then save, so the file can be restored to its original format.");
            return null;
        }

        var run = await DiffFormatTool.RunAsync(
            rule.Restore,
            Encoding.UTF8.GetBytes(text),
            _toplevel,
            _executable,
            cancellationToken,
            rule.Extension).ConfigureAwait(false);
        TrackFormat(run);
        if (!run.Ok)
            throw new RepositoryActionException("Could not restore the file format. " + run.Error);
        return run.Output;
    }

    private async Task<FormatBytes> ReadFormatBytesAsync(
        string path,
        string? revision,
        bool worktree,
        CancellationToken cancellationToken)
    {
        if (worktree)
        {
            var full = RepoPath.CombineUnder(_toplevel, path);
            if (full is null || !File.Exists(full))
                return new FormatBytes([]);
            try
            {
                var length = new FileInfo(full).Length;
                if (length > HistoryLimits.MaxPreviewBytes)
                    return new FormatBytes(null, "This file is larger than 8 MB, so it was not formatted.");
                return new FormatBytes(await File.ReadAllBytesAsync(full, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new FormatBytes(null, "This file could not be read, so it was not formatted.");
            }
        }

        if (revision is null)
            return new FormatBytes([]);

        var spec = GitCommands.ObjectSpec(revision, path);
        var sizeOutput = await ExecuteAsync(GitCommands.CatFileSize(_toplevel, spec), null, cancellationToken).ConfigureAwait(false);
        Track(sizeOutput);
        if (sizeOutput.ExitCode != 0)
            return new FormatBytes([]);
        if (!TrySize(sizeOutput.Stdout, out var size) || size > HistoryLimits.MaxPreviewBytes)
            return new FormatBytes(null, "This file is larger than 8 MB, so it was not formatted.");
        var blob = await ExecuteAsync(GitCommands.CatFileBlob(_toplevel, spec), null, cancellationToken).ConfigureAwait(false);
        Track(blob);
        if (blob.ExitCode != 0)
            return new FormatBytes([]);
        if (blob.Stdout.LongLength > HistoryLimits.MaxPreviewBytes)
            return new FormatBytes(null, "This file is larger than 8 MB, so it was not formatted.");
        return new FormatBytes(blob.Stdout);
    }

    private void TrackFormat(DiffFormatRun run)
    {
        if (run.Display.Count == 0)
            return;
        Track(new GitOutput
        {
            ExitCode = run.ExitCode < 0 ? 1 : run.ExitCode,
            Stdout = [],
            StandardError = run.Ok ? "" : run.Error,
            Progress = null,
            Duration = run.Duration,
            DisplayArguments = run.Display,
        });
    }

    private static bool HasTransform(DiffFormatRule? rule) => rule is { Transform.Length: > 0 };

    private static bool OverFormatLimit(byte[] bytes) => bytes.LongLength > HistoryLimits.MaxPreviewBytes;

    private static void AppendSlice(StringBuilder builder, string slice)
    {
        if (slice.Length == 0)
            return;
        if (builder.Length > 0 && builder[^1] != '\n')
            builder.Append('\n');
        builder.Append(slice);
        if (!slice.EndsWith('\n'))
            builder.Append('\n');
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct FormatBytes(byte[]? Data, string? Problem = null);
}
