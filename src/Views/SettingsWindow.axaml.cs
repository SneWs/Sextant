using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Sextant.Git;
using Sextant.Git.Parsing;
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
        MergeCommand.Text = draft.MergeTool;
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
    }

    public SettingsDraft? Result { get; private set; }

    private void OnSection(object? sender, SelectionChangedEventArgs e)
    {
        if (AppearancePage is null || DiffPage is null || GitPage is null || MergePage is null || ErrorText is null)
            return;
        var index = Sections.SelectedIndex;
        if (index < 0)
        {
            Sections.SelectedIndex = 0;
            return;
        }

        AppearancePage.IsVisible = index == 0;
        DiffPage.IsVisible = index == 1;
        GitPage.IsVisible = index == 2;
        MergePage.IsVisible = index == 3;
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
            Sections.SelectedIndex = 2;
            ErrorText.Text = "That git executable was not found.";
            return;
        }

        var merge = MergeCommand.Text ?? "";
        if (MergeToolCommand.HasLineBreak(merge))
        {
            Sections.SelectedIndex = 3;
            ErrorText.Text = "The merge command must be a single line.";
            return;
        }

        var chosen = Dark.IsChecked == true ? ThemePreference.Dark
            : Light.IsChecked == true ? ThemePreference.Light
            : ThemePreference.System;
        Result = new SettingsDraft(
            path,
            SideBySide.IsChecked == true,
            IgnoreWhitespace.IsChecked == true,
            chosen,
            MergeToolCommand.Normalize(merge) ?? "");
        Close();
    }

    private static string ShellQuote(string path)
    {
        if (!path.Contains('\''))
            return "'" + path + "'";
        return "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
