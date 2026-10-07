using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Sextant.Git;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new WorkspaceStore(AppPaths.ConfigDirectory());
            var settings = store.LoadSettings();
            AppTheme.Apply(settings.Theme, settings.Palette);
            DiffFont.Apply(settings.DiffFont, settings.DiffFontSize);
            var window = new MainWindow
            {
                DataContext = new MainViewModel(store, store.LoadWorkspace(), settings, new GitProcessRunner()),
            };
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnShowAbout(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainViewModel vm })
            await vm.ShowAboutCommand.ExecuteAsync(null);
    }

    private async void OnOpenSettings(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainViewModel vm })
            await vm.OpenSettingsCommand.ExecuteAsync(null);
    }
}
