using Sextant.Git;
using Sextant.ViewModels;

namespace Sextant.Services;

public interface IDialogService
{
    Task<string?> PickFolderAsync(string title);

    Task<string?> PickGitExecutableAsync();

    Task<bool> ConfirmAsync(string title, string message, string confirm = "OK");

    Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false);

    Task<string?> SaveFileAsync(string title, string suggestedName);

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
    string? Palette = null);

public sealed record CloneRequest(string Url, string Destination);

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

    void Activate(RepositoryViewModel tab);

    void Close(RepositoryViewModel tab);

    void NoteLoaded(RepositoryViewModel tab);

    void Save();

    Task OpenRepositoryAsync(string path);
}
