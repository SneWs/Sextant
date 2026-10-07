using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class LocationPanelTests
{
    [Fact]
    public async Task Filter_keeps_matching_rows_and_eyes_hide_a_branch_and_a_stash()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "root\n");
        repo.CommitAll("root");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "feature/grass");
        repo.WriteFile("c.txt", "feature\n");
        repo.CommitAll("feature-only");
        var feature = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("tag", "v1");
        repo.Run("update-ref", "refs/remotes/origin/side", feature);
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "wip\n");
        repo.Run("stash", "push", "-m", "wip note");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new LocationsHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();

            var branch = vm.Locations.Single(item => item.Key == "b:feature/grass");
            var remote = vm.Locations.Single(item => item.Key == "r:origin/side");
            var stash = vm.Locations.Single(item => item.Key.StartsWith("s:", StringComparison.Ordinal));
            var tag = vm.Locations.Single(item => item.Key == "t:v1");
            Assert.True(branch.ShowEye);
            Assert.True(branch.EyeOpen);
            Assert.False(branch.EyeHidden);
            Assert.True(remote.ShowEye);
            Assert.True(stash.ShowEye);
            Assert.False(tag.ShowEye);
            Assert.False(Header(vm, "h:branches").ShowEye);
            Assert.Contains(vm.Rows, row => row.Subject == "feature-only");
            Assert.Contains(vm.Rows, row => row.Subject.Contains("wip note", StringComparison.Ordinal));

            vm.ToggleLocation(Header(vm, "h:branches"));
            Assert.DoesNotContain(vm.Locations, item => item.Key == "b:feature/grass");

            vm.LocationFilter = "GRASS";
            Assert.Contains(vm.Locations, item => item.Key == "b:feature/grass");
            Assert.Contains(vm.Locations, item => item.Key == "h:branches");
            Assert.DoesNotContain(vm.Locations, item => item.Key == "b:" + trunk);
            Assert.DoesNotContain(vm.Locations, item => item.Key == "t:v1");
            Assert.DoesNotContain(vm.Locations, item => item.Key.StartsWith("s:", StringComparison.Ordinal));
            Assert.DoesNotContain(vm.Locations, item => item.Key == "h:submodules");

            vm.LocationFilter = "v1";
            Assert.Equal(["h:tags", "t:v1"], vm.Locations.Select(item => item.Key).Where(key => key is "h:tags" or "t:v1").ToArray());
            Assert.Contains(vm.Locations, item => item.Key == "t:v1");
            Assert.DoesNotContain(vm.Locations, item => item.Key == "b:feature/grass");

            vm.LocationFilter = "wip";
            Assert.Contains(vm.Locations, item => item.Key.StartsWith("s:", StringComparison.Ordinal) && item.Label.Contains("wip note", StringComparison.Ordinal));
            Assert.DoesNotContain(vm.Locations, item => item.Key == "t:v1");

            vm.LocationFilter = "origin/side";
            Assert.Contains(vm.Locations, item => item.Key == "r:origin/side");
            Assert.DoesNotContain(vm.Locations, item => item.Key == "t:v1");

            vm.LocationFilter = "";
            Assert.Contains(vm.Locations, item => item.Key == "t:v1");
            Assert.Contains(vm.Locations, item => item.Key.StartsWith("s:", StringComparison.Ordinal));
            Assert.Contains(vm.Locations, item => item.Key == "h:submodules");
            Assert.DoesNotContain(vm.Locations, item => item.Key == "b:feature/grass");

            vm.ToggleLocation(Header(vm, "h:branches"));
            branch = vm.Locations.Single(item => item.Key == "b:feature/grass");
            await ((AsyncRelayCommand)branch.HideCommand).ExecuteAsync(null);
            branch = vm.Locations.Single(item => item.Key == "b:feature/grass");
            Assert.True(branch.EyeHidden);
            Assert.False(branch.EyeOpen);
            Assert.Equal(0.45, branch.LabelOpacity);
            Assert.Equal("Show branch", branch.HideLabel);
            Assert.DoesNotContain(vm.Rows, row => row.Subject == "feature-only");

            stash = vm.Locations.Single(item => item.Key.StartsWith("s:", StringComparison.Ordinal));
            await ((AsyncRelayCommand)stash.HideCommand).ExecuteAsync(null);
            stash = vm.Locations.Single(item => item.Key.StartsWith("s:", StringComparison.Ordinal));
            Assert.True(stash.EyeHidden);
            Assert.Equal("Show stash", stash.HideLabel);
            Assert.DoesNotContain(vm.Rows, row => row.Subject.Contains("wip note", StringComparison.Ordinal));
            Assert.Contains(vm.Rows, row => row.Subject == "root");

            remote = vm.Locations.Single(item => item.Key == "r:origin/side");
            await ((AsyncRelayCommand)remote.HideCommand).ExecuteAsync(null);
            remote = vm.Locations.Single(item => item.Key == "r:origin/side");
            Assert.True(remote.EyeHidden);
            Assert.Equal(0.45, remote.LabelOpacity);

            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Locations_header_opens_the_filter_and_rows_show_an_eye()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "one\n");
        repo.CommitAll("first");

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new LocationsHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();
            var view = new RepositoryView { DataContext = vm, Width = 1000, Height = 700 };
            var window = new Window { Content = view, Width = 1000, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Locations");
            var filter = view.FindControl<TextBox>("LocationFilterBox");
            Assert.NotNull(filter);
            Assert.False(filter.IsVisible);
            var search = view.FindControl<Button>("LocationSearchButton");
            Assert.NotNull(search);
            search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.LocationFilterOpen);
            Assert.True(filter.IsVisible);
            Assert.True(filter.IsFocused);

            var eyes = view.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("eye") && button.IsVisible).ToList();
            var branch = vm.Locations.Single(item => item.ShowEye && item.Key.StartsWith("b:", StringComparison.Ordinal));
            Assert.Contains(eyes, button => button.Command == branch.HideCommand);

            filter.Text = "nothing-matches";
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(vm.Locations, item => item.Key.StartsWith("b:", StringComparison.Ordinal));

            window.Close();
            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    private static LocationItem Header(RepositoryViewModel vm, string key) =>
        vm.Locations.Single(item => item.Key == key);

    private sealed class LocationsHost(string git) : IWorkspaceHost
    {
        public IDialogService? Dialogs => null;

        public GitProcessRunner Runner { get; } = new();

        public string? GitExecutable { get; } = git;

        public string? MergeTool => null;

        public bool GitReady => true;

        public Task Activate(RepositoryViewModel tab) => Task.CompletedTask;

        public Task Close(RepositoryViewModel tab) => Task.CompletedTask;

        public void NoteLoaded(RepositoryViewModel tab)
        {
        }

        public void Save()
        {
        }

        public Task OpenRepositoryAsync(string path) => Task.CompletedTask;
    }
}
