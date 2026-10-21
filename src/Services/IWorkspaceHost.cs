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

    /// <summary>Save dialog for one file type. Cancel returns null. The default cancels.</summary>
    Task<string?> SaveAsAsync(string title, string suggestedName, string typeName, IReadOnlyList<string> patterns, string defaultExtension) =>
        Task.FromResult<string?>(null);

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

    /// <summary>A short message with one button. The default uses a confirmation.</summary>
    Task ShowNoticeAsync(string title, string message)
    {
        return ConfirmAsync(title, message);
    }

    /// <summary>
    /// The update offer, including "Do not prompt me for new updates again".
    /// Closing the dialog keeps the checkbox. The default has no checkbox.
    /// </summary>
    Task<UpdateChoice> OfferUpdateAsync(string message, string confirm, bool skipPrompts)
    {
        return OfferWithoutCheck(message, confirm, skipPrompts);

        async Task<UpdateChoice> OfferWithoutCheck(string offerMessage, string offerConfirm, bool skip)
        {
            var accepted = await ConfirmAsync("Update available", offerMessage, offerConfirm).ConfigureAwait(false);
            return new UpdateChoice(accepted, skip);
        }
    }

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
    double DiffFontSize = DiffFontPreference.DefaultSize,
    string? PullMode = null,
    int FetchIntervalMinutes = 0);

public sealed record SecretPrompt(string? Value, bool RememberForSession);

public sealed record UpdateChoice(bool Accepted, bool SkipFuturePrompts);

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

    /// <summary><see cref="PullPreference.Rebase"/> or <see cref="PullPreference.Merge"/>.</summary>
    string PullMode => PullPreference.Rebase;

    bool GitReady { get; }

    Task Activate(RepositoryViewModel tab);

    Task Close(RepositoryViewModel tab);

    void NoteLoaded(RepositoryViewModel tab);

    void Save();

    Task OpenRepositoryAsync(string path);

    /// <summary>
    /// Opens and selects a tool dock for the active repository unless it is already selected, so a click can bring a pane to focus without stealing it when it is on screen. The default does nothing; hosts that own docks override it.
    /// </summary>
    void FocusDock(string id) { }
}
