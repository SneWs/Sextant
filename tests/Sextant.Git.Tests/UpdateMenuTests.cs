using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Sextant.Git;
using Sextant.Git.Models;
using Sextant.Git.Workspace;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class UpdateMenuTests
{
    [Fact]
    public async Task Check_for_update_is_in_the_application_menu_on_macOS_and_under_Help_on_Windows_and_Linux()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-update-menu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var appMenu = NativeMenu.GetMenu(Application.Current!);
                Assert.NotNull(appMenu);
                var appHeaders = appMenu.Items.OfType<NativeMenuItem>().Select(item => item.Header).ToList();
                Assert.Equal(["About Sextant", "Settings…", "Check for update"], appHeaders);

                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner());
                var window = new MainWindow { DataContext = vm, Width = 800, Height = 600 };
                window.Show();
                try
                {
                    var menu = NativeMenu.GetMenu(window);
                    Assert.NotNull(menu);
                    var helpMenus = menu.Items.OfType<NativeMenuItem>().Where(item => item.Header == "_Help").ToList();
                    Assert.Equal(2, helpMenus.Count);
                    Assert.Equal(OperatingSystem.IsMacOS(), helpMenus[0].IsVisible);
                    Assert.Equal(!OperatingSystem.IsMacOS(), helpMenus[1].IsVisible);
                    var macHelp = helpMenus[0].Menu;
                    var windowHelp = helpMenus[1].Menu;
                    Assert.NotNull(macHelp);
                    Assert.NotNull(windowHelp);
                    Assert.Equal(["_Get help"], macHelp.Items.OfType<NativeMenuItem>().Select(item => item.Header).ToList());
                    Assert.Equal(
                        ["_Get help", "_Check for update", "_About Sextant"],
                        windowHelp.Items.OfType<NativeMenuItem>().Select(item => item.Header).ToList());
                    var item = windowHelp.Items.OfType<NativeMenuItem>().Single(entry => entry.Header == "_Check for update");
                    Assert.Same(vm.CheckForUpdateCommand, item.Command);
                    Assert.True(vm.ShowCheckForUpdate);
                    Assert.True(item.IsVisible);
                }
                finally
                {
                    await vm.Shutdown();
                    window.DataContext = null;
                    window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_store_install_hides_check_for_update_and_does_not_ask_github()
    {
        StorePackage.Override = true;
        try
        {
            using var session = HeadlessUnitTestSession.StartNew(typeof(App));
            var directory = Path.Combine(Path.GetTempPath(), "sextant-store-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                await session.Dispatch(async () =>
                {
                    var appMenu = NativeMenu.GetMenu(Application.Current!);
                    Assert.NotNull(appMenu);
                    var appItem = appMenu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "Check for update");
                    Assert.False(appItem.IsVisible);

                    var dialogs = new RecordingDialogs();
                    var catalog = new FakeReleases { Release = Full("99.0.0") };
                    var store = new WorkspaceStore(directory);
                    var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner(), catalog);
                    vm.Attach(dialogs);
                    var window = new MainWindow { DataContext = vm, Width = 800, Height = 600 };
                    window.Show();
                    try
                    {
                        Assert.False(vm.ShowCheckForUpdate);
                        var menu = NativeMenu.GetMenu(window);
                        Assert.NotNull(menu);
                        var update = menu.Items.OfType<NativeMenuItem>()
                            .SelectMany(item => item.Menu?.Items.OfType<NativeMenuItem>() ?? [])
                            .Single(item => item.Header == "_Check for update");
                        Assert.False(update.IsVisible);
                        await vm.CheckForUpdateCommand.ExecuteAsync(null);
                        await vm.WatchForReleaseAsync();
                        await vm.CheckForReleaseAsync(announceWhenCurrent: true);
                        Assert.Equal(0, catalog.Calls);
                        Assert.Empty(dialogs.Offers);
                        Assert.Empty(dialogs.Notices);
                    }
                    finally
                    {
                        await vm.Shutdown();
                        window.DataContext = null;
                        window.Close();
                    }
                }, CancellationToken.None);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        finally
        {
            StorePackage.Override = null;
        }
    }

    [Fact]
    public void The_store_family_name_is_the_partner_center_package()
    {
        Assert.True(StorePackage.IsStoreFamily("20817M.Grenangen.SextantGit_nr8zhaqknvty0"));
        Assert.False(StorePackage.IsStoreFamily("Sextant_dev"));
        Assert.False(StorePackage.IsStoreFamily(null));
        Assert.False(StorePackage.IsStoreInstall);
    }

    [Fact]
    public async Task A_newer_release_downloads_this_platforms_file_and_a_current_copy_says_so()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var newer = Full("99.0.0");
                var expected = ReleaseCheck.Create(newer, AppInfo.Version, ReleaseCheck.Current());
                Assert.NotNull(expected);
                var dialogs = new RecordingDialogs { Accept = true };
                string? opened = null;
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner(), new FakeReleases { Release = newer })
                {
                    OpenUpdateLink = url => opened = url,
                };
                vm.Attach(dialogs);
                vm.StatusText = "Ready";
                await vm.CheckForUpdateCommand.ExecuteAsync(null);
                Assert.Equal("Ready", vm.StatusText);
                var confirm = Assert.Single(dialogs.Offers);
                Assert.Equal(expected.Confirm, confirm.Confirm);
                Assert.Equal(expected.Message, confirm.Message);
                Assert.False(confirm.SkipPrompts);
                Assert.Equal(expected.Url, opened);
                Assert.False(store.LoadSettings().SkipUpdatePrompt);
                Assert.Empty(dialogs.Notices);

                opened = null;
                dialogs.Offers.Clear();
                dialogs.Accept = false;
                await vm.CheckForReleaseAsync(announceWhenCurrent: true);
                Assert.Null(opened);
                Assert.Single(dialogs.Offers);

                dialogs.Offers.Clear();
                await vm.CheckForReleaseAsync(announceWhenCurrent: false);
                Assert.Single(dialogs.Offers);
                Assert.Empty(dialogs.Notices);
            }, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task The_startup_check_stays_quiet_when_this_copy_is_current()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-update-quiet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var dialogs = new RecordingDialogs();
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner(), new FakeReleases { Release = Full("99.0.0") });
                vm.Attach(dialogs);
                vm.StatusText = "Ready";
                await vm.WatchForReleaseAsync();
                Assert.Empty(dialogs.Offers);
                Assert.Empty(dialogs.Notices);
                await vm.CheckForReleaseAsync(announceWhenCurrent: false);
                Assert.Single(dialogs.Offers);
                Assert.Empty(dialogs.Notices);
                Assert.Equal("Ready", vm.StatusText);

                dialogs.Offers.Clear();
                var current = new PublishedRelease("v" + AppInfo.Version, AppInfo.Version, ReleaseCheck.ReleasesPage, []);
                var same = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner(), new FakeReleases { Release = current });
                same.Attach(dialogs);
                await same.CheckForReleaseAsync(announceWhenCurrent: false);
                Assert.Empty(dialogs.Offers);
                Assert.Empty(dialogs.Notices);
                await same.CheckForReleaseAsync(announceWhenCurrent: true);
                var notice = Assert.Single(dialogs.Notices);
                Assert.Contains("latest release", notice.Message, StringComparison.Ordinal);

                dialogs.Notices.Clear();
                var older = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner(), new FakeReleases { Release = Full("0.0.1") });
                older.Attach(dialogs);
                await older.CheckForReleaseAsync(announceWhenCurrent: true);
                Assert.Contains("newer than the latest release", Assert.Single(dialogs.Notices).Message, StringComparison.Ordinal);

                dialogs.Notices.Clear();
                var failed = new MainViewModel(
                    store,
                    store.LoadWorkspace(),
                    new AppSettings(),
                    new GitProcessRunner(),
                    new FakeReleases { Error = new HttpRequestException("offline") });
                failed.Attach(dialogs);
                await failed.CheckForReleaseAsync(announceWhenCurrent: false);
                Assert.Empty(dialogs.Offers);
                Assert.Empty(dialogs.Notices);
                await failed.CheckForReleaseAsync(announceWhenCurrent: true);
                Assert.Contains("could not check", Assert.Single(dialogs.Notices).Message, StringComparison.Ordinal);

                dialogs.Notices.Clear();
                var none = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner(), new FakeReleases());
                none.Attach(dialogs);
                await none.CheckForReleaseAsync(announceWhenCurrent: true);
                Assert.Contains("No Sextant release", Assert.Single(dialogs.Notices).Message, StringComparison.Ordinal);
            }, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Do_not_prompt_again_is_saved_and_the_startup_check_stays_quiet()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-update-skip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var dialogs = new RecordingDialogs { Accept = false, SkipPrompts = true };
                var store = new WorkspaceStore(directory);
                var settings = new AppSettings();
                var catalog = new FakeReleases { Release = Full("99.0.0") };
                var vm = new MainViewModel(store, store.LoadWorkspace(), settings, new GitProcessRunner(), catalog);
                vm.Attach(dialogs);
                await vm.CheckForReleaseAsync(announceWhenCurrent: true);
                Assert.True(store.LoadSettings().SkipUpdatePrompt);
                Assert.False(Assert.Single(dialogs.Offers).SkipPrompts);

                dialogs.Offers.Clear();
                var calls = catalog.Calls;
                var saved = store.LoadSettings();
                var later = new MainViewModel(store, store.LoadWorkspace(), saved, new GitProcessRunner(), catalog);
                later.Attach(dialogs);
                await later.CheckForReleaseAsync(announceWhenCurrent: false);
                Assert.Equal(calls, catalog.Calls);
                Assert.Empty(dialogs.Offers);

                dialogs.SkipPrompts = false;
                await later.CheckForReleaseAsync(announceWhenCurrent: true);
                Assert.True(Assert.Single(dialogs.Offers).SkipPrompts);
                Assert.False(store.LoadSettings().SkipUpdatePrompt);

                dialogs.Offers.Clear();
                catalog.Release = Full("100.0.0");
                await later.CheckForReleaseAsync(announceWhenCurrent: false);
                Assert.False(Assert.Single(dialogs.Offers).SkipPrompts);
            }, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task The_update_dialog_offers_to_stop_later_prompts()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(async () =>
        {
            var owner = new Window { Width = 800, Height = 600 };
            owner.Show();
            var service = new AvaloniaDialogService(owner);
            try
            {
                Dispatcher.UIThread.Post(KeepQuiet, DispatcherPriority.SystemIdle);
                var declined = await service.OfferUpdateAsync("Sextant 2.0.0 is available.", "Download", skipPrompts: false);
                Assert.False(declined.Accepted);
                Assert.True(declined.SkipFuturePrompts);

                Dispatcher.UIThread.Post(DownloadAnyway, DispatcherPriority.SystemIdle);
                var accepted = await service.OfferUpdateAsync("Sextant 2.0.0 is available.", "Download", skipPrompts: true);
                Assert.True(accepted.Accepted);
                Assert.True(accepted.SkipFuturePrompts);
            }
            finally
            {
                owner.Close();
            }

            void KeepQuiet()
            {
                var dialog = Assert.Single(owner.OwnedWindows);
                Assert.Equal("Update available", dialog.Title);
                var box = Assert.Single(dialog.GetLogicalDescendants().OfType<CheckBox>());
                Assert.Equal("Do not prompt me for new updates again", box.Content);
                Assert.NotEqual(true, box.IsChecked);
                box.IsChecked = true;
                var cancel = dialog.GetLogicalDescendants().OfType<Button>().Single(button => button.Content as string == "Cancel");
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            void DownloadAnyway()
            {
                var dialog = Assert.Single(owner.OwnedWindows);
                var box = Assert.Single(dialog.GetLogicalDescendants().OfType<CheckBox>());
                Assert.Equal(true, box.IsChecked);
                var download = dialog.GetLogicalDescendants().OfType<Button>().Single(button => button.Content as string == "Download");
                download.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }, CancellationToken.None);
    }

    private static PublishedRelease Full(string version)
    {
        var tag = "v" + version;
        string Url(string name) => "https://github.com/SneWs/Sextant/releases/download/" + tag + "/" + name;
        return new PublishedRelease(
            tag,
            version,
            "https://github.com/SneWs/Sextant/releases/tag/" + tag,
            [
                new ReleaseAsset("Sextant-osx-arm64.tar", Url("Sextant-osx-arm64.tar")),
                new ReleaseAsset("Sextant-win-x64.zip", Url("Sextant-win-x64.zip")),
                new ReleaseAsset("Sextant-win-arm64.zip", Url("Sextant-win-arm64.zip")),
                new ReleaseAsset("Sextant-linux-x64.deb", Url("Sextant-linux-x64.deb")),
                new ReleaseAsset("Sextant-linux-arm64.deb", Url("Sextant-linux-arm64.deb")),
                new ReleaseAsset("Sextant-linux-x64.rpm", Url("Sextant-linux-x64.rpm")),
                new ReleaseAsset("Sextant-linux-arm64.rpm", Url("Sextant-linux-arm64.rpm")),
            ]);
    }

    private sealed class FakeReleases : IReleaseCatalog
    {
        public PublishedRelease? Release { get; set; }

        public Exception? Error { get; init; }

        public int Calls { get; private set; }

        public Task<PublishedRelease?> LatestAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Error is not null)
                return Task.FromException<PublishedRelease?>(Error);
            return Task.FromResult(Release);
        }
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public List<(string Message, string Confirm, bool SkipPrompts)> Offers { get; } = [];

        public List<(string Title, string Message)> Notices { get; } = [];

        public bool Accept { get; set; }

        public bool SkipPrompts { get; set; }

        public Task<bool> ConfirmAsync(string title, string message, string confirm = "OK") =>
            Task.FromResult(Accept);

        public Task<UpdateChoice> OfferUpdateAsync(string message, string confirm, bool skipPrompts)
        {
            Offers.Add((message, confirm, skipPrompts));
            return Task.FromResult(new UpdateChoice(Accept, SkipPrompts));
        }

        public Task ShowNoticeAsync(string title, string message)
        {
            Notices.Add((title, message));
            return Task.CompletedTask;
        }

        public Task<string?> PickFolderAsync(string title, string? startDirectory = null) => throw new NotSupportedException();

        public Task<string?> PickGitExecutableAsync() => throw new NotSupportedException();

        public Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false) => throw new NotSupportedException();

        public Task<string?> PromptSecretAsync(string title, string message) => throw new NotSupportedException();

        public Task<string?> SaveFileAsync(string title, string suggestedName) => throw new NotSupportedException();

        public Task<string?> PickFileAsync(string title, string typeName, IReadOnlyList<string> patterns) => throw new NotSupportedException();

        public Task<CloneRequest?> PromptCloneAsync() => throw new NotSupportedException();

        public Task<string?> PickAsync(string title, string message, IReadOnlyList<string> options) => throw new NotSupportedException();

        public Task<PerformanceChoice?> ConfirmPerformanceAsync(PerformanceSuggestion suggestion) => throw new NotSupportedException();

        public Task<IReadOnlyList<RebaseStep>?> EditRebaseAsync(IReadOnlyList<RebaseStep> steps) => throw new NotSupportedException();

        public Task<SettingsDraft?> EditSettingsAsync(SettingsDraft current) => throw new NotSupportedException();

        public Task ShowAboutAsync() => throw new NotSupportedException();

        public Task CopyAsync(string text) => throw new NotSupportedException();
    }
}
