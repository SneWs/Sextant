using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sextant.Git.Workspace;

/// <summary>A layout file is Sextant's dock record. It is not the workspace and not Dock's object graph.</summary>
public static class DockLayoutFile
{
    public const string TypeName = "Sextant layout";

    public const string SuggestedName = "Layout.sextant-layout";

    public const string Extension = "sextant-layout";

    public static readonly string[] SavePatterns = ["*.sextant-layout"];

    public static readonly string[] OpenPatterns = ["*.sextant-layout", "*.json"];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Write(string path, DockLayout layout)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(layout, Options));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Reads a layout file and migrates it. False leaves <paramref name="layout"/> null.</summary>
    public static bool TryRead(string path, out DockLayout? layout, out string? error)
    {
        layout = null;
        error = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<DockLayout>(File.ReadAllText(path), Options);
            if (parsed is null || !DockLayouts.TryAccept(parsed))
            {
                error = "That file is not a Sextant layout.";
                return false;
            }

            layout = parsed;
            return true;
        }
        catch (JsonException)
        {
            error = "That file is not a Sextant layout.";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Sextant could not read that file.";
            return false;
        }
    }
}
