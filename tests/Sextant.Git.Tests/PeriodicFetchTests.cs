using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Sextant.Git.Workspace;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class PeriodicFetchTests
{
    [Fact]
    public async Task Git_page_saves_fetch_minutes_and_zero_stays_off()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            var window = Show(0);
            var minutes = window.GetLogicalDescendants().OfType<NumericUpDown>().Single(box => box.IsEffectivelyVisible);
            Assert.Equal(0, minutes.Value);
            minutes.Value = 15;
            window.GetLogicalDescendants().OfType<Button>().Single(button => button.Content as string == "OK")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(15, window.Result?.FetchIntervalMinutes);
            window.Close();

            var again = Show(15);
            again.GetLogicalDescendants().OfType<NumericUpDown>().Single(box => box.IsEffectivelyVisible).Value = 0;
            again.GetLogicalDescendants().OfType<Button>().Single(button => button.Content as string == "OK")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, again.Result?.FetchIntervalMinutes);
            again.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Git_page_saves_refresh_on_focus()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            var window = Show(0);
            var box = window.GetLogicalDescendants().OfType<CheckBox>().Single(item => item.Content as string == "Refresh when focused");
            Assert.True(box.IsEffectivelyVisible);
            Assert.False(box.IsChecked);
            box.IsChecked = true;
            window.GetLogicalDescendants().OfType<Button>().Single(button => button.Content as string == "OK")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.Result?.RefreshOnFocus);
            window.Close();

            var again = new SettingsWindow(new SettingsDraft("", false, false, "system", "", RefreshOnFocus: true));
            again.Show();
            Dispatcher.UIThread.RunJobs();
            again.GetLogicalDescendants().OfType<ListBox>().Single().SelectedIndex = 3;
            Dispatcher.UIThread.RunJobs();
            var saved = again.GetLogicalDescendants().OfType<CheckBox>().Single(item => item.Content as string == "Refresh when focused");
            Assert.True(saved.IsChecked);
            saved.IsChecked = false;
            again.GetLogicalDescendants().OfType<Button>().Single(button => button.Content as string == "OK")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(again.Result?.RefreshOnFocus);
            again.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Zero_minutes_does_not_fetch_and_a_new_value_restarts_the_wait()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-ws-" + Guid.NewGuid().ToString("N"));
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner());
                var seen = new List<double>();
                vm.UseFetchDelay(async (interval, cancellationToken) =>
                {
                    seen.Add(interval.TotalMinutes);
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                });
                Assert.Empty(seen);

                vm.ApplyFetchInterval(1);
                Assert.Equal([1d], seen);
                vm.ApplyFetchInterval(3);
                Assert.Equal([1d, 3d], seen);
                await vm.Shutdown();
                return 0;
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Periodic_fetch_updates_the_active_repository_only()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var origin = new TempRepo();
        origin.WriteFile("a.txt", "one\n");
        origin.CommitAll("base");
        var first = Clone(origin);
        var second = Clone(origin);
        var directory = Path.Combine(Path.GetTempPath(), "sextant-ws-" + Guid.NewGuid().ToString("N"));
        try
        {
            origin.WriteFile("a.txt", "two\n");
            origin.CommitAll("second");
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var settings = new AppSettings
                {
                    GitExecutable = origin.Git,
                    ReopenTabs = false,
                    FetchIntervalMinutes = 1,
                };
                var vm = new MainViewModel(store, store.LoadWorkspace(), settings, new GitProcessRunner())
                {
                    GitExecutable = origin.Git,
                    GitReady = true,
                };
                var active = new RepositoryViewModel(vm, first);
                var other = new RepositoryViewModel(vm, second);
                vm.Tabs.Add(active);
                vm.Tabs.Add(other);
                await vm.Activate(other);
                await vm.Activate(active);
                Assert.True(active.IsReady, active.Banner);
                Assert.True(other.IsReady, other.Banner);
                Assert.False(active.IsBusy);
                Assert.False(other.IsBusy);
                Assert.Equal("", active.AheadBehindText);
                Assert.Equal("", other.AheadBehindText);
                var activeFetches = Fetches(active.CommandLog);
                var otherFetches = Fetches(other.CommandLog);

                var gates = new[]
                {
                    new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
                    new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
                };
                var turn = 0;
                vm.UseFetchDelay(async (interval, cancellationToken) =>
                {
                    Assert.Equal(1d, interval.TotalMinutes);
                    var current = Interlocked.Increment(ref turn);
                    if (current > gates.Length)
                        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                    else
                        await gates[current - 1].Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                });

                gates[0].TrySetResult();
                await WaitUntil(() => Fetches(active.CommandLog) == activeFetches + 1, () =>
                    $"turn {Volatile.Read(ref turn)} busy {active.IsBusy} ready {active.IsReady} banner {active.Banner} log {active.CommandLog}");
                Assert.Contains("↓1", active.AheadBehindText);
                Assert.Equal(otherFetches, Fetches(other.CommandLog));
                Assert.Equal("", other.AheadBehindText);

                await vm.Activate(other);
                gates[1].TrySetResult();
                await WaitUntil(() => Fetches(other.CommandLog) == otherFetches + 1, () =>
                    $"turn {Volatile.Read(ref turn)} busy {other.IsBusy} ready {other.IsReady} banner {other.Banner} log {other.CommandLog}");
                Assert.Contains("↓1", other.AheadBehindText);
                Assert.Equal(activeFetches + 1, Fetches(active.CommandLog));
                await vm.Shutdown();
                return 0;
            }, CancellationToken.None);
        }
        finally
        {
            DeleteDirectory(first);
            DeleteDirectory(second);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static SettingsWindow Show(int minutes)
    {
        var window = new SettingsWindow(new SettingsDraft("", false, false, "system", "", FetchIntervalMinutes: minutes));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.GetLogicalDescendants().OfType<ListBox>().Single().SelectedIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var label = window.GetLogicalDescendants().OfType<TextBlock>().Single(block => block.Text == "Fetch every");
        Assert.True(label.IsEffectivelyVisible);
        return window;
    }

    private static async Task WaitUntil(Func<bool> ready, Func<string>? detail = null)
    {
        for (var attempt = 0; attempt < 250; attempt++)
        {
            if (ready())
                return;
            await Task.Delay(20);
        }

        Assert.Fail(detail is null ? "The periodic fetch did not finish." : "The periodic fetch did not finish. " + detail());
    }

    private static int Fetches(string log) =>
        log.Split('\n').Count(line => line.Contains(" fetch ", StringComparison.Ordinal));

    private static string Clone(TempRepo origin)
    {
        var clone = Path.Combine(Path.GetTempPath(), "sextant-clone-" + Guid.NewGuid().ToString("N"));
        origin.Run("clone", origin.Directory, clone);
        origin.SetIdentity(clone);
        return clone;
    }

    private static void DeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;
                foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                    entry.Attributes = FileAttributes.Normal;
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(40);
            }
        }
    }
}
