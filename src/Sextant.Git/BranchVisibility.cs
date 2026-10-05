using System.Globalization;
using Sextant.Git.Parsing;

namespace Sextant.Git;

/// <summary>
/// Which branch and stash tips the commit graph walks. A hidden row stays in the locations list.
/// Commits that no visible branch or stash reaches are left out of the graph.
/// </summary>
public static class BranchVisibility
{
    public const string StashPrefix = "stash:";

    public static bool IsGraphBranch(string name)
    {
        if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
            return name.Length > "refs/heads/".Length;
        if (name.StartsWith("refs/remotes/", StringComparison.Ordinal))
            return name.Length > "refs/remotes/".Length
                && !name.EndsWith("/HEAD", StringComparison.Ordinal);
        return false;
    }

    /// <summary>A hidden stash, stored as <c>stash:</c> plus the stash commit sha.</summary>
    public static bool IsStashToken(string name) =>
        name.StartsWith(StashPrefix, StringComparison.Ordinal) && name.Length > StashPrefix.Length;

    public static string StashToken(string sha) => StashPrefix + sha;

    public static bool IsRemembered(string name) => IsGraphBranch(name) || IsStashToken(name);

    /// <summary>
    /// Pattern for <c>git log --exclude</c>. It is the name under <c>refs/heads</c> or <c>refs/remotes</c>.
    /// </summary>
    public static string? ExcludePattern(string refName)
    {
        if (!IsGraphBranch(refName))
            return null;
        if (refName.StartsWith("refs/heads/", StringComparison.Ordinal))
            return refName["refs/heads/".Length..];
        return refName["refs/remotes/".Length..];
    }

    /// <summary>Every graph branch except <paramref name="keep"/>.</summary>
    public static List<string> HiddenExcept(IEnumerable<GitRef> refs, string keep)
    {
        var hidden = new List<string>();
        foreach (var reference in refs)
        {
            if (!IsGraphBranch(reference.Name) || string.Equals(reference.Name, keep, StringComparison.Ordinal))
                continue;
            hidden.Add(reference.Name);
        }

        return hidden;
    }

    public static string Describe(IReadOnlyCollection<string> hidden)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var stashes = 0;
        foreach (var name in hidden)
        {
            if (IsStashToken(name))
            {
                stashes++;
                continue;
            }

            var pattern = ExcludePattern(name);
            if (!string.IsNullOrEmpty(pattern))
                names.Add(pattern);
        }

        var parts = new List<string>();
        if (names.Count == 1)
            parts.Add(names.First());
        else if (names.Count == 2)
            parts.Add(names.First() + " and " + names.Last());
        else if (names.Count > 2)
            parts.Add(names.Count.ToString(CultureInfo.InvariantCulture) + " branches");
        if (stashes == 1)
            parts.Add("1 stash");
        else if (stashes > 1)
            parts.Add(stashes.ToString(CultureInfo.InvariantCulture) + " stashes");
        if (parts.Count == 0)
            return "";
        return "Hiding " + string.Join(" and ", parts);
    }

    /// <summary>
    /// Caption above the graph. A search that names a revision ignores the hidden set,
    /// because that revision is the whole walk.
    /// </summary>
    public static string? Caption(HistoryQuery? query, IReadOnlyCollection<string> hidden)
    {
        var pinned = query?.Revision is not null || query?.ShaLookup == true;
        var phrase = pinned ? "" : Describe(hidden);
        var queryText = query is { IsEmpty: false } active ? active.Describe() : null;
        if (phrase.Length == 0)
            return queryText;
        if (string.IsNullOrEmpty(queryText))
            return phrase;
        return queryText + "  ·  " + char.ToLowerInvariant(phrase[0]) + phrase[1..];
    }
}
