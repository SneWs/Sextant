namespace Sextant.Git.Tests;

public class PalettePreferenceTests
{
    [Fact]
    public void Built_in_names_normalize_and_odd_names_are_refused()
    {
        Assert.Equal(PalettePreference.Catppuccin, PalettePreference.Normalize(null));
        Assert.Equal(PalettePreference.TokyoNight, PalettePreference.Normalize(" TokyoNight "));
        Assert.Equal(PalettePreference.Gruvbox, PalettePreference.Normalize("gruvbox"));
        Assert.Equal("my-theme", PalettePreference.Normalize("My Theme"));
        Assert.Equal(PalettePreference.Catppuccin, PalettePreference.Normalize("nope!"));
        Assert.Equal(PalettePreference.Catppuccin, PalettePreference.Normalize("../secret"));
    }

    [Fact]
    public void Theme_folder_lists_xaml_files_and_skips_the_rest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sextant-themes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Solarized.axaml"), "<ResourceDictionary/>");
            File.WriteAllText(Path.Combine(directory, "notes.txt"), "no");
            File.WriteAllText(Path.Combine(directory, "bad name!.xaml"), "<ResourceDictionary/>");
            var nested = Path.Combine(directory, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "hidden.xaml"), "<ResourceDictionary/>");

            var choices = ThemeFiles.Choices(directory);
            Assert.Equal(
                ["catppuccin", "gruvbox", "monokai", "tokyonight", "dracula", "github", "black", "solarized"],
                choices.Select(choice => choice.Id).ToArray());
            Assert.Equal("Solarized", choices.Single(choice => choice.Id == "solarized").Title);
            Assert.Null(choices.Single(choice => choice.Id == "gruvbox").Path);
            Assert.NotNull(choices.Single(choice => choice.Id == "solarized").Path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_user_file_replaces_the_built_in_of_the_same_name()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sextant-themes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Dracula.xaml");
            File.WriteAllText(path, "<ResourceDictionary/>");
            var choice = ThemeFiles.Choices(directory).Single(item => item.Id == PalettePreference.Dracula);
            Assert.Equal("Dracula", choice.Title);
            Assert.Equal(path, choice.Path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
