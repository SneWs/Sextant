using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Sextant.Docking;
using Sextant.Git;
using Sextant.Git.AskPass;
using Sextant.Git.Workspace;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant;

public partial class App : Application
{
    private AskPassServer? _askPass;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (!StorePackage.IsStoreInstall || NativeMenu.GetMenu(this) is not { } menu)
            return;
        foreach (var item in menu.Items.OfType<NativeMenuItem>())
        {
            if (item.Header == "Check for update")
                item.IsVisible = false;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        DockChrome.Ensure(this);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new WorkspaceStore(AppPaths.ConfigDirectory());
            var settings = store.LoadSettings();
            AppTheme.Apply(settings.Theme, settings.Palette);
            DiffFont.Apply(settings.DiffFont, settings.DiffFontSize);
            var runner = new GitProcessRunner();
            var window = new MainWindow
            {
                DataContext = new MainViewModel(store, store.LoadWorkspace(), settings, runner),
            };
            if (OperatingSystem.IsWindows())
                _askPass = WindowsAskPass.Attach(runner, window);
            desktop.ShutdownRequested += (_, _) =>
            {
                if (_askPass is not null)
                    _ = _askPass.DisposeAsync().AsTask();
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

    private async void OnCheckForUpdate(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainViewModel vm })
            await vm.CheckForUpdateCommand.ExecuteAsync(null);
    }
}
