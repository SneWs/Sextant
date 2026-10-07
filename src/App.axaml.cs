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
            var window = new MainWindow
            {
                DataContext = new MainViewModel(store, store.LoadWorkspace(), settings, new GitProcessRunner()),
            };
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) =>
            {
                if (window.DataContext is MainViewModel vm)
                    vm.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShowAbout(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainViewModel vm })
            vm.ShowAboutCommand.Execute(null);
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainViewModel vm })
            vm.OpenSettingsCommand.Execute(null);
    }
}
