using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Sextant.Docking;
using Sextant.Git;
using Sextant.Git.Models;
using Sextant.Git.Repo;
using Sextant.Git.Workspace;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class DockLayoutTests
{
    [Fact]
    public void Default_layout_is_locations_history_changes_and_the_diff()
    {
        using var vm = Open(new WorkspaceState());
        Assert.Equal(
            [DockIds.Locations, DockIds.History, DockIds.Files, DockIds.Changes, DockIds.Staging, DockIds.Diff, DockIds.Blame, DockIds.Locks],
            vm.View.Dock.PaneOrder());
        Assert.True(vm.View.Dock.SharesHost(DockIds.History, DockIds.Files));
        Assert.False(vm.View.Dock.SharesHost(DockIds.History, DockIds.Locks));
        Assert.True(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Locks));
        Assert.True(vm.View.Dock.SharesHost(DockIds.Blame, DockIds.Locks));
        Assert.True(vm.View.Dock.IsSelected(DockIds.History));
        Assert.True(vm.View.Dock.IsSelected(DockIds.Diff));
        Assert.False(vm.View.Dock.IsSelected(DockIds.Locks));
        Assert.True(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Blame));
    }

    [Fact]
    public void Reset_restores_the_default_tools()
    {
        using var vm = Open(new WorkspaceState());
        vm.View.Dock.SetOpen(DockIds.Files, false);
        vm.View.Dock.SetOpen(DockIds.Locks, false);
        vm.View.Dock.SetOpen(DockIds.Commands, true);
        Assert.False(vm.View.Dock.IsOpen(DockIds.Files));
        Assert.False(vm.View.Dock.IsOpen(DockIds.Locks));
        Assert.False(vm.View.Dock.SharesHost(DockIds.History, DockIds.Files));
        Assert.False(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Locks));
        Assert.True(vm.View.Dock.IsOpen(DockIds.Commands));

        vm.View.Dock.Reset();

        Assert.Equal(
            [DockIds.Locations, DockIds.History, DockIds.Files, DockIds.Changes, DockIds.Staging, DockIds.Diff, DockIds.Blame, DockIds.Locks],
            vm.View.Dock.PaneOrder());
        Assert.True(vm.View.Dock.SharesHost(DockIds.History, DockIds.Files));
        Assert.False(vm.View.Dock.SharesHost(DockIds.History, DockIds.Locks));
        Assert.True(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Locks));
        Assert.True(vm.View.Dock.SharesHost(DockIds.Blame, DockIds.Locks));
        Assert.True(vm.View.Dock.IsSelected(DockIds.History));
        Assert.True(vm.View.Dock.IsSelected(DockIds.Diff));
        Assert.False(vm.View.Dock.IsSelected(DockIds.Locks));
        Assert.True(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Blame));
        Assert.False(vm.View.Dock.IsOpen(DockIds.Commands));
    }

    [Fact]
    public async Task Older_workspace_seeds_a_layout_and_keeps_hidden_branches()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sextant-dock-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "repo");
            var state = new WorkspaceState
            {
                ActiveTab = path,
                LocationsWidth = 180,
                GraphWidth = 260,
                FilesHeight = 90,
                RepoLayouts =
                {
                    [path] = new RepoLayout
                    {
                        ShowLocations = false,
                        LocationsWidth = 300,
                        GraphWidth = 400,
                        FilesHeight = 100,
                        HiddenBranches = ["refs/heads/secret"],
                    },
                },
            };
            var store = new WorkspaceStore(directory);
            var vm = new MainViewModel(store, state, new AppSettings(), new GitProcessRunner());
            Assert.Equal(
                [DockIds.History, DockIds.Files, DockIds.Changes, DockIds.Staging, DockIds.Diff, DockIds.Blame, DockIds.Locks],
                vm.Dock.PaneOrder());
            Assert.True(vm.Dock.SharesHost(DockIds.History, DockIds.Files));
            Assert.True(vm.Dock.SharesHost(DockIds.Diff, DockIds.Locks));
            Assert.False(vm.Dock.IsOpen(DockIds.Locations));

            vm.Save();
            var loaded = store.LoadWorkspace();
            Assert.NotNull(loaded.DockLayout);
            Assert.True(DockLayouts.IsUsable(loaded.DockLayout));
            Assert.Equal(["refs/heads/secret"], loaded.RepoLayouts[path].HiddenBranches);
            await vm.Shutdown();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Closing_the_diff_leaves_the_other_tools_open()
    {
        using var vm = Open(new WorkspaceState());
        vm.View.Dock.SetOpen(DockIds.Diff, false);
        Assert.Equal(
            [DockIds.Locations, DockIds.History, DockIds.Files, DockIds.Changes, DockIds.Staging, DockIds.Blame, DockIds.Locks],
            vm.View.Dock.PaneOrder());
        Assert.False(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Blame));
        Assert.True(vm.View.Dock.SharesHost(DockIds.Blame, DockIds.Locks));
    }

    [Fact]
    public void Closing_a_tool_from_its_chrome_and_choosing_it_again_shows_it()
    {
        using var vm = Open(new WorkspaceState());
        var dock = vm.View.Dock;
        foreach (var id in new[]
        {
            DockIds.Locations, DockIds.History, DockIds.Files, DockIds.Changes,
            DockIds.Staging, DockIds.Diff, DockIds.Blame, DockIds.Locks,
        })
        {
            var tool = SextantDockFactory.Collect(dock.Root).OfType<RepositoryTool>().Single(item => item.ToolId == id);
            dock.Factory.CloseDockable(tool);
            Assert.False(dock.IsOpen(id));

            dock.Show(id);
            Assert.True(dock.IsOpen(id), id);
            Assert.True(dock.IsSelected(id), id);
        }
    }

    [Fact]
    public async Task Saved_layout_without_locks_shows_that_pane_from_the_window_menu()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-dock-saved-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var state = new WorkspaceState
                {
                    DockLayout = new DockLayout
                    {
                        Schema = DockLayouts.CurrentSchema,
                        Root = new DockSplit
                        {
                            Orientation = "horizontal",
                            Slots =
                            [
                                new DockSlot { Id = DockIds.Locations, Proportion = 0.16 },
                                new DockSlot
                                {
                                    Proportion = 0.28,
                                    Split = new DockSplit
                                    {
                                        Orientation = "vertical",
                                        Slots =
                                        [
                                            new DockSlot { Id = DockIds.Changes, Proportion = 0.23 },
                                            new DockSlot
                                            {
                                                Proportion = 0.77,
                                                Tabs = [DockIds.History, DockIds.Files],
                                                Active = DockIds.History,
                                            },
                                        ],
                                    },
                                },
                                new DockSlot
                                {
                                    Proportion = 0.56,
                                    Split = new DockSplit
                                    {
                                        Orientation = "vertical",
                                        Slots =
                                        [
                                            new DockSlot { Id = DockIds.Staging, Proportion = 0.25 },
                                            new DockSlot
                                            {
                                                Proportion = 0.75,
                                                Tabs = [DockIds.Diff, DockIds.Blame],
                                                Active = DockIds.Diff,
                                            },
                                        ],
                                    },
                                },
                            ],
                        },
                    },
                };
                var vm = new MainViewModel(new WorkspaceStore(directory), state, new AppSettings(), new GitProcessRunner());
                vm.Tabs.Add(new RepositoryViewModel(vm, Path.GetTempPath()));
                vm.ActiveTab = vm.Tabs[0];
                var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Assert.False(vm.Dock.IsOpen(DockIds.Locks));
                vm.ShowLfsLocksCommand.Execute(null);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.True(vm.Dock.IsOpen(DockIds.Locks));
                Assert.True(vm.Dock.IsSelected(DockIds.Locks));
                AssertVisible(window, DockIds.Locks, "saved");

                var diff = SextantDockFactory.Collect(vm.Dock.Root).OfType<RepositoryTool>().Single(item => item.ToolId == DockIds.Diff);
                vm.Dock.Factory.CloseDockable(diff);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                vm.ShowDiffCommand.Execute(null);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.True(vm.Dock.IsOpen(DockIds.Diff));
                AssertVisible(window, DockIds.Diff, "saved-diff");

                await vm.Shutdown();
                window.DataContext = null;
                window.Close();
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
    public async Task Closed_pane_comes_back_from_the_window_menu()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-dock-reshow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var vm = new MainViewModel(new WorkspaceStore(directory), new WorkspaceState(), new AppSettings(), new GitProcessRunner());
                var tab = new RepositoryViewModel(vm, Path.GetTempPath());
                vm.Tabs.Add(tab);
                vm.ActiveTab = tab;
                var window = new MainWindow { DataContext = vm, Width = 1400, Height = 900 };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var menu = NativeMenu.GetMenu(window);
                var view = menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_View");
                var panes = view.Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_Window");
                var blameItem = panes.Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_Blame");
                Assert.NotNull(blameItem.Command);
                Assert.True(blameItem.Command!.CanExecute(blameItem.CommandParameter));

                foreach (var (id, command) in new (string Id, System.Windows.Input.ICommand Command)[]
                {
                    (DockIds.Blame, vm.ShowBlameCommand),
                    (DockIds.Locks, vm.ShowLfsLocksCommand),
                    (DockIds.Files, vm.ShowFilesCommand),
                    (DockIds.Locations, vm.ShowLocationsCommand),
                    (DockIds.History, vm.ShowHistoryCommand),
                    (DockIds.Changes, vm.ShowChangesCommand),
                    (DockIds.Staging, vm.ShowStagingCommand),
                    (DockIds.Diff, vm.ShowDiffCommand),
                })
                {
                    Assert.True(HasTool(window, id), $"before {id}");
                    var tool = SextantDockFactory.Collect(vm.Dock.Root).OfType<RepositoryTool>().Single(item => item.ToolId == id);
                    vm.Dock.Factory.CloseDockable(tool);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(vm.Dock.IsOpen(id), $"closed {id} dockOpen={tool.IsOpen} state={tool.DockingState} owner={tool.Owner?.GetType().Name}");
                    Assert.False(HasTool(window, id), $"closed view {id}");

                    Assert.True(command.CanExecute(null), id);
                    command.Execute(null);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(vm.Dock.IsOpen(id), $"menu {id} dockOpen={tool.IsOpen} state={tool.DockingState} hidden={vm.Dock.Root?.HiddenDockables?.Count}");
                    Assert.True(vm.Dock.IsSelected(id), $"selected {id}");
                    AssertVisible(window, id, "shown");
                }

                var floated = SextantDockFactory.Collect(vm.Dock.Root).OfType<RepositoryTool>().Single(item => item.ToolId == DockIds.Staging);
                vm.Dock.Factory.FloatDockable(floated);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.True(vm.Dock.IsOpen(DockIds.Staging));
                var host = vm.Dock.Root?.Windows?.LastOrDefault()?.Host as Window;
                Assert.NotNull(host);
                host.Close();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                vm.ShowStagingCommand.Execute(null);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.True(vm.Dock.IsOpen(DockIds.Staging));
                Assert.True(floated.Owner is not null && (double.IsNaN(floated.Owner.Proportion) || floated.Owner.Proportion > 0.02),
                    $"proportion {floated.Owner?.Proportion}");
                AssertVisible(window, DockIds.Staging, "floated");

                await vm.Shutdown();
                window.DataContext = null;
                window.Close();
                return 0;
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static bool HasTool(Window window, string id) =>
        window.GetVisualDescendants().OfType<Control>().Any(control => control.DataContext is RepositoryTool item && item.ToolId == id);

    private static void AssertVisible(Window window, string id, string when)
    {
        var matches = window.GetVisualDescendants().OfType<ContentControl>()
            .Where(control => control.Content is RepositoryTool item && item.ToolId == id)
            .Select(control => $"{control.GetType().Name} vis={control.IsEffectivelyVisible} {control.Bounds.Width:0.#}x{control.Bounds.Height:0.#}")
            .ToList();
        Assert.True(
            matches.Any(match => match.Contains("vis=True") && !match.Contains("x0") && !match.Contains(" 0x")),
            $"{when} {id} :: {(matches.Count == 0 ? "none" : string.Join(" | ", matches))}");
    }

    [Fact]
    public void Closing_blame_and_opening_it_again_puts_it_back_with_the_diff()
    {
        using var vm = Open(new WorkspaceState());
        vm.View.Dock.SetOpen(DockIds.Blame, false);
        Assert.False(vm.View.Dock.IsOpen(DockIds.Blame));
        Assert.True(vm.View.Dock.IsOpen(DockIds.Diff));

        vm.View.Dock.SetOpen(DockIds.Blame, true);

        Assert.Equal(
            [DockIds.Locations, DockIds.History, DockIds.Files, DockIds.Changes, DockIds.Staging, DockIds.Diff, DockIds.Blame, DockIds.Locks],
            vm.View.Dock.PaneOrder());
        Assert.True(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Blame));
        Assert.True(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Locks));
        Assert.True(vm.View.Dock.SharesHost(DockIds.History, DockIds.Files));
    }

    [Fact]
    public void Closing_repository_files_and_opening_them_again_puts_them_back_with_history()
    {
        using var vm = Open(new WorkspaceState());
        vm.View.Dock.SetOpen(DockIds.Files, false);
        Assert.False(vm.View.Dock.IsOpen(DockIds.Files));
        Assert.True(vm.View.Dock.IsOpen(DockIds.History));
        Assert.False(vm.View.Dock.SharesHost(DockIds.History, DockIds.Files));

        vm.View.Dock.SetOpen(DockIds.Files, true);

        Assert.True(vm.View.Dock.IsOpen(DockIds.History));
        Assert.True(vm.View.Dock.IsOpen(DockIds.Files));
        Assert.True(vm.View.Dock.SharesHost(DockIds.History, DockIds.Files));
        Assert.True(vm.View.Dock.IsSelected(DockIds.Files));
    }

    [Fact]
    public void Showing_a_pane_opens_it_selects_it_and_does_not_hide_it()
    {
        using var vm = Open(new WorkspaceState());
        var tab = new RepositoryViewModel(vm.View, Path.GetTempPath());
        vm.View.Tabs.Add(tab);
        vm.View.ActiveTab = tab;

        Assert.True(vm.View.Dock.IsSelected(DockIds.Diff));
        vm.View.ShowBlameCommand.Execute(null);
        Assert.True(vm.View.Dock.IsOpen(DockIds.Diff));
        Assert.True(vm.View.Dock.IsOpen(DockIds.Blame));
        Assert.True(vm.View.Dock.IsSelected(DockIds.Blame));
        Assert.False(vm.View.Dock.IsSelected(DockIds.Diff));
        vm.View.ShowBlameCommand.Execute(null);
        Assert.True(vm.View.Dock.IsOpen(DockIds.Blame));
        Assert.True(vm.View.Dock.IsSelected(DockIds.Blame));

        vm.View.ShowDiffCommand.Execute(null);
        Assert.True(vm.View.Dock.IsOpen(DockIds.Blame));
        Assert.True(vm.View.Dock.IsSelected(DockIds.Diff));

        vm.View.Dock.SetOpen(DockIds.Staging, false);
        Assert.False(vm.View.Dock.IsOpen(DockIds.Staging));
        vm.View.ShowStagingCommand.Execute(null);
        Assert.True(vm.View.Dock.IsOpen(DockIds.Staging));
        vm.View.ShowStagingCommand.Execute(null);
        Assert.True(vm.View.Dock.IsOpen(DockIds.Staging));

        Assert.False(vm.View.Dock.IsOpen(DockIds.Commands));
        vm.View.ShowCommandsCommand.Execute(null);
        Assert.True(vm.View.Dock.IsOpen(DockIds.Commands));
        Assert.True(tab.CommandsOpen);
        vm.View.ShowCommandsCommand.Execute(null);
        Assert.True(vm.View.Dock.IsOpen(DockIds.Commands));
        Assert.True(tab.CommandsOpen);

        vm.View.Dock.SetOpen(DockIds.Locks, false);
        Assert.False(vm.View.Dock.IsOpen(DockIds.Locks));
        Assert.True(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Blame));
        Assert.True(vm.View.Dock.SharesHost(DockIds.History, DockIds.Files));
        vm.View.ShowLfsLocksCommand.Execute(null);
        Assert.True(vm.View.Dock.IsOpen(DockIds.Locks));
        Assert.True(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Locks));
        Assert.True(vm.View.Dock.SharesHost(DockIds.Blame, DockIds.Locks));
        Assert.False(vm.View.Dock.SharesHost(DockIds.History, DockIds.Locks));
        Assert.True(vm.View.Dock.IsSelected(DockIds.Locks));
        vm.View.ShowLfsLocksCommand.Execute(null);
        Assert.True(vm.View.Dock.IsOpen(DockIds.Locks));

        vm.View.Dock.SetOpen(DockIds.Locations, false);
        Assert.False(tab.ShowLocations);
        vm.View.ShowLocationsCommand.Execute(null);
        Assert.True(vm.View.Dock.IsOpen(DockIds.Locations));
        Assert.True(tab.ShowLocations);
        vm.View.ShowLocationsCommand.Execute(null);
        Assert.True(tab.ShowLocations);
    }

    [Fact]
    public async Task View_menu_puts_the_panes_under_window()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-window-menu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner());
                var window = new MainWindow { DataContext = vm, Width = 800, Height = 600 };
                window.Show();

                var menu = NativeMenu.GetMenu(window);
                Assert.NotNull(menu);
                var view = menu.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_View");
                var panes = view.Menu!.Items.OfType<NativeMenuItem>().Single(item => item.Header == "_Window");
                var headers = panes.Menu!.Items.OfType<NativeMenuItem>().Select(item => item.Header).ToList();
                Assert.Equal(
                    [
                        "_Locations",
                        "_History",
                        "_Commit",
                        "Chan_ges",
                        "_Diff",
                        "_Blame",
                        "_Repository files",
                        "_LFS locks",
                        "Command _log",
                    ],
                    headers);
                Assert.DoesNotContain(headers, header => header?.Contains("Toggle", StringComparison.Ordinal) == true);
                var tools = SextantDockFactory.Collect(vm.Dock.Root).OfType<RepositoryTool>().ToList();
                Assert.Equal("Commit", tools.Single(tool => tool.ToolId == DockIds.Changes).Title);
                Assert.Equal("Changes", tools.Single(tool => tool.ToolId == DockIds.Staging).Title);
                Assert.Contains(view.Menu.Items.OfType<NativeMenuItem>(), item => item.Header == "_Save layout…");
                Assert.Contains(view.Menu.Items.OfType<NativeMenuItem>(), item => item.Header == "Restore _layout…");
                Assert.Contains(view.Menu.Items.OfType<NativeMenuItem>(), item => item.Header == "_Reset layout");
                Assert.DoesNotContain(view.Menu.Items.OfType<NativeMenuItem>(), item => item.Header == "_Locations");

                await vm.Shutdown();
                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Older_diff_slot_gains_a_blame_tab_once()
    {
        var state = new WorkspaceState
        {
            DockLayout = new DockLayout
            {
                Root = new DockSplit
                {
                    Orientation = "horizontal",
                    Slots =
                    [
                        new DockSlot { Id = DockIds.History, Proportion = 0.4 },
                        new DockSlot { Id = DockIds.Diff, Proportion = 0.6 },
                    ],
                },
            },
        };
        var layout = DockLayouts.Resolve(state);
        Assert.Same(state.DockLayout, layout);
        Assert.Equal(DockLayouts.CurrentSchema, layout.Schema);
        Assert.Equal([DockIds.Diff, DockIds.Blame], layout.Root!.Slots[1].Tabs);
        Assert.Equal(DockIds.Diff, layout.Root.Slots[1].Active);

        layout.Root.Slots[1].Tabs = null;
        layout.Root.Slots[1].Id = DockIds.Diff;
        layout.Root.Slots[1].Active = "";
        var again = DockLayouts.Resolve(state);
        Assert.Same(layout, again);
        Assert.Equal(DockIds.Diff, again.Root!.Slots[1].Id);
        Assert.Null(again.Root.Slots[1].Tabs);
    }

    [Fact]
    public void Saved_changes_pane_gains_a_staging_pane_once()
    {
        var state = new WorkspaceState
        {
            DockLayout = new DockLayout
            {
                Schema = 1,
                Root = new DockSplit
                {
                    Orientation = "vertical",
                    Slots =
                    [
                        new DockSlot { Id = DockIds.Changes, Proportion = 0.4 },
                        new DockSlot
                        {
                            Proportion = 0.6,
                            Tabs = [DockIds.Diff, DockIds.Blame],
                            Active = DockIds.Diff,
                        },
                    ],
                },
            },
        };
        var layout = DockLayouts.Resolve(state);
        Assert.Same(state.DockLayout, layout);
        Assert.Equal(DockLayouts.CurrentSchema, layout.Schema);
        Assert.Equal(DockIds.Changes, layout.Root!.Slots[0].Id);
        Assert.Equal(DockIds.Staging, layout.Root.Slots[1].Id);
        Assert.Equal([DockIds.Diff, DockIds.Blame], layout.Root.Slots[2].Tabs);
        Assert.True(layout.Root.Slots[0].Proportion < 0.4);
        Assert.True(layout.Root.Slots[1].Proportion > 0);

        var again = DockLayouts.Resolve(state);
        Assert.Equal(3, again.Root!.Slots.Count);
        Assert.Equal(DockIds.Staging, again.Root.Slots[1].Id);
    }

    [Fact]
    public void Pinned_changes_still_gains_a_staging_pane()
    {
        var state = new WorkspaceState
        {
            DockLayout = new DockLayout
            {
                Schema = 1,
                Root = new DockSplit
                {
                    Orientation = "horizontal",
                    Slots =
                    [
                        new DockSlot { Id = DockIds.History, Proportion = 0.4 },
                        new DockSlot
                        {
                            Proportion = 0.6,
                            Tabs = [DockIds.Diff, DockIds.Blame],
                            Active = DockIds.Diff,
                        },
                    ],
                },
                Pins = [new DockPin { Id = DockIds.Changes, Side = "right" }],
            },
        };
        var layout = DockLayouts.Resolve(state);
        Assert.Equal(DockIds.History, layout.Root!.Slots[0].Id);
        Assert.Equal(DockIds.Staging, layout.Root.Slots[1].Id);
        Assert.Equal([DockIds.Diff, DockIds.Blame], layout.Root.Slots[2].Tabs);
    }

    [Fact]
    public void A_closed_file_list_stays_closed()
    {
        var state = new WorkspaceState
        {
            DockLayout = new DockLayout
            {
                Schema = DockLayouts.CurrentSchema,
                Root = new DockSplit
                {
                    Orientation = "vertical",
                    Slots =
                    [
                        new DockSlot { Id = DockIds.Changes, Proportion = 0.3 },
                        new DockSlot
                        {
                            Proportion = 0.7,
                            Tabs = [DockIds.Diff, DockIds.Blame],
                            Active = DockIds.Diff,
                        },
                    ],
                },
            },
        };
        var layout = DockLayouts.Resolve(state);
        Assert.Equal(2, layout.Root!.Slots.Count);
        Assert.DoesNotContain(layout.Root.Slots, slot => slot.Id == DockIds.Staging);
    }

    [Fact]
    public void A_layout_without_the_diff_is_usable()
    {
        Assert.True(DockLayouts.IsUsable(new DockLayout
        {
            Root = new DockSplit
            {
                Orientation = "horizontal",
                Slots = [new DockSlot { Id = DockIds.History, Proportion = 1 }],
            },
        }));
    }

    [Fact]
    public void Older_document_id_is_read_as_the_diff()
    {
        var state = new WorkspaceState
        {
            DockLayout = new DockLayout
            {
                Root = new DockSplit
                {
                    Orientation = "horizontal",
                    Slots =
                    [
                        new DockSlot { Id = DockIds.History, Proportion = 0.4 },
                        new DockSlot { Id = DockIds.Documents, Proportion = 0.6 },
                    ],
                },
            },
        };
        var layout = DockLayouts.Resolve(state);
        Assert.Same(state.DockLayout, layout);
        Assert.Equal([DockIds.Diff, DockIds.Blame], layout.Root!.Slots[1].Tabs);
    }

    [Fact]
    public void Unusable_layout_falls_back_to_the_default()
    {
        var state = new WorkspaceState
        {
            DockLayout = new DockLayout
            {
                Root = new DockSplit
                {
                    Orientation = "horizontal",
                    Slots = [new DockSlot { Id = DockIds.Diff, Proportion = 0 }],
                },
            },
        };
        using var vm = Open(state);
        Assert.Equal(
            [DockIds.Locations, DockIds.History, DockIds.Files, DockIds.Changes, DockIds.Staging, DockIds.Diff, DockIds.Blame, DockIds.Locks],
            vm.View.Dock.PaneOrder());
        Assert.True(vm.View.Dock.SharesHost(DockIds.History, DockIds.Files));
        Assert.True(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Locks));
    }

    [Fact]
    public async Task Hidden_repository_is_not_refreshed_with_the_visible_one()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var left = new TempRepo();
        using var right = new TempRepo();
        left.WriteFile("a.txt", "one\n");
        left.CommitAll("left");
        right.WriteFile("b.txt", "one\n");
        right.CommitAll("right");
        var directory = Path.Combine(Path.GetTempPath(), "sextant-dock-visible-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner())
                {
                    GitExecutable = left.Git,
                    GitReady = true,
                };
                var first = new RepositoryViewModel(vm, left.Directory);
                var second = new RepositoryViewModel(vm, right.Directory);
                vm.Tabs.Add(first);
                vm.Tabs.Add(second);
                await vm.Activate(first);
                await vm.Activate(second);

                left.WriteFile("hidden.txt", "new\n");
                right.WriteFile("shown.txt", "new\n");
                await vm.RefreshShownAsync();

                Assert.DoesNotContain(first.Files, row => row.Path == "hidden.txt");
                Assert.Contains(second.Files, row => row.Path == "shown.txt");

                await vm.Shutdown();
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Digit_shortcut_follows_outer_tab_order()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var left = new TempRepo();
        using var right = new TempRepo();
        left.WriteFile("a.txt", "one\n");
        left.CommitAll("left");
        right.WriteFile("b.txt", "one\n");
        right.CommitAll("right");
        var directory = Path.Combine(Path.GetTempPath(), "sextant-dock-digit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner())
                {
                    GitExecutable = left.Git,
                    GitReady = true,
                };
                var first = new RepositoryViewModel(vm, left.Directory);
                var second = new RepositoryViewModel(vm, right.Directory);
                vm.Tabs.Add(first);
                vm.Tabs.Add(second);
                vm.ActiveTab = second;

                await vm.ActivateDigit(1);
                Assert.Same(first, vm.ActiveTab);

                await vm.ActivateDigit(2);
                Assert.Same(second, vm.ActiveTab);

                await vm.Shutdown();
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Changes_pane_stays_tall_enough_for_the_commit_box()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("readme.txt", "hello\n");
        repo.CommitAll("first");
        var directory = Path.Combine(Path.GetTempPath(), "sextant-changes-min-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner())
                {
                    GitExecutable = repo.Git,
                    GitReady = true,
                };
                var host = ChangesHost(vm);
                Assert.Equal(RepositoryDock.ChangesPaneMinHeight, host.MinHeight);

                vm.Dock.SetOpen(DockIds.Changes, false);
                vm.Dock.SetOpen(DockIds.Changes, true);
                host = ChangesHost(vm);
                Assert.Equal(RepositoryDock.ChangesPaneMinHeight, host.MinHeight);

                var tab = new RepositoryViewModel(vm, repo.Directory);
                vm.Tabs.Add(tab);
                var window = new MainWindow { DataContext = vm, Width = 1400, Height = 1000 };
                window.Show();
                await vm.Activate(tab);

                ChangesToolView? changes = null;
                for (var i = 0; i < 40 && changes is null; i++)
                {
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    changes = window.GetVisualDescendants().OfType<ChangesToolView>().FirstOrDefault();
                    if (changes is null)
                        await Task.Delay(25);
                }

                Assert.NotNull(changes);
                host.Proportion = 0.001;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var box = changes.FindControl<TextBox>("CommitMessageInput")!;
                var commit = changes.GetVisualDescendants().OfType<SplitButton>().Single();
                var committer = changes.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("flat"));
                var pane = window.GetVisualDescendants().OfType<ToolDockControl>().Single(control => ReferenceEquals(control.DataContext, host));
                var tool = changes.FindAncestorOfType<ToolControl>();
                var chrome = changes.FindAncestorOfType<ToolChromeControl>();
                Assert.NotNull(tool);
                Assert.NotNull(chrome);
                Assert.Equal(0, tool.MinHeight);
                Assert.Equal(0, chrome.MinHeight);
                Assert.True(pane.Bounds.Height + 1 >= host.MinHeight, $"pane {pane.Bounds.Height} min {host.MinHeight}");
                Assert.True(box.Bounds.Height + 0.5 >= box.MinHeight, $"box {box.Bounds.Height}");
                AssertInside(pane, box);
                AssertInside(pane, committer);
                AssertInside(pane, commit);

                var slack = box.Bounds.Height - box.MinHeight;
                var floor = pane.Bounds.Height - slack;
                Assert.InRange(host.MinHeight, floor - 1, floor + 8);

                await vm.Shutdown();
                window.Close();
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Tool_headers_are_semibold_and_dock_type_is_larger()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            var app = Application.Current;
            Assert.NotNull(app);
            DockChrome.Ensure(app);
            var factory = new Factory();
            var tool = new Tool { Id = "locations", Title = "Locations" };
            var host = new ToolDock
            {
                VisibleDockables = factory.CreateList<IDockable>(tool),
                ActiveDockable = tool,
            };
            tool.Owner = host;
            var chrome = new ToolChromeControl { DataContext = host, Width = 320, Height = 180 };
            var tab = new ToolTabStripItem { DataContext = tool, Width = 140, Height = 28 };
            var window = new Window
            {
                Width = 420,
                Height = 320,
                Content = new StackPanel { Children = { chrome, tab } },
            };
            window.Show();
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var title = chrome.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "PART_Title");
                Assert.Equal("Locations", title.Text);
                Assert.Equal(14, title.FontSize);
                Assert.Equal(FontWeight.SemiBold, title.FontWeight);
                Assert.Equal(14, tab.FontSize);
                Assert.Equal(FontWeight.Normal, tab.FontWeight);
            }
            finally
            {
                window.Close();
            }

            return 0;
        }, CancellationToken.None);
    }

    [Fact]
    public void Layout_file_migrates_an_older_diff_and_rejects_a_broken_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sextant-layout-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var older = Path.Combine(directory, "old.sextant-layout");
            File.WriteAllText(older, """
                {
                  "schema": 0,
                  "root": {
                    "orientation": "horizontal",
                    "slots": [
                      { "id": "history", "proportion": 0.5 },
                      { "id": "documents", "proportion": 0.5 }
                    ]
                  }
                }
                """);
            Assert.True(DockLayoutFile.TryRead(older, out var layout, out var error));
            Assert.Null(error);
            Assert.NotNull(layout);
            Assert.Equal(DockLayouts.CurrentSchema, layout.Schema);
            Assert.Equal([DockIds.Diff, DockIds.Blame], layout.Root!.Slots[1].Tabs);

            using var host = Open(new WorkspaceState());
            Assert.True(host.View.Dock.TryRestore(layout));
            Assert.True(host.View.Dock.IsOpen(DockIds.History));
            Assert.True(host.View.Dock.SharesHost(DockIds.Diff, DockIds.Blame));
            Assert.False(host.View.Dock.IsOpen(DockIds.Locations));
            Assert.False(host.View.Dock.IsOpen(DockIds.Commands));

            var broken = Path.Combine(directory, "broken.json");
            File.WriteAllText(broken, "{ \"openTabs\": [] }");
            Assert.False(DockLayoutFile.TryRead(broken, out _, out error));
            Assert.Equal("That file is not a Sextant layout.", error);
            File.WriteAllText(broken, "not json");
            Assert.False(DockLayoutFile.TryRead(broken, out _, out error));
            Assert.Equal("That file is not a Sextant layout.", error);
            Assert.False(DockLayoutFile.TryRead(Path.Combine(directory, "missing.sextant-layout"), out _, out error));
            Assert.Equal("Sextant could not read that file.", error);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Saved_layout_file_restores_that_arrangement()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        var directory = Path.Combine(Path.GetTempPath(), "sextant-layout-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Layout.sextant-layout");
        try
        {
            await session.Dispatch(async () =>
            {
                var store = new WorkspaceStore(directory);
                var vm = new MainViewModel(store, store.LoadWorkspace(), new AppSettings(), new GitProcessRunner());
                var dialogs = new LayoutDialogs(path);
                vm.Attach(dialogs);
                var tab = new RepositoryViewModel(vm, Path.Combine(directory, "repo"));
                vm.Tabs.Add(tab);
                vm.ActiveTab = tab;
                vm.Dock.SetOpen(DockIds.Files, false);
                vm.Dock.SetOpen(DockIds.Locations, false);
                vm.Dock.SetOpen(DockIds.Commands, true);

                await vm.SaveLayoutCommand.ExecuteAsync(null);
                Assert.Null(dialogs.Notice);
                var saved = File.ReadAllText(path);
                Assert.Contains("\"schema\": 2", saved, StringComparison.Ordinal);
                Assert.Contains("\"commands\"", saved, StringComparison.Ordinal);
                Assert.DoesNotContain("\"files\"", saved, StringComparison.Ordinal);

                vm.Dock.Reset();
                Assert.True(vm.Dock.IsOpen(DockIds.Files));
                Assert.True(vm.Dock.IsOpen(DockIds.Locations));
                Assert.False(vm.Dock.IsOpen(DockIds.Commands));

                dialogs.OpenPath = null;
                await vm.RestoreLayoutCommand.ExecuteAsync(null);
                Assert.Null(dialogs.Notice);
                Assert.True(vm.Dock.IsOpen(DockIds.Files));

                dialogs.OpenPath = Path.Combine(directory, "notes.txt");
                File.WriteAllText(dialogs.OpenPath, "hello");
                await vm.RestoreLayoutCommand.ExecuteAsync(null);
                Assert.Equal("That file is not a Sextant layout.", dialogs.Notice);
                Assert.True(vm.Dock.IsOpen(DockIds.Files));
                Assert.False(vm.Dock.IsOpen(DockIds.Commands));

                dialogs.Notice = null;
                dialogs.OpenPath = path;
                await vm.RestoreLayoutCommand.ExecuteAsync(null);
                Assert.Null(dialogs.Notice);
                Assert.False(vm.Dock.IsOpen(DockIds.Files));
                Assert.False(vm.Dock.IsOpen(DockIds.Locations));
                Assert.True(vm.Dock.IsOpen(DockIds.Commands));

                var again = new MainViewModel(new WorkspaceStore(directory), new WorkspaceStore(directory).LoadWorkspace(), new AppSettings(), new GitProcessRunner());
                Assert.False(again.Dock.IsOpen(DockIds.Files));
                Assert.False(again.Dock.IsOpen(DockIds.Locations));
                Assert.True(again.Dock.IsOpen(DockIds.Commands));

                await again.Shutdown();
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

    private sealed class LayoutDialogs : IDialogService
    {
        private readonly string _savePath;

        public LayoutDialogs(string path)
        {
            _savePath = path;
            OpenPath = path;
        }

        public string? OpenPath { get; set; }

        public string? Notice { get; set; }

        public Task<string?> PickFolderAsync(string title, string? startDirectory = null) => throw new NotSupportedException();

        public Task<string?> PickGitExecutableAsync() => throw new NotSupportedException();

        public Task<bool> ConfirmAsync(string title, string message, string confirm = "OK") => throw new NotSupportedException();

        public Task<string?> PromptAsync(string title, string message, string initial = "", bool allowEmpty = false) => throw new NotSupportedException();

        public Task<string?> PromptSecretAsync(string title, string message) => throw new NotSupportedException();

        public Task<string?> SaveFileAsync(string title, string suggestedName) => throw new NotSupportedException();

        public Task<string?> SaveAsAsync(string title, string suggestedName, string typeName, IReadOnlyList<string> patterns, string defaultExtension)
        {
            Assert.Equal("Save layout", title);
            Assert.Equal(DockLayoutFile.SuggestedName, suggestedName);
            Assert.Equal(DockLayoutFile.TypeName, typeName);
            Assert.Contains("*.sextant-layout", patterns);
            Assert.Equal(DockLayoutFile.Extension, defaultExtension);
            return Task.FromResult<string?>(_savePath);
        }

        public Task<string?> PickFileAsync(string title, string typeName, IReadOnlyList<string> patterns)
        {
            Assert.Equal("Restore layout", title);
            Assert.Equal(DockLayoutFile.TypeName, typeName);
            Assert.Contains("*.sextant-layout", patterns);
            Assert.Contains("*.json", patterns);
            return Task.FromResult(OpenPath);
        }

        public Task<CloneRequest?> PromptCloneAsync() => throw new NotSupportedException();

        public Task<string?> PickAsync(string title, string message, IReadOnlyList<string> options) => throw new NotSupportedException();

        public Task<PerformanceChoice?> ConfirmPerformanceAsync(PerformanceSuggestion suggestion) => throw new NotSupportedException();

        public Task<IReadOnlyList<RebaseStep>?> EditRebaseAsync(IReadOnlyList<RebaseStep> steps) => throw new NotSupportedException();

        public Task<SettingsDraft?> EditSettingsAsync(SettingsDraft current) => throw new NotSupportedException();

        public Task ShowAboutAsync() => throw new NotSupportedException();

        public Task ShowNoticeAsync(string title, string message)
        {
            Assert.Equal("Restore layout", title);
            Notice = message;
            return Task.CompletedTask;
        }

        public Task CopyAsync(string text) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Shift_escape_focuses_the_commit_message()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("readme.txt", "hello\n");
        repo.CommitAll("first");
        var directory = Path.Combine(Path.GetTempPath(), "sextant-commit-focus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var vm = new MainViewModel(new WorkspaceStore(directory), new WorkspaceState(), new AppSettings(), new GitProcessRunner())
                {
                    GitExecutable = repo.Git,
                    GitReady = true,
                };
                var tab = new RepositoryViewModel(vm, repo.Directory);
                vm.Tabs.Add(tab);
                var window = new MainWindow { DataContext = vm, Width = 1200, Height = 800 };
                window.Show();
                await vm.Activate(tab);
                var until = DateTime.UtcNow.AddSeconds(5);
                while (tab.IsBusy && DateTime.UtcNow < until)
                    await Task.Delay(30);
                Assert.False(tab.IsBusy);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var graph = Graph(window);
                graph.Focusable = true;
                Assert.True(graph.Focus(), $"graph visible {graph.IsEffectivelyVisible} enabled {graph.IsEnabled}");
                Assert.False(CommitMessage(window)!.IsFocused);

                window.KeyPress(Key.Escape, RawInputModifiers.Shift, PhysicalKey.Escape, null);
                var box = await WaitForCommitFocus(window);
                Assert.True(vm.Dock.IsOpen(DockIds.Changes));
                Assert.True(vm.Dock.IsSelected(DockIds.Changes));
                Assert.True(box.IsFocused);

                vm.Dock.SetOpen(DockIds.Changes, false);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.False(vm.Dock.IsOpen(DockIds.Changes));
                graph = Graph(window);
                graph.Focusable = true;
                Assert.True(graph.Focus(), $"graph visible {graph.IsEffectivelyVisible} enabled {graph.IsEnabled}");
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.False(vm.Dock.IsOpen(DockIds.Changes));

                window.KeyPress(Key.Escape, RawInputModifiers.Shift, PhysicalKey.Escape, null);
                box = await WaitForCommitFocus(window);
                Assert.True(vm.Dock.IsOpen(DockIds.Changes));
                Assert.True(vm.Dock.IsSelected(DockIds.Changes));
                Assert.True(box.IsEffectivelyVisible);
                Assert.True(box.IsFocused);

                graph = Graph(window);
                var commit = tab.Rows.First(row => !row.IsWorkingCopy && row.Subject == "first");
                tab.SelectedGraphRow = commit;
                var shown = DateTime.UtcNow.AddSeconds(5);
                while (tab.ShowingWorkingCopy && DateTime.UtcNow < shown)
                    await Task.Delay(20);
                Assert.False(tab.ShowingWorkingCopy);
                for (var i = 0; i < 40; i++)
                    tab.Rows.Add(new GraphRowViewModel { Subject = "pad " + i });
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var scroll = graph.GetVisualDescendants().OfType<ScrollViewer>().First();
                scroll.Offset = new Vector(0, 2000);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.True(scroll.Offset.Y > 40, $"offset {scroll.Offset.Y} extent {scroll.Extent.Height} viewport {scroll.Viewport.Height}");

                graph.Focusable = true;
                Assert.True(graph.Focus());
                await Task.Delay(600);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Same(commit, tab.SelectedGraphRow);
                Assert.False(tab.ShowingWorkingCopy);
                Assert.True(scroll.Offset.Y > 40, $"plain escape moved the history list to {scroll.Offset.Y}");

                window.KeyPress(Key.Escape, RawInputModifiers.Shift, PhysicalKey.Escape, null);
                box = await WaitForCommitFocus(window);
                Assert.True(tab.SelectedGraphRow!.IsWorkingCopy);
                Assert.True(tab.ShowingWorkingCopy);
                Assert.True(scroll.Offset.Y < 1, $"history offset {scroll.Offset.Y}");
                Assert.True(box.IsFocused);

                await vm.Shutdown();
                window.DataContext = null;
                window.Close();
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
    public async Task Double_escape_activates_tabs_and_focuses_the_commit_message()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var repo = new TempRepo();
        repo.WriteFile("a.txt", "root\n");
        repo.CommitAll("root");
        var trunk = repo.CurrentBranch();
        repo.Run("switch", "-c", "feature/grass");
        repo.WriteFile("c.txt", "feature\n");
        repo.CommitAll("feature-only");
        repo.Run("switch", trunk);
        var directory = Path.Combine(Path.GetTempPath(), "sextant-double-esc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await session.Dispatch(async () =>
            {
                var vm = new MainViewModel(new WorkspaceStore(directory), new WorkspaceState(), new AppSettings(), new GitProcessRunner())
                {
                    GitExecutable = repo.Git,
                    GitReady = true,
                };
                var tab = new RepositoryViewModel(vm, repo.Directory);
                vm.Tabs.Add(tab);
                var window = new MainWindow { DataContext = vm, Width = 1200, Height = 800 };
                window.Show();
                await vm.Activate(tab);
                await WaitIdle(tab);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var branch = tab.Locations.Single(item => item.Key == "b:feature/grass");
                await ((AsyncRelayCommand)branch.HideCommand).ExecuteAsync(null);
                await WaitIdle(tab);
                Assert.True(tab.Locations.Single(item => item.Key == "b:feature/grass").EyeHidden);
                Assert.DoesNotContain(tab.Rows, row => row.Subject == "feature-only");

                tab.LocationFilter = "zzz";
                tab.ShowHistorySearch = true;
                tab.HistoryText = "kept";
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var graph = Graph(window);
                graph.Focusable = true;
                Assert.True(graph.Focus());
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("zzz", tab.LocationFilter);
                Assert.Equal("kept", tab.HistoryText);
                Assert.True(tab.ShowHistorySearch);
                Assert.True(vm.Dock.IsSelected(DockIds.History));
                Assert.False(vm.Dock.IsSelected(DockIds.Blame));
                Assert.True(vm.Dock.IsOpen(DockIds.Changes));
                Assert.Contains("refs/heads/feature/grass", tab.HiddenBranchNames);

                await Task.Delay(600);
                vm.Dock.SetOpen(DockIds.Locks, false);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.False(vm.Dock.IsOpen(DockIds.Locks));
                var commit = tab.Rows.First(row => !row.IsWorkingCopy && row.Subject == "root");
                tab.SelectedGraphRow = commit;
                var shown = DateTime.UtcNow.AddSeconds(5);
                while (tab.ShowingWorkingCopy && DateTime.UtcNow < shown)
                    await Task.Delay(20);
                Assert.False(tab.ShowingWorkingCopy);
                graph = Graph(window);
                for (var i = 0; i < 40; i++)
                    tab.Rows.Add(new GraphRowViewModel { Subject = "pad " + i });
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var scroll = graph.GetVisualDescendants().OfType<ScrollViewer>().First();
                scroll.Offset = new Vector(0, 2000);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.True(scroll.Offset.Y > 40, $"offset {scroll.Offset.Y} extent {scroll.Extent.Height}");
                vm.Dock.Show(DockIds.Files);
                vm.Dock.Show(DockIds.Blame);
                Assert.True(vm.Dock.IsSelected(DockIds.Files));
                Assert.True(vm.Dock.IsSelected(DockIds.Blame));
                var arrangement = Arrangement(vm);

                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

                var until = DateTime.UtcNow.AddSeconds(8);
                TextBox? box = null;
                while (DateTime.UtcNow < until)
                {
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    box = CommitMessage(window);
                    if (!tab.IsBusy && tab.ShowingWorkingCopy
                        && tab.SelectedGraphRow?.IsWorkingCopy == true
                        && vm.Dock.IsSelected(DockIds.History)
                        && vm.Dock.IsSelected(DockIds.Diff)
                        && box is { IsFocused: true })
                        break;
                    await Task.Delay(20);
                }

                Assert.Equal(arrangement, Arrangement(vm));
                Assert.Equal("zzz", tab.LocationFilter);
                Assert.Equal("kept", tab.HistoryText);
                Assert.True(tab.ShowHistorySearch);
                Assert.False(tab.HasHistoryQuery);
                Assert.False(tab.ShowingBlame);
                Assert.True(tab.ShowingWorkingCopy);
                Assert.True(tab.SelectedGraphRow!.IsWorkingCopy);
                Assert.True(vm.Dock.IsSelected(DockIds.History));
                Assert.False(vm.Dock.IsSelected(DockIds.Files));
                Assert.True(vm.Dock.IsOpen(DockIds.Files));
                Assert.True(vm.Dock.IsSelected(DockIds.Diff));
                Assert.False(vm.Dock.IsSelected(DockIds.Blame));
                Assert.False(vm.Dock.IsOpen(DockIds.Locks));
                Assert.True(vm.Dock.IsOpen(DockIds.Changes));
                Assert.True(vm.Dock.IsSelected(DockIds.Changes));
                Assert.True(box!.IsFocused);
                graph = Graph(window);
                scroll = graph.GetVisualDescendants().OfType<ScrollViewer>().First();
                Assert.True(scroll.Offset.Y < 1, $"history offset {scroll.Offset.Y}");
                Assert.Contains("Hiding", tab.HistoryCaption);
                Assert.Contains("refs/heads/feature/grass", tab.HiddenBranchNames);
                Assert.DoesNotContain(tab.Rows, row => row.Subject == "feature-only");

                await vm.Shutdown();
                window.DataContext = null;
                window.Close();
                return 0;
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Which tools are open, and the split proportion of every dock node.</summary>
    private static string Arrangement(MainViewModel vm)
    {
        var open = string.Join(",", DockIds.Tools.Select(id => id + (vm.Dock.IsOpen(id) ? "+" : "-")));
        var proportions = string.Join(",", SextantDockFactory.Collect(vm.Dock.Root).Select(node => node.Id + ":" + node.Proportion.ToString("R")));
        return open + "|" + proportions;
    }

    private static async Task WaitIdle(RepositoryViewModel tab)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (tab.IsBusy && DateTime.UtcNow < until)
            await Task.Delay(30);
        Assert.False(tab.IsBusy);
    }

    private static ListBox Graph(Window window) =>
        window.GetVisualDescendants().OfType<ListBox>().First(list => list.Name == "GraphList");

    private static TextBox? CommitMessage(Window window) =>
        window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box => box.Name == "CommitMessageInput");

    private static async Task<TextBox> WaitForCommitFocus(Window window)
    {
        var until = DateTime.UtcNow.AddSeconds(2);
        TextBox? box = null;
        while (DateTime.UtcNow < until)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            box = CommitMessage(window);
            if (box is { IsFocused: true })
                return box;
            await Task.Delay(20);
        }

        Assert.Fail(box is null ? "The commit message was not shown." : "The commit message did not take focus.");
        return box!;
    }

    private static ToolDock ChangesHost(MainViewModel vm)
    {
        var tool = SextantDockFactory.Collect(vm.Dock.Root).OfType<RepositoryTool>().Single(item => item.ToolId == DockIds.Changes);
        return Assert.IsType<ToolDock>(tool.Owner);
    }

    private static void AssertInside(Control pane, Control item)
    {
        var origin = item.TranslatePoint(default, pane);
        Assert.NotNull(origin);
        Assert.True(origin.Value.Y >= -0.5, $"{item.GetType().Name} top {origin.Value.Y}");
        var bottom = origin.Value.Y + item.Bounds.Height;
        Assert.True(bottom <= pane.Bounds.Height + 1, $"{item.GetType().Name} bottom {bottom} pane {pane.Bounds.Height}");
    }

    [Fact]
    public void Focus_dock_opens_and_selects_a_closed_pane()
    {
        using var host = Open(new WorkspaceState());
        host.View.Dock.SetOpen(DockIds.Diff, false);
        Assert.False(host.View.Dock.IsSelected(DockIds.Diff));

        host.View.FocusDock(DockIds.Diff);

        Assert.True(host.View.Dock.IsOpen(DockIds.Diff));
        Assert.True(host.View.Dock.IsSelected(DockIds.Diff));
    }

    [Fact]
    public void Focus_dock_leaves_an_already_selected_pane_untouched()
    {
        using var host = Open(new WorkspaceState());
        // The default layout selects the diff. Focusing it again must not disturb the arrangement.
        Assert.True(host.View.Dock.IsSelected(DockIds.Diff));
        var before = host.View.Dock.PaneOrder();

        host.View.FocusDock(DockIds.Diff);

        Assert.Equal(before, host.View.Dock.PaneOrder());
        Assert.True(host.View.Dock.IsSelected(DockIds.Diff));
    }

    private static Host Open(WorkspaceState state)
    {
        var directory = Path.Combine(Path.GetTempPath(), "sextant-dock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var vm = new MainViewModel(new WorkspaceStore(directory), state, new AppSettings(), new GitProcessRunner());
        return new Host(directory, vm);
    }

    private sealed class Host : IDisposable
    {
        private readonly string _directory;

        public Host(string directory, MainViewModel view)
        {
            _directory = directory;
            View = view;
        }

        public MainViewModel View { get; }

        public void Dispose()
        {
            View.Shutdown().GetAwaiter().GetResult();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
