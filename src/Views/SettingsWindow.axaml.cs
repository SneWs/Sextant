using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Sextant;
using Sextant.Git;
using Sextant.Git.Diff;
using Sextant.Git.Parsing;
using Sextant.Git.Workspace;
using Sextant.Services;

namespace Sextant.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsDraft draft) : this()
    {
        GitPath.Text = draft.GitExecutable;
        Inline.IsChecked = !draft.SideBySide;
        SideBySide.IsChecked = draft.SideBySide;
        IgnoreWhitespace.IsChecked = draft.IgnoreWhitespace;
        LoadFonts(draft.DiffFont, draft.DiffFontSize);
        MergeCommand.Text = draft.MergeTool;
        LoadFormats(draft.DiffFormats);
        _choices = ThemeFiles.Choices(AppPaths.ThemesDirectory());
        var palette = PalettePreference.Normalize(draft.Palette);
        if (_choices.All(choice => choice.Id != palette))
            _choices.Add(new ThemeChoice(palette, palette, null));
        PaletteBox.ItemsSource = _choices;
        PaletteBox.SelectedItem = _choices.First(choice => choice.Id == palette);
        var themesDirectory = AppPaths.ThemesDirectory();
        ThemesFolderPath.Text = themesDirectory;
        // The theme's Underline value is one shared object. Another test thread may own it, and drawing it then throws.
        var underline = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Underline } };
        ThemesFolderLink.SetValue(TextBlock.TextDecorationsProperty, underline);
        ThemesFolderPath.TextDecorations = underline;
        ToolTip.SetTip(ThemesFolderLink, DesktopOpen.FolderLabel(DesktopOpen.Current));
        var theme = ThemePreference.Normalize(draft.Theme);
        FollowSystem.IsChecked = false;
        Light.IsChecked = false;
        Dark.IsChecked = false;
        if (theme == ThemePreference.Dark)
            Dark.IsChecked = true;
        else if (theme == ThemePreference.Light)
            Light.IsChecked = true;
        else
            FollowSystem.IsChecked = true;
        _savedTheme = draft.Theme;
        _savedPalette = draft.Palette;
        PaletteBox.SelectionChanged += (_, _) => PreviewAppearance();
        FollowSystem.IsCheckedChanged += (_, _) => PreviewAppearance();
        Light.IsCheckedChanged += (_, _) => PreviewAppearance();
        Dark.IsCheckedChanged += (_, _) => PreviewAppearance();
        _ready = true;
        DialogFocus.WhenShown(this, CurrentPage);
    }

    private Control? CurrentPage()
    {
        if (AppearancePage is null)
            return null;
        if (DiffPage.IsVisible)
            return DiffPage;
        if (FormatsPage.IsVisible)
            return FormatsPage;
        if (GitPage.IsVisible)
            return GitPage;
        if (MergePage.IsVisible)
            return MergePage;
        return AppearancePage;
    }

    public SettingsDraft? Result { get; private set; }

    private List<ThemeChoice> _choices = [];

    private string? _savedTheme;

    private string? _savedPalette;

    private bool _ready;

    private bool _previewed;

    protected override void OnClosed(EventArgs e)
    {
        if (Result is null && _previewed)
            AppTheme.Apply(_savedTheme, _savedPalette);
        base.OnClosed(e);
    }

    private void PreviewAppearance()
    {
        if (!_ready || PaletteBox is null || Dark is null || Light is null || ErrorText is null)
            return;
        var palette = PaletteBox.SelectedItem as ThemeChoice;
        if (palette is null)
            return;
        if (!ThemeXaml.TryValidate(palette, out var error))
        {
            ErrorText.Text = error;
            return;
        }

        ErrorText.Text = "";
        var chosen = Dark.IsChecked == true ? ThemePreference.Dark
            : Light.IsChecked == true ? ThemePreference.Light
            : ThemePreference.System;
        AppTheme.Apply(chosen, palette.Id);
        _previewed = true;
    }

    private void OnSection(object? sender, SelectionChangedEventArgs e)
    {
        if (AppearancePage is null || DiffPage is null || FormatsPage is null || GitPage is null || MergePage is null || ErrorText is null)
            return;
        var index = Sections.SelectedIndex;
        if (index < 0)
        {
            Sections.SelectedIndex = 0;
            return;
        }

        AppearancePage.IsVisible = index == 0;
        DiffPage.IsVisible = index == 1;
        FormatsPage.IsVisible = index == 2;
        GitPage.IsVisible = index == 3;
        MergePage.IsVisible = index == 4;
        ErrorText.Text = "";
    }

    private async void OnBrowseGit(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Git executable",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("git") { Patterns = OperatingSystem.IsWindows() ? ["git.exe"] : ["git"] },
                FilePickerFileTypes.All,
            ],
        });
        if (picked.Count > 0)
            GitPath.Text = picked[0].Path.LocalPath;
    }

    private void OnUseSystemGit(object? sender, RoutedEventArgs e) => GitPath.Text = "";

    private async void OnBrowseMerge(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Merge application",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.All],
        });
        if (picked.Count == 0)
            return;
        MergeCommand.Text = ShellQuote(picked[0].Path.LocalPath) + " \"$LOCAL\" \"$MERGED\" \"$REMOTE\"";
    }

    private void OnOpenThemesFolder(object? sender, RoutedEventArgs e)
    {
        var directory = AppPaths.ThemesDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            DesktopOpen.Start(DesktopOpen.OpenDirectory(DesktopOpen.Current, directory));
        }
        catch (Exception)
        {
            ErrorText.Text = "That folder could not be opened.";
        }
    }

    private void OnClearMerge(object? sender, RoutedEventArgs e) => MergeCommand.Text = "";

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var path = (GitPath.Text ?? "").Trim();
        if (path.Length > 0 && !File.Exists(path))
        {
            Sections.SelectedIndex = 3;
            ErrorText.Text = "That git executable was not found.";
            return;
        }

        var merge = MergeCommand.Text ?? "";
        if (MergeToolCommand.HasLineBreak(merge))
        {
            Sections.SelectedIndex = 4;
            ErrorText.Text = "The merge command must be a single line.";
            return;
        }

        var formatError = DiffFormatRules.TryCollect(ReadFormats(), out var formats);
        if (formatError is not null)
        {
            Sections.SelectedIndex = 2;
            ErrorText.Text = formatError;
            return;
        }

        var chosen = Dark.IsChecked == true ? ThemePreference.Dark
            : Light.IsChecked == true ? ThemePreference.Light
            : ThemePreference.System;
        var palette = PaletteBox.SelectedItem as ThemeChoice ?? _choices[0];
        if (!ThemeXaml.TryValidate(palette, out var themeError))
        {
            Sections.SelectedIndex = 0;
            ErrorText.Text = themeError;
            return;
        }

        Result = new SettingsDraft(
            path,
            SideBySide.IsChecked == true,
            IgnoreWhitespace.IsChecked == true,
            chosen,
            MergeToolCommand.Normalize(merge) ?? "",
            formats,
            palette.Id,
            SelectedFont(),
            SelectedSize());
        Close();
    }

    private void LoadFonts(string? saved, double size)
    {
        var choices = new List<FontChoice> { new("Default", "") };
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var family in FontManager.Current.SystemFonts)
            {
                var name = family.Name?.Trim();
                if (string.IsNullOrEmpty(name) || name.Contains(',') || name.Contains('"') || name.Contains('\''))
                    continue;
                if (name[0] == '$')
                    continue;
                names.Add(name);
            }
        }
        catch (Exception)
        {
        }

        foreach (var name in names)
            choices.Add(new FontChoice(name, name));

        var current = DiffFontPreference.Normalize(saved);
        if (current.Length > 0 && choices.All(choice => !string.Equals(choice.Saved, current, StringComparison.OrdinalIgnoreCase)))
            choices.Insert(1, new FontChoice(current, current));

        DiffFontBox.ItemsSource = choices;
        DiffFontBox.SelectedItem = choices.First(choice => string.Equals(choice.Saved, current, StringComparison.OrdinalIgnoreCase));
        DiffFontSizeBox.Value = (decimal)DiffFontPreference.NormalizeSize(size);
        DiffFontBox.SelectionChanged += (_, _) => UpdateFontPreview();
        DiffFontSizeBox.ValueChanged += (_, _) => UpdateFontPreview();
        UpdateFontPreview();
    }

    private void UpdateFontPreview()
    {
        var family = new FontFamily(DiffFontPreference.Family(SelectedFont()));
        var size = SelectedSize();
        foreach (var block in new[] { FontPreviewContext, FontPreviewAdded, FontPreviewRemoved })
        {
            block.FontFamily = family;
            block.FontSize = size;
        }
    }

    private string SelectedFont() =>
        DiffFontBox.SelectedItem is FontChoice choice ? choice.Saved : "";

    private double SelectedSize() =>
        DiffFontPreference.NormalizeSize((double)(DiffFontSizeBox.Value ?? (decimal)DiffFontPreference.DefaultSize));

    private sealed class FontChoice
    {
        public FontChoice(string title, string saved)
        {
            Title = title;
            Saved = saved;
        }

        public string Title { get; }

        public string Saved { get; }

        public override string ToString() => Title;
    }

    private void LoadFormats(IReadOnlyList<DiffFormatRule>? rules)
    {
        FormatRows.Children.Clear();
        var normalized = DiffFormatRules.Normalize(rules);
        if (normalized.Count == 0)
        {
            AddFormatRow("", "", "");
            return;
        }

        foreach (var rule in normalized)
            AddFormatRow(rule.Extension, rule.Transform, rule.Restore);
    }

    private void OnAddFormat(object? sender, RoutedEventArgs e) => AddFormatRow("", "", "");

    private void AddFormatRow(string extension, string transform, string restore)
    {
        var extensionBox = new TextBox { Text = extension, PlaceholderText = ".json" };
        var transformBox = new TextBox { Text = transform, PlaceholderText = "jq ." };
        var restoreBox = new TextBox { Text = restore, PlaceholderText = "jq -c ." };
        var remove = new Button { Content = "Remove", MinWidth = 72 };
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("96,*,*,Auto"),
            ColumnSpacing = 8,
        };
        Grid.SetColumn(transformBox, 1);
        Grid.SetColumn(restoreBox, 2);
        Grid.SetColumn(remove, 3);
        row.Children.Add(extensionBox);
        row.Children.Add(transformBox);
        row.Children.Add(restoreBox);
        row.Children.Add(remove);
        remove.Click += (_, _) => FormatRows.Children.Remove(row);
        FormatRows.Children.Add(row);
    }

    private List<(string Extension, string Transform, string Restore)> ReadFormats()
    {
        var rows = new List<(string, string, string)>();
        foreach (var child in FormatRows.Children)
        {
            if (child is not Grid row || row.Children.Count < 3)
                continue;
            rows.Add((
                (row.Children[0] as TextBox)?.Text ?? "",
                (row.Children[1] as TextBox)?.Text ?? "",
                (row.Children[2] as TextBox)?.Text ?? ""));
        }

        return rows;
    }

    private static string ShellQuote(string path)
    {
        if (!path.Contains('\''))
            return "'" + path + "'";
        return "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
