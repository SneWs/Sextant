using Sextant.Git;
using Sextant.Git.Diff;
using Sextant.Git.Models;
using Sextant.ViewModels;

namespace Sextant.Services;

public interface IDialogService
{
    Task<string?> PickFolderAsync(string title, string? startDirectory = null);

    Task<string?> PickGitExecutableAsync();

    Task<bool> ConfirmAsync(string title, string message, string confirm = "OK");

    Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false);

    /// <summary>Masked prompt. Cancel returns null. Confirming an empty field returns an empty string.</summary>
    Task<string?> PromptSecretAsync(string title, string message);

    /// <summary>
    /// Masked prompt with a session checkbox. Cancel returns a null value.
    /// The default implementation has no checkbox and does not remember.
    /// </summary>
    Task<SecretPrompt> PromptSessionSecretAsync(string title, string message, bool rememberChecked = false)
    {
        return PromptWithoutRemember(title, message);

        async Task<SecretPrompt> PromptWithoutRemember(string promptTitle, string promptMessage)
        {
            var value = await PromptSecretAsync(promptTitle, promptMessage).ConfigureAwait(false);
            return new SecretPrompt(value, false);
        }
    }

    Task<string?> SaveFileAsync(string title, string suggestedName);

    /// <summary>
    /// Prompts for the committer name and email, and whether to write the global config or the repository's.
    /// Cancel returns null. The default implementation cancels.
    /// </summary>
    Task<CommitterEdit?> PromptCommitterAsync(string? name, string? email) =>
        Task.FromResult<CommitterEdit?>(null);

    Task<string?> PickFileAsync(string title, string typeName, IReadOnlyList<string> patterns);

    Task<CloneRequest?> PromptCloneAsync();

    Task<string?> PickAsync(string title, string message, IReadOnlyList<string> options);

    Task<PerformanceChoice?> ConfirmPerformanceAsync(PerformanceSuggestion suggestion);

    Task<IReadOnlyList<RebaseStep>?> EditRebaseAsync(IReadOnlyList<RebaseStep> steps);

    Task<SettingsDraft?> EditSettingsAsync(SettingsDraft current);

    Task ShowAboutAsync();

    Task CopyAsync(string text);
}

public sealed record SettingsDraft(
    string GitExecutable,
    bool SideBySide,
    bool IgnoreWhitespace,
    string Theme,
    string MergeTool,
    IReadOnlyList<DiffFormatRule>? DiffFormats = null,
    string? Palette = null,
    string? DiffFont = null,
    double DiffFontSize = DiffFontPreference.DefaultSize);

public sealed record SecretPrompt(string? Value, bool RememberForSession);

public sealed record CloneRequest(string Url, string Destination);

public sealed record CommitterEdit(string Name, string Email, bool Global);

public sealed record PerformanceChoice(bool ManyFiles, bool FileSystemMonitor);

public interface IWorkspaceHost
{
    IDialogService? Dialogs { get; }

    GitProcessRunner Runner { get; }

    string? GitExecutable { get; }

    /// <summary>Shell command for git mergetool. Null uses the in-app editor unless git has merge.tool.</summary>
    string? MergeTool { get; }

    /// <summary>File type tools. Empty when the host has none.</summary>
    IReadOnlyList<DiffFormatRule> DiffFormats => [];

    bool GitReady { get; }

    Task Activate(RepositoryViewModel tab);

    Task Close(RepositoryViewModel tab);

    void NoteLoaded(RepositoryViewModel tab);

    void Save();

    Task OpenRepositoryAsync(string path);
}
