using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Sextant.Git;
using Sextant.Git.Diff;

namespace Sextant;

/// <summary>Installs the diff and merge font, and paints controls that already exist.</summary>
public static class DiffFont
{
    public const string ResourceKey = "DiffFontFamily";

    public const string SizeKey = "DiffFontSize";

    public static event Action? Changed;

    public static FontFamily Current { get; private set; } = new(DiffFontPreference.Fallback);

    public static double Size { get; private set; } = DiffFontPreference.DefaultSize;

    public static void Apply(string? saved, double size = 0)
    {
        Current = new FontFamily(DiffFontPreference.Family(saved));
        Size = DiffFontPreference.NormalizeSize(size);
        if (Application.Current is not { } app)
        {
            Changed?.Invoke();
            return;
        }

        app.Resources[ResourceKey] = Current;
        app.Resources[SizeKey] = Size;
        if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
                Paint(window);
        }

        Changed?.Invoke();
    }

    static void Paint(Visual root)
    {
        foreach (var visual in root.GetVisualDescendants())
        {
            if (visual is TextEditor editor)
            {
                editor.FontFamily = Current;
                editor.FontSize = Size;
            }
            else if (visual is TextBlock block && block.Classes.Contains("diff"))
            {
                block.FontFamily = Current;
                block.FontSize = Size;
            }
            else if (visual is TextBox box && box.Classes.Contains("diff"))
            {
                box.FontFamily = Current;
                box.FontSize = Size;
            }
        }
    }
}
