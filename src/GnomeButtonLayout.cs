using System.Diagnostics;

namespace Sextant;

/// <summary>
/// GNOME stores the caption buttons in <c>org.gnome.desktop.wm.preferences button-layout</c>
/// as <c>left:right</c>. Ubuntu is usually <c>:minimize,maximize,close</c>. Fedora is usually
/// <c>appmenu:close</c>. The upstream default, used when the setting cannot be read, is close on the right.
/// </summary>
public readonly record struct GnomeButtonLayout(IReadOnlyList<string> Left, IReadOnlyList<string> Right)
{
    public const string Default = "appmenu:close";

    private static readonly string[] Known = ["minimize", "maximize", "close"];

    public static GnomeButtonLayout Parse(string? layout)
    {
        var text = Normalize(layout);
        var split = text.Split(':', 2);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var left = Side(split[0], seen);
        var right = Side(split.Length > 1 ? split[1] : "", seen);
        if (!seen.Contains("close"))
            right.Add("close");
        return new GnomeButtonLayout(left, right);
    }

    public static string Normalize(string? layout)
    {
        if (string.IsNullOrWhiteSpace(layout))
            return Default;
        var text = layout.Trim();
        if (text.Length >= 2 && text[0] == text[^1] && text[0] is '\'' or '"')
            text = text[1..^1].Trim();
        return text.Contains(':') ? text : Default;
    }

    public static string FromGsettings()
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "gsettings";
            process.StartInfo.ArgumentList.Add("get");
            process.StartInfo.ArgumentList.Add("org.gnome.desktop.wm.preferences");
            process.StartInfo.ArgumentList.Add("button-layout");
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            if (!process.Start())
                return Default;
            if (!process.WaitForExit(1500))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return Default;
            }

            if (process.ExitCode != 0)
                return Default;
            return Normalize(process.StandardOutput.ReadToEnd());
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Default;
        }
    }

    private static List<string> Side(string text, HashSet<string> seen)
    {
        var buttons = new List<string>();
        foreach (var token in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = token.ToLowerInvariant();
            if (!Known.Contains(name, StringComparer.Ordinal) || !seen.Add(name))
                continue;
            buttons.Add(name);
        }

        return buttons;
    }
}
