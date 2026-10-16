using System.Text.Json;
using Sextant.Git.Models;

namespace Sextant.Git.Parsing;

public static class LfsLockParser
{
    public static IReadOnlyList<LfsLock> Parse(byte[] output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw InvalidOutput(verifiable: false);
            return ReadLocks(document.RootElement, verifiable: false);
        }
        catch (JsonException)
        {
            throw InvalidOutput(verifiable: false);
        }
    }

    /// <summary>
    /// Reads the JSON from <c>git lfs locks --verify --json</c>.
    /// That object is <c>ours</c> and <c>theirs</c>, not the flat lock array.
    /// </summary>
    public static LfsLockListing ParseVerifiable(byte[] output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || (!root.TryGetProperty("ours", out _) && !root.TryGetProperty("theirs", out _)))
                throw InvalidOutput(verifiable: true);
            var mine = ReadGroup(root, "ours");
            var theirs = ReadGroup(root, "theirs");
            return new LfsLockListing(mine, theirs, ForceUnlockSupported: true);
        }
        catch (JsonException)
        {
            throw InvalidOutput(verifiable: true);
        }
    }

    private static IReadOnlyList<LfsLock> ReadGroup(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return [];
        if (value.ValueKind != JsonValueKind.Array)
            throw InvalidOutput(verifiable: true);
        return ReadLocks(value, verifiable: true);
    }

    private static IReadOnlyList<LfsLock> ReadLocks(JsonElement array, bool verifiable)
    {
        var locks = new List<LfsLock>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(id.GetString()) || string.IsNullOrEmpty(path.GetString()))
                throw InvalidOutput(verifiable);
            var ownerName = "";
            if (item.TryGetProperty("owner", out var owner) && owner.ValueKind == JsonValueKind.Object
                && owner.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                ownerName = name.GetString() ?? "";
            locks.Add(new LfsLock(id.GetString()!, path.GetString()!, ownerName));
        }

        return locks;
    }

    private static RepositoryActionException InvalidOutput(bool verifiable) =>
        new(verifiable
            ? "Git LFS returned invalid lock information. Refresh the LFS locks pane to try again."
            : "Git LFS returned invalid lock information. Refresh the Files tab to try again.");
}
