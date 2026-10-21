using System.Text.Json;

namespace Sextant.Git.Wsl;

/// <summary>
/// A Store install keeps Sextant.AskPass.exe under Program Files\WindowsApps.
/// WSL can see that path and still gets "Permission denied" when it execs it.
/// The packaged app can read its own files, so the helper and the runtime it
/// loads are copied to a normal per-user folder before the distribution runs it.
/// A zip or a folder build is already outside WindowsApps and is left alone.
/// </summary>
internal static class AskPassPackageCopy
{
    internal static string Stage(string windowsExecutable, string? cacheRoot = null)
    {
        if (string.IsNullOrWhiteSpace(windowsExecutable) || !IsWindowsAppsPath(windowsExecutable))
            return windowsExecutable;

        var sourceDir = Path.GetDirectoryName(Path.GetFullPath(windowsExecutable));
        if (string.IsNullOrEmpty(sourceDir) || !File.Exists(windowsExecutable))
            return windowsExecutable;

        cacheRoot ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sextant",
            "askpass");
        var destinationDir = Path.Combine(cacheRoot, new DirectoryInfo(sourceDir).Name);
        var destinationExe = Path.Combine(destinationDir, Path.GetFileName(windowsExecutable));
        var stamp = Path.Combine(destinationDir, ".staged");
        lock (Gate)
        {
            if (SameFile(windowsExecutable, destinationExe) && File.Exists(stamp))
                return destinationExe;

            var incoming = destinationDir + ".incoming";
            if (Directory.Exists(incoming))
                Directory.Delete(incoming, recursive: true);
            Directory.CreateDirectory(incoming);
            foreach (var name in FilesToCopy(sourceDir, windowsExecutable))
            {
                var source = Path.Combine(sourceDir, name);
                if (!File.Exists(source))
                    continue;
                var target = Path.Combine(incoming, name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: false);
            }

            if (!File.Exists(Path.Combine(incoming, Path.GetFileName(windowsExecutable))))
            {
                Directory.Delete(incoming, recursive: true);
                return windowsExecutable;
            }

            File.WriteAllText(Path.Combine(incoming, ".staged"), "");
            if (Directory.Exists(destinationDir))
                Directory.Delete(destinationDir, recursive: true);
            Directory.Move(incoming, destinationDir);
            RemoveOtherCopies(cacheRoot, destinationDir);
            return destinationExe;
        }
    }

    private static readonly object Gate = new();

    private static bool IsWindowsAppsPath(string path) =>
        path.Contains(@"\Program Files\WindowsApps\", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/Program Files/WindowsApps/", StringComparison.OrdinalIgnoreCase);

    private static bool SameFile(string source, string destination)
    {
        if (!File.Exists(source) || !File.Exists(destination))
            return false;
        var left = new FileInfo(source);
        var right = new FileInfo(destination);
        return left.Length == right.Length && left.LastWriteTimeUtc == right.LastWriteTimeUtc;
    }

    private static void RemoveOtherCopies(string cacheRoot, string keep)
    {
        if (!Directory.Exists(cacheRoot))
            return;
        foreach (var directory in Directory.EnumerateDirectories(cacheRoot))
        {
            if (string.Equals(directory, keep, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static List<string> FilesToCopy(string sourceDir, string executable)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileName = Path.GetFileName(executable);
        var stem = Path.GetFileNameWithoutExtension(executable);
        names.Add(fileName);
        names.Add(stem + ".dll");
        names.Add(stem + ".deps.json");
        names.Add(stem + ".runtimeconfig.json");

        var depsPath = Path.Combine(sourceDir, stem + ".deps.json");
        if (!File.Exists(depsPath))
            return names.ToList();

        using var document = JsonDocument.Parse(File.ReadAllText(depsPath));
        if (!document.RootElement.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
            return names.ToList();
        foreach (var target in targets.EnumerateObject())
        {
            if (target.Value.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var library in target.Value.EnumerateObject())
            {
                AddNames(library.Value, "runtime", names);
                AddNames(library.Value, "native", names);
            }
        }

        return names.Where(name => IsRelativeFile(name)).ToList();
    }

    private static void AddNames(JsonElement library, string property, HashSet<string> names)
    {
        if (library.ValueKind != JsonValueKind.Object || !library.TryGetProperty(property, out var files) || files.ValueKind != JsonValueKind.Object)
            return;
        foreach (var file in files.EnumerateObject())
            names.Add(file.Name.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool IsRelativeFile(string name) =>
        name.Length > 0
        && !Path.IsPathRooted(name)
        && !name.Contains(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        && name != "..";
}
