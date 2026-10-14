using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
                var confirm = Assert.Single(dialogs.Confirms);
                Assert.Equal("Update available", confirm.Title);
                Assert.Equal(expected.Confirm, confirm.Confirm);
                Assert.Equal(expected.Message, confirm.Message);
                Assert.Equal(expected.Url, opened);
                Assert.Equal("99.0.0", store.LoadSettings().UpdateNotice);
                Assert.Empty(dialogs.Notices);

                opened = null;
                dialogs.Confirms.Clear();
                dialogs.Accept = false;
                await vm.CheckForReleaseAsync(announceWhenCurrent: true);
                Assert.Null(opened);
                Assert.Single(dialogs.Confirms);

                dialogs.Confirms.Clear();
                await vm.CheckForReleaseAsync(announceWhenCurrent: false);
                Assert.Empty(dialogs.Confirms);
                Assert.Empty(dialogs.Notices);
            }, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task The_startup_check_stays_quiet_when_this_copy_is_current_or_the_release_was_already_offered()
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
                var settings = new AppSettings { UpdateNotice = "99.0.0" };
                var vm = new MainViewModel(store, store.LoadWorkspace(), settings, new GitProcessRunner(), new FakeReleases { Release = Full("99.0.0") });
                vm.Attach(dialogs);
                vm.StatusText = "Ready";
                await vm.WatchForReleaseAsync();
                Assert.Empty(dialogs.Confirms);
                Assert.Empty(dialogs.Notices);
                await vm.CheckForReleaseAsync(announceWhenCurrent: false);
                Assert.Empty(dialogs.Confirms);
                Assert.Empty(dialogs.Notices);
                Assert.Equal("Ready", vm.StatusText);

                var current = new PublishedRelease("v" + AppInfo.Version, AppInfo.Version, ReleaseCheck.ReleasesPage, []);
                var same = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner(), new FakeReleases { Release = current });
                same.Attach(dialogs);
                await same.CheckForReleaseAsync(announceWhenCurrent: false);
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
        public PublishedRelease? Release { get; init; }

        public Exception? Error { get; init; }

        public Task<PublishedRelease?> LatestAsync(CancellationToken cancellationToken)
        {
            if (Error is not null)
                return Task.FromException<PublishedRelease?>(Error);
            return Task.FromResult(Release);
        }
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public List<(string Title, string Message, string Confirm)> Confirms { get; } = [];

        public List<(string Title, string Message)> Notices { get; } = [];

        public bool Accept { get; set; }

        public Task<bool> ConfirmAsync(string title, string message, string confirm = "OK")
        {
            Confirms.Add((title, message, confirm));
            return Task.FromResult(Accept);
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
