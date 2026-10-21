using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace Sextant.ViewModels;

public partial class MainViewModel
{
    private readonly IReleaseCatalog _releases;
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    /// <summary>Tests record the download link here. A running app opens it in the browser.</summary>
    internal Action<string>? OpenUpdateLink { get; set; }

    /// <summary>Asks GitHub and always reports the result.</summary>
    internal Task CheckForReleaseAsync(bool announceWhenCurrent) => RunReleaseCheckAsync(announceWhenCurrent);

    /// <summary>Startup check. It stays quiet when this copy is current, and when prompts are turned off.</summary>
    internal async Task WatchForReleaseAsync()
    {
        if (ReleaseCheck.IsTestProcess || StorePackage.IsStoreInstall)
            return;
        try
        {
            await RunReleaseCheckAsync(announceWhenCurrent: false);
        }
        catch (Exception)
        {
            // The window is already open. A startup check that cannot report stays quiet.
        }
    }

    [RelayCommand]
    private Task CheckForUpdateAsync() => RunReleaseCheckAsync(announceWhenCurrent: true);

    private async Task RunReleaseCheckAsync(bool announceWhenCurrent)
    {
        // The Store installs and updates this package. Do not offer a GitHub download.
        if (StorePackage.IsStoreInstall)
            return;

        if (!announceWhenCurrent && _settings.SkipUpdatePrompt)
            return;

        // Do not check for updates in a local build. It is not a release, and it is not on GitHub.
        if (AppInfo.IsLocalBuild)
            return;

        var acquired = false;
        string? previousStatus = null;
        try
        {
            if (!await _updateGate.WaitAsync(0).ConfigureAwait(false))
            {
                if (!announceWhenCurrent)
                    return;
                await _updateGate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            }

            acquired = true;
            if (announceWhenCurrent)
            {
                await OnUi(() =>
                {
                    previousStatus = StatusText;
                    StatusText = "Checking for a new release…";
                });
            }

            PublishedRelease? release = null;
            var failed = false;
            try
            {
                release = await _releases.LatestAsync(_lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                failed = true;
            }

            await OnUi(async () =>
            {
                if (announceWhenCurrent && StatusText == "Checking for a new release…")
                    StatusText = previousStatus ?? "";
                if (Dialogs is null || _lifetime.IsCancellationRequested)
                    return;
                if (failed)
                {
                    if (announceWhenCurrent)
                        await Dialogs.ShowNoticeAsync("Sextant", "Sextant could not check GitHub for a new release.");
                    return;
                }

                if (release is null)
                {
                    if (announceWhenCurrent)
                        await Dialogs.ShowNoticeAsync("Sextant", "No Sextant release is published yet.");
                    return;
                }

                var compared = ReleaseVersion.Compare(release.Version, AppInfo.Version);
                if (compared == ReleaseCompare.Unknown)
                {
                    if (announceWhenCurrent)
                        await Dialogs.ShowNoticeAsync("Sextant", "Sextant could not compare this copy with the latest release.");
                    return;
                }

                var offer = ReleaseCheck.Create(release, AppInfo.Version, ReleaseCheck.Current());
                if (offer is null)
                {
                    if (!announceWhenCurrent)
                        return;
                    
                    var message = compared == ReleaseCompare.Older
                        ? $"Sextant {AppInfo.Version} is newer than the latest release, {release.Version}."
                        : $"Sextant {AppInfo.Version} is the latest release.";
                    await Dialogs.ShowNoticeAsync("Sextant", message);
                    return;
                }

                var choice = await Dialogs.OfferUpdateAsync(offer.Message, offer.Confirm, _settings.SkipUpdatePrompt);
                if (choice.SkipFuturePrompts != _settings.SkipUpdatePrompt)
                {
                    _settings.SkipUpdatePrompt = choice.SkipFuturePrompts;
                    _store.SaveSettings(_settings);
                }

                if (choice.Accepted)
                    OpenReleaseLink(offer.Url);
            });
        }
        finally
        {
            try
            {
                if (announceWhenCurrent)
                {
                    await OnUi(() =>
                    {
                        if (StatusText == "Checking for a new release…")
                            StatusText = previousStatus ?? "";
                    });
                }
            }
            finally
            {
                if (acquired)
                    _updateGate.Release();
            }
        }
    }

    private void OpenReleaseLink(string url)
    {
        if (!ReleaseCheck.IsReleaseUrl(url))
        {
            StatusText = "That download link cannot be opened.";
            return;
        }

        if (OpenUpdateLink is not null)
        {
            OpenUpdateLink(url);
            return;
        }

        try
        {
            DesktopOpen.Start(DesktopOpen.OpenUrl(DesktopOpen.Current, url));
        }
        catch (Exception)
        {
            StatusText = "The download page could not be opened.";
        }
    }

    private async Task OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }

    private async Task OnUi(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }
}
