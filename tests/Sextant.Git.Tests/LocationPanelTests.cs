using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
    public async Task Locations_filter_is_shown_and_rows_show_an_eye()
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

            Assert.NotNull(view.GetVisualDescendants().OfType<LocationsToolView>().Single());
            var filter = view.FindControl<TextBox>("LocationFilterBox");
            Assert.NotNull(filter);
            Assert.True(filter.IsVisible);

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

    [Fact]
    public async Task Location_groups_show_icons_with_space_between_rows_and_keep_their_fold()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "root\n");
        repo.CommitAll("root");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "feature/a");
        repo.WriteFile("a.txt", "feature a\n");
        repo.CommitAll("feature a");
        repo.Run("switch", "-c", "feature/b");
        repo.WriteFile("a.txt", "feature b\n");
        repo.CommitAll("feature b");
        repo.Run("tag", "v1");
        repo.Run("update-ref", "refs/remotes/origin/side", "HEAD");
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "wip\n");
        repo.Run("stash", "push", "-m", "wip note");

        await session.Dispatch(async () =>
        {
            var host = new LocationsHost(repo.Git);
            var vm = new RepositoryViewModel(host, repo.Directory);
            IReadOnlyList<string>? saved = null;
            host.OnSave = () => saved = vm.CollapsedLocationNames.ToArray();
            vm.UseCollapsedLocations(["h:tags", "b:feature"]);
            await vm.EnsureLoadedAsync();

            Assert.Equal(LocationIcons.Branches, Header(vm, "h:branches").IconName);
            Assert.True(Header(vm, "h:branches").ShowIcon);
            Assert.NotNull(Header(vm, "h:branches").Icon);
            Assert.Equal(LocationIcons.Folder, vm.Locations.Single(item => item.Key == "g:b:feature").IconName);
            Assert.Equal(LocationIcons.Stashes, Header(vm, "h:stashes").IconName);
            Assert.Equal(LocationIcons.Submodules, Header(vm, "h:submodules").IconName);
            Assert.Equal(LocationIcons.Remotes, Header(vm, "h:remotes").IconName);
            Assert.Equal(LocationIcons.Remote, Header(vm, "h:remote:origin").IconName);
            Assert.Equal(LocationIcons.Tags, Header(vm, "h:tags").IconName);
            Assert.Equal("", vm.Locations.Single(item => item.Key == "b:" + trunk).IconName);
            Assert.False(Header(vm, "h:tags").IsExpanded);
            Assert.DoesNotContain(vm.Locations, item => item.Key == "t:v1");
            Assert.False(vm.Locations.Single(item => item.Key == "g:b:feature").IsExpanded);
            Assert.DoesNotContain(vm.Locations, item => item.Key == "b:feature/a");

            await vm.Refresh();
            Assert.False(Header(vm, "h:tags").IsExpanded);
            Assert.DoesNotContain(vm.Locations, item => item.Key == "b:feature/b");

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var names = view.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                .Where(path => path.IsEffectivelyVisible)
                .Select(path => AutomationProperties.GetName(path))
                .Where(name => name is { Length: > 0 })
                .ToArray();
            Assert.Contains(LocationIcons.Branches, names);
            Assert.Contains(LocationIcons.Folder, names);
            Assert.Contains(LocationIcons.Stashes, names);
            Assert.Contains(LocationIcons.Remote, names);
            Assert.Contains(LocationIcons.Tags, names);

            var list = view.FindControl<ListBox>("LocationList")!;
            var rows = list.GetVisualDescendants().OfType<ListBoxItem>().Where(item => item.IsVisible).Take(2).ToArray();
            Assert.Equal(2, rows.Length);
            var first = rows[0].TranslatePoint(default, list);
            var second = rows[1].TranslatePoint(default, list);
            Assert.NotNull(first);
            Assert.NotNull(second);
            var gap = second.Value.Y - (first.Value.Y + rows[0].Bounds.Height);
            Assert.True(rows[0].Bounds.Height >= 26, $"row height {rows[0].Bounds.Height}");
            Assert.True(gap >= 1, $"gap {gap}");

            vm.ToggleLocation(Header(vm, "h:branches"));
            Assert.Equal(["b:feature", "h:branches", "h:tags"], saved);
            var again = new RepositoryViewModel(host, repo.Directory);
            again.UseCollapsedLocations(saved);
            await again.EnsureLoadedAsync();
            Assert.False(Header(again, "h:branches").IsExpanded);
            Assert.DoesNotContain(again.Locations, item => item.Key is "g:b:feature" or "b:feature/a" or "t:v1");
            Assert.Contains(again.Locations, item => item.Key == "h:stashes");

            window.Close();
            await vm.DisposeAsync();
            await again.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Expand_and_collapse_all_locations_sit_left_of_the_filter()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "root\n");
        repo.CommitAll("root");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "feature/a");
        repo.WriteFile("a.txt", "feature a\n");
        repo.CommitAll("feature a");
        repo.Run("switch", "-c", "feature/b");
        repo.WriteFile("a.txt", "feature b\n");
        repo.CommitAll("feature b");
        repo.Run("tag", "v1");
        repo.Run("update-ref", "refs/remotes/origin/side", "HEAD");
        repo.Run("switch", trunk);
        repo.WriteFile("a.txt", "wip\n");
        repo.Run("stash", "push", "-m", "wip note");

        await session.Dispatch(async () =>
        {
            var host = new LocationsHost(repo.Git);
            var vm = new RepositoryViewModel(host, repo.Directory);
            IReadOnlyList<string>? saved = null;
            host.OnSave = () => saved = vm.CollapsedLocationNames.ToArray();
            await vm.EnsureLoadedAsync();

            Assert.Contains(vm.Locations, item => item.Key == "b:feature/a");
            Assert.Contains(vm.Locations, item => item.Key == "t:v1");
            Assert.True(vm.Locations.Single(item => item.Key == "g:b:feature").IsExpanded);

            var view = new RepositoryView { DataContext = vm, Width = 1100, Height = 700 };
            var window = new Window { Content = view, Width = 1100, Height = 700 };
            window.Show();
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var expand = view.FindControl<Button>("ExpandLocationsButton");
            var collapse = view.FindControl<Button>("CollapseLocationsButton");
            var filter = view.FindControl<TextBox>("LocationFilterBox");
            Assert.NotNull(expand);
            Assert.NotNull(collapse);
            Assert.NotNull(filter);
            Assert.True(expand.IsEffectivelyVisible);
            Assert.True(collapse.IsEffectivelyVisible);
            Assert.True(filter.IsVisible);
            Assert.Same(vm.ExpandAllLocationsCommand, expand.Command);
            Assert.Same(vm.CollapseAllLocationsCommand, collapse.Command);
            Assert.Equal("Expand all", ToolTip.GetTip(expand));
            Assert.Equal("Collapse all", ToolTip.GetTip(collapse));
            Assert.Equal("Expand all", AutomationProperties.GetName(expand));
            Assert.Equal("Collapse all", AutomationProperties.GetName(collapse));
            Assert.True(expand.Bounds.Width is >= 16 and <= 22, $"expand width {expand.Bounds.Width}");
            Assert.True(expand.Bounds.Height is >= 16 and <= 22, $"expand height {expand.Bounds.Height}");
            Assert.True(collapse.Bounds.Width is >= 16 and <= 22, $"collapse width {collapse.Bounds.Width}");
            Assert.True(collapse.Bounds.Height is >= 16 and <= 22, $"collapse height {collapse.Bounds.Height}");

            var expandAt = Origin(expand, view);
            var collapseAt = Origin(collapse, view);
            var filterAt = Origin(filter, view);
            var collapseRight = collapse.TranslatePoint(new Point(collapse.Bounds.Width, 0), view);
            Assert.NotNull(collapseRight);
            Assert.True(expandAt.X < collapseAt.X, "Expand all is left of Collapse all");
            Assert.True(collapseRight.Value.X <= filterAt.X + 0.5, "Collapse all is left of the filter");
            Assert.True(Math.Abs(expandAt.Y - filterAt.Y) < 16, "Expand all shares the filter row");
            Assert.True(Math.Abs(collapseAt.Y - filterAt.Y) < 16, "Collapse all shares the filter row");

            Click(window, collapse);
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(vm.Locations, item => item.Key is "b:feature/a" or "b:feature/b" or "g:b:feature" or "t:v1" or "r:origin/side");
            Assert.DoesNotContain(vm.Locations, item => item.Key.StartsWith("s:", StringComparison.Ordinal));
            Assert.Contains(vm.Locations, item => item.Key == "h:branches");
            Assert.Contains(vm.Locations, item => item.Key == "h:tags");
            Assert.Contains(vm.Locations, item => item.Key == "h:stashes");
            Assert.Contains(vm.Locations, item => item.Key == "h:submodules");
            Assert.NotNull(saved);
            var collapsed = saved.ToArray();
            Assert.Contains("h:branches", collapsed);
            Assert.Contains("h:tags", collapsed);
            Assert.Contains("h:stashes", collapsed);
            Assert.Contains("h:remote:origin", collapsed);
            Assert.Contains("b:feature", collapsed);
            Assert.DoesNotContain("h:submodules", collapsed);

            vm.ToggleLocation(Header(vm, "h:branches"));
            Assert.Contains(vm.Locations, item => item.Key == "g:b:feature");
            Assert.False(vm.Locations.Single(item => item.Key == "g:b:feature").IsExpanded);
            Assert.DoesNotContain(vm.Locations, item => item.Key == "b:feature/a");
            Assert.Contains("b:feature", vm.CollapsedLocationNames);

            Click(window, expand);
            view.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(vm.Locations, item => item.Key == "b:feature/a");
            Assert.Contains(vm.Locations, item => item.Key == "t:v1");
            Assert.Contains(vm.Locations, item => item.Key.StartsWith("s:", StringComparison.Ordinal));
            Assert.Empty(vm.CollapsedLocationNames);
            Assert.Empty(saved);

            vm.LocationFilter = "v1";
            Click(window, collapse);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(vm.Locations, item => item.Key == "t:v1");
            vm.LocationFilter = "";
            Assert.DoesNotContain(vm.Locations, item => item.Key == "t:v1");
            Assert.DoesNotContain(vm.Locations, item => item.Key == "b:feature/a");

            var again = new RepositoryViewModel(host, repo.Directory);
            again.UseCollapsedLocations(collapsed);
            await again.EnsureLoadedAsync();
            Assert.False(Header(again, "h:tags").IsExpanded);
            Assert.False(Header(again, "h:branches").IsExpanded);
            Assert.DoesNotContain(again.Locations, item => item.Key is "g:b:feature" or "b:feature/a" or "t:v1");
            Assert.Contains(again.Locations, item => item.Key == "h:submodules");

            window.Close();
            await vm.DisposeAsync();
            await again.DisposeAsync();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task A_single_slashed_branch_is_a_folder_under_branches_and_origin()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "root\n");
        repo.CommitAll("root");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "fix/create");
        repo.WriteFile("a.txt", "fix\n");
        repo.CommitAll("fix");
        var tip = repo.RunCapture("rev-parse", "HEAD").Trim();
        repo.Run("update-ref", "refs/remotes/origin/fix/create", tip);
        repo.Run("switch", "-c", "topic/only/leaf");
        repo.Run("tag", "release/1.2");
        repo.Run("switch", trunk);

        await session.Dispatch(async () =>
        {
            var vm = new RepositoryViewModel(new LocationsHost(repo.Git), repo.Directory);
            await vm.EnsureLoadedAsync();

            var localFolder = vm.Locations.Single(item => item.Key == "g:b:fix");
            Assert.Equal("fix (1)", localFolder.Label);
            Assert.Equal(LocationIcons.Folder, localFolder.IconName);
            Assert.True(localFolder.IsExpanded);
            var local = vm.Locations.Single(item => item.Key == "b:fix/create");
            Assert.Equal("create", local.Label);
            Assert.True(local.Depth > localFolder.Depth);
            Assert.Contains(vm.Locations, item => item.Key == "b:" + trunk && item.Label == trunk);

            var topic = vm.Locations.Single(item => item.Key == "g:b:topic");
            var only = vm.Locations.Single(item => item.Key == "g:b:topic/only");
            var leaf = vm.Locations.Single(item => item.Key == "b:topic/only/leaf");
            Assert.Equal("topic (1)", topic.Label);
            Assert.Equal("only (1)", only.Label);
            Assert.Equal("leaf", leaf.Label);
            Assert.Equal(LocationIcons.Folder, topic.IconName);
            Assert.Equal(LocationIcons.Folder, only.IconName);
            Assert.True(topic.Depth < only.Depth && only.Depth < leaf.Depth);

            var remoteFolder = vm.Locations.Single(item => item.Key == "g:r:origin:fix");
            Assert.Equal("fix (1)", remoteFolder.Label);
            Assert.Equal(LocationIcons.Folder, remoteFolder.IconName);
            var remote = vm.Locations.Single(item => item.Key == "r:origin/fix/create");
            Assert.Equal("create", remote.Label);
            Assert.True(remote.Depth > remoteFolder.Depth);

            var tag = vm.Locations.Single(item => item.Key == "t:release/1.2");
            Assert.Equal("release/1.2", tag.Label);
            Assert.DoesNotContain(vm.Locations, item => item.Key == "g:t:release");

            await vm.DisposeAsync();
        }, CancellationToken.None);
    }

    private static Point Origin(Control control, Visual relative)
    {
        var point = control.TranslatePoint(default, relative);
        Assert.NotNull(point);
        return point.Value;
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
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

        public Action? OnSave { get; set; }

        public void Save() => OnSave?.Invoke();

        public Task OpenRepositoryAsync(string path) => Task.CompletedTask;
    }
}
