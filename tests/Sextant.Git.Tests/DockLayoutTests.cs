using Avalonia.Controls;
using Avalonia.Headless;
using Sextant.Docking;
using Sextant.Git.Repo;
using Sextant.Git.Workspace;
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
            [DockIds.Locations, DockIds.History, DockIds.Changes, DockIds.Staging, DockIds.Diff, DockIds.Blame],
            vm.View.Dock.PaneOrder());
        Assert.True(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Blame));
    }

    [Fact]
    public void Reset_restores_the_default_tools()
    {
        using var vm = Open(new WorkspaceState());
        vm.View.Dock.SetOpen(DockIds.Files, true);
        vm.View.Dock.SetOpen(DockIds.Commands, true);
        Assert.Contains(DockIds.Files, vm.View.Dock.PaneOrder());
        Assert.True(vm.View.Dock.IsOpen(DockIds.Commands));
        Assert.True(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Blame));

        vm.View.Dock.Reset();

        Assert.Equal(
            [DockIds.Locations, DockIds.History, DockIds.Changes, DockIds.Staging, DockIds.Diff, DockIds.Blame],
            vm.View.Dock.PaneOrder());
        Assert.True(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Blame));
        Assert.False(vm.View.Dock.IsOpen(DockIds.Files));
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
                [DockIds.History, DockIds.Changes, DockIds.Staging, DockIds.Diff, DockIds.Blame],
                vm.Dock.PaneOrder());
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
            [DockIds.Locations, DockIds.History, DockIds.Changes, DockIds.Staging, DockIds.Blame],
            vm.View.Dock.PaneOrder());
        Assert.False(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Blame));
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
            [DockIds.Locations, DockIds.History, DockIds.Changes, DockIds.Staging, DockIds.Diff, DockIds.Blame],
            vm.View.Dock.PaneOrder());
        Assert.True(vm.View.Dock.SharesHost(DockIds.Diff, DockIds.Blame));
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
                        "_Changes",
                        "_Staging",
                        "_Diff",
                        "_Blame",
                        "_Repository files",
                        "Command _log",
                    ],
                    headers);
                Assert.DoesNotContain(headers, header => header?.Contains("Toggle", StringComparison.Ordinal) == true);
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
            [DockIds.Locations, DockIds.History, DockIds.Changes, DockIds.Staging, DockIds.Diff, DockIds.Blame],
            vm.View.Dock.PaneOrder());
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
