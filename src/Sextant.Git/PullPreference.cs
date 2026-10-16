namespace Sextant.Git;

/// <summary>How the Pull button brings in the upstream. Pull with rebase stays a rebase.</summary>
public static class PullPreference
{
    public const string Rebase = "rebase";

    public const string Merge = "merge";

    public static string Normalize(string? value) =>
        string.Equals(value?.Trim(), Merge, StringComparison.OrdinalIgnoreCase) ? Merge : Rebase;

    public static bool IsMerge(string? value) => Normalize(value) == Merge;
}
