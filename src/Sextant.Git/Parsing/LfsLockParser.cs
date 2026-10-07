using System.Text.Json;

namespace Sextant.Git.Parsing;

public static class LfsLockParser
{
    public static IReadOnlyList<LfsLock> Parse(byte[] output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw InvalidOutput();
            var locks = new List<LfsLock>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                    || !item.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String
                    || string.IsNullOrEmpty(id.GetString()) || string.IsNullOrEmpty(path.GetString()))
                    throw InvalidOutput();
                var ownerName = "";
                if (item.TryGetProperty("owner", out var owner) && owner.ValueKind == JsonValueKind.Object
                    && owner.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    ownerName = name.GetString() ?? "";
                locks.Add(new LfsLock(id.GetString()!, path.GetString()!, ownerName));
            }

            return locks;
        }
        catch (JsonException)
        {
            throw InvalidOutput();
        }
    }

    private static RepositoryActionException InvalidOutput() =>
        new("Git LFS returned invalid lock information. Refresh the Files tab to try again.");
}
