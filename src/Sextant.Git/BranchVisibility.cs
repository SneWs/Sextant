using System.Globalization;
using Sextant.Git.Parsing;

namespace Sextant.Git;

/// <summary>
/// Which branch tips the commit graph walks. A hidden branch stays in the locations list.
/// Commits that no visible branch reaches are left out of the graph.
/// </summary>
public static class BranchVisibility
{
    public static bool IsGraphBranch(string name)
    {
        if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
            return name.Length > "refs/heads/".Length;
        if (name.StartsWith("refs/remotes/", StringComparison.Ordinal))
            return name.Length > "refs/remotes/".Length
                && !name.EndsWith("/HEAD", StringComparison.Ordinal);
        return false;
    }

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
        foreach (var name in hidden)
        {
            var pattern = ExcludePattern(name);
            if (!string.IsNullOrEmpty(pattern))
                names.Add(pattern);
        }

        if (names.Count == 0)
            return "";
        if (names.Count == 1)
            return "Hiding " + names.First();
        if (names.Count == 2)
            return "Hiding " + names.First() + " and " + names.Last();
        return "Hiding " + names.Count.ToString(CultureInfo.InvariantCulture) + " branches";
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
