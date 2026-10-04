namespace Sextant.Git.Parsing;

/// <summary>The external merge command stored in settings. Empty means git's own merge tool.</summary>
public static class MergeToolCommand
{
    public static string? Normalize(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) || HasLineBreak(command))
            return null;
        return command.Trim();
    }

    public static bool HasLineBreak(string? command) =>
        !string.IsNullOrWhiteSpace(command) && command.IndexOfAny(['\r', '\n']) >= 0;
}
