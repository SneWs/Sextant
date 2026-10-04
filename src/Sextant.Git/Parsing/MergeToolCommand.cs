namespace Sextant.Git.Parsing;

/// <summary>
/// The external merge command stored in settings.
/// Empty uses the in-app editor, unless git config already names <c>merge.tool</c>.
/// </summary>
public static class MergeToolCommand
{
    public static string? Normalize(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) || HasLineBreak(command))
            return null;
        return command.Trim();
    }

    /// <summary>
    /// A settings command, or a non-empty <c>merge.tool</c>, keeps the external tool.
    /// Otherwise the conflict opens in the in-app editor.
    /// </summary>
    public static bool UseInAppEditor(string? settingsCommand, IReadOnlyDictionary<string, string>? config)
    {
        if (Normalize(settingsCommand) is not null)
            return false;
        if (config is null || !config.TryGetValue("merge.tool", out var tool))
            return true;
        return string.IsNullOrWhiteSpace(tool);
    }

    public static bool HasLineBreak(string? command) =>
        !string.IsNullOrWhiteSpace(command) && command.IndexOfAny(['\r', '\n']) >= 0;
}
