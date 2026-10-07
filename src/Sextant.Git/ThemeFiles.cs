namespace Sextant.Git;

/// <summary>User themes live as .xaml files in the config themes folder.</summary>
public static class ThemeFiles
{
    public static List<ThemeChoice> Choices(string themesDirectory)
    {
        var files = List(themesDirectory);
        var byId = new Dictionary<string, ThemeChoice>(StringComparer.Ordinal);
        foreach (var file in files)
            byId[file.Id] = file;

        var choices = new List<ThemeChoice>();
        foreach (var builtIn in PalettePreference.BuiltIn)
        {
            if (byId.TryGetValue(builtIn.Id, out var file))
            {
                choices.Add(new ThemeChoice(builtIn.Id, builtIn.Title, file.Path));
                byId.Remove(builtIn.Id);
            }
            else
                choices.Add(builtIn);
        }

        foreach (var file in byId.Values.OrderBy(choice => choice.Title, StringComparer.OrdinalIgnoreCase))
            choices.Add(file);
        return choices;
    }

    public static IReadOnlyList<ThemeChoice> List(string themesDirectory)
    {
        if (string.IsNullOrWhiteSpace(themesDirectory))
            return [];
        try
        {
            Directory.CreateDirectory(themesDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var found = new List<ThemeChoice>();
        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(themesDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        foreach (var path in paths.OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(path);
            if (!extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".axaml", StringComparison.OrdinalIgnoreCase))
                continue;
            var stem = Path.GetFileNameWithoutExtension(path);
            var id = PalettePreference.IdFromStem(stem);
            if (id is null)
                continue;
            if (found.Any(choice => choice.Id == id))
                continue;
            found.Add(new ThemeChoice(id, stem.Trim(), path));
        }

        return found;
    }
}
