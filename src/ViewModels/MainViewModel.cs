using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant;
using Sextant.Git;
using Sextant.Git.Parsing;
using Sextant.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Sextant.Git.Diff;
using Sextant.Git.Repo;
using Sextant.Git.Workspace;
using Sextant.Git.Wsl;

namespace Sextant.ViewModels;

public partial class MainViewModel : ViewModelBase, IWorkspaceHost
{
    private readonly WorkspaceStore _store;
    private readonly WorkspaceState _workspace;
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<PaletteItem> _palette = [];
    private readonly HashSet<Task> _closingTabs = [];
    private readonly List<WslDistro> _wslDistros = [];
    private Task? _initialize;
    private Task? _shutdown;
    private bool _started;

    public MainViewModel(WorkspaceStore store, WorkspaceState workspace, AppSettings settings, GitProcessRunner runner)
    {
        _store = store;
        _workspace = workspace;
        _settings = settings;
        Runner = runner;
        WindowWidth = workspace.WindowWidth;
        WindowHeight = workspace.WindowHeight;
        WindowX = workspace.WindowX;
        WindowY = workspace.WindowY;
        WindowMaximized = workspace.WindowMaximized;
        Tabs.CollectionChanged += (_, _) => RefreshShortcutHints();
    }

    public GitProcessRunner Runner { get; }

    public IDialogService? Dialogs { get; private set; }

    public ObservableCollection<RepositoryViewModel> Tabs { get; } = [];

    public ObservableCollection<PaletteItem> PaletteMatches { get; } = [];

    [ObservableProperty]
    public partial RepositoryViewModel? ActiveTab { get; set; }

    [ObservableProperty]
    public partial string? GitExecutable { get; set; }

    public string? MergeTool => MergeToolCommand.Normalize(_settings.MergeTool);

    public IReadOnlyList<DiffFormatRule> DiffFormats => DiffFormatRules.Normalize(_settings.DiffFormats);

    [ObservableProperty]
    public partial bool GitReady { get; set; }

    [ObservableProperty]
    public partial bool HasWsl { get; set; }

    /// <summary>Open from WSL does not need the Windows git binary. The tab uses the distribution's git.</summary>
    public bool CanOpenWsl => HasWsl && !IsBusy;

    [ObservableProperty]
    public partial string GitProblem { get; set; } = "";

    [ObservableProperty]
    public partial bool HasGitProblem { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial string TitleText { get; set; } = "Sextant";

    [ObservableProperty]
    public partial bool PaletteOpen { get; set; }

    [ObservableProperty]
    public partial string PaletteQuery { get; set; } = "";

    [ObservableProperty]
    public partial PaletteItem? SelectedPalette { get; set; }

    public double WindowWidth { get; set; }

    public double WindowHeight { get; set; }

    public int? WindowX { get; set; }

    public int? WindowY { get; set; }

    public bool WindowMaximized { get; set; }

    public bool CanUseGit => GitReady && !IsBusy;

    /// <summary>Branch, stash, and the other repository commands need an open tab that is not busy.</summary>
    public bool CanRunRepositoryCommands => ActiveTab is { CanRunCommands: true };

    public bool CanStash => ActiveTab is { CanStash: true };

    public bool CanPopLatestStash => ActiveTab is { CanPopLatestStash: true };

    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    public bool HasActiveTab => ActiveTab is not null;

    public bool ShowWindowExitMenu => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <summary>macOS shows Settings in the application menu, so the window does not repeat it.</summary>
    public bool ShowWindowSettingsMenu => !OperatingSystem.IsMacOS();

    /// <summary>macOS shows About in the application menu, before Settings. Windows and Linux keep it under About.</summary>
    public bool ShowWindowAboutMenu => !OperatingSystem.IsMacOS();

    /// <summary>macOS keeps Get help under Help. About itself is in the application menu.</summary>
    public bool ShowMacHelpMenu => OperatingSystem.IsMacOS();

    public bool ShowEmpty => ActiveTab is null;

    public void Attach(IDialogService dialogs) => Dialogs = dialogs;

    public Task InitializeAsync() => _initialize ??= InitializeCoreAsync();

    public Task OnWindowActivated()
    {
        return _started && ActiveTab is { IsReady: true } tab
            ? tab.RefreshFromFocusAsync()
            : Task.CompletedTask;
    }

    public Task Shutdown() => _shutdown ??= ShutdownCoreAsync();

    private async Task ShutdownCoreAsync()
    {
        Save();
        _lifetime.Cancel();
        var tabs = Tabs.ToArray();
        var closing = _closingTabs.ToArray();
        await Task.WhenAll(tabs.Select(tab => tab.DisposeAsync().AsTask()).Concat(closing));
        if (_initialize is not null)
            await _initialize;
    }

    public async Task Activate(RepositoryViewModel tab)
    {
        if (!Tabs.Contains(tab))
            return;
        var refresh = ActiveTab != tab && tab.IsReady;
        foreach (var other in Tabs)
            other.IsActive = other == tab;
        ActiveTab = tab;
        TitleText = $"Sextant — {tab.Title}";
        Save();
        await tab.EnsureLoadedAsync();
        // The first selection loads the repository. A later switch refreshes it
        // the same way focusing the window does, so commits and files stay current.
        if (refresh && ActiveTab == tab)
            await tab.RefreshFromFocusAsync();
    }

    public async Task Close(RepositoryViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;
        Tabs.Remove(tab);
        var cleanup = tab.DisposeAsync().AsTask();
        _closingTabs.Add(cleanup);
        try
        {
            if (ActiveTab == tab)
            {
                if (Tabs.Count == 0)
                {
                    ActiveTab = null;
                    TitleText = "Sextant";
                }
                else
                {
                    await Activate(Tabs[Math.Min(index, Tabs.Count - 1)]);
                    return;
                }
            }

            Save();
        }
        finally
        {
            try
            {
                await cleanup;
            }
            finally
            {
                _closingTabs.Remove(cleanup);
            }
        }
    }

    public void NoteLoaded(RepositoryViewModel tab)
    {
        if (tab.Toplevel is not { Length: > 0 } top)
            return;
        if (!tab.PanesEdited)
        {
            var saved = RepoLayouts.TryGet(_workspace, top);
            if (saved is not null)
            {
                tab.LocationsWidth = saved.LocationsWidth;
                tab.GraphWidth = saved.GraphWidth;
                tab.FilesHeight = saved.FilesHeight;
                tab.ShowLocations = saved.ShowLocations;
            }
        }

        if (ActiveTab == tab)
            TitleText = $"Sextant — {tab.Title}";
        Save();
    }

    public void Save()
    {
        _workspace.OpenTabs = Tabs.Select(tab => tab.Toplevel ?? tab.RequestedPath).ToList();
        _workspace.ActiveTab = ActiveTab is null ? null : ActiveTab.Toplevel ?? ActiveTab.RequestedPath;
        foreach (var tab in Tabs)
        {
            var path = tab.Toplevel ?? tab.RequestedPath;
            if (string.IsNullOrWhiteSpace(path))
                continue;
            RepoLayouts.Remember(_workspace, path, tab.LocationsWidth, tab.GraphWidth, tab.FilesHeight, tab.ShowLocations, tab.HiddenBranchNames);
            if (tab.Toplevel is { Length: > 0 } top && !RepoPath.Same(tab.RequestedPath, top))
                RepoLayouts.Forget(_workspace, tab.RequestedPath);
        }

        _workspace.WindowWidth = WindowWidth;
        _workspace.WindowHeight = WindowHeight;
        _workspace.WindowX = WindowX;
        _workspace.WindowY = WindowY;
        _workspace.WindowMaximized = WindowMaximized;
        _store.SaveWorkspace(_workspace);
    }

    public void MoveTab(RepositoryViewModel tab, int index)
    {
        var from = Tabs.IndexOf(tab);
        if (from < 0 || index < 0 || index >= Tabs.Count || from == index)
            return;
        Tabs.Move(from, index);
    }

    public Task ActivateDigit(int digit)
    {
        var index = TabShortcut.IndexFromDigit(digit);
        if (index is not int slot || slot >= Tabs.Count || Tabs[slot] == ActiveTab)
            return Task.CompletedTask;
        return Activate(Tabs[slot]);
    }

    private void RefreshShortcutHints()
    {
        for (var i = 0; i < Tabs.Count; i++)
            Tabs[i].ShortcutHint = TabShortcut.Hint(i);
    }

    [RelayCommand(CanExecute = nameof(CanUseGit))]
    public Task OpenFolder() => OpenFolderAsync();

    [RelayCommand(CanExecute = nameof(CanOpenWsl))]
    public Task OpenWsl() => OpenWslAsync();

    [RelayCommand(CanExecute = nameof(CanUseGit))]
    public Task Clone() => CloneAsync();

    [RelayCommand(CanExecute = nameof(CanUseGit))]
    public Task Init() => InitAsync();

    /// <summary>File → Exit. The window closes, which saves the workspace and shuts down.</summary>
    public event EventHandler? ExitRequested;

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanRunRepositoryCommands))]
    private Task CreateBranch() => ActiveTab?.CreateBranch() ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanStash))]
    private Task Stash() => ActiveTab is { } tab && tab.StashCommand.CanExecute(null)
        ? tab.StashCommand.ExecuteAsync(null)
        : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanPopLatestStash))]
    private Task PopLatestStash() => ActiveTab is { } tab && tab.PopLatestStashCommand.CanExecute(null)
        ? tab.PopLatestStashCommand.ExecuteAsync(null)
        : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRunRepositoryCommands))]
    private Task AddRemote() => ActiveTab?.AddRemoteCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRunRepositoryCommands))]
    private Task AddWorktree() => ActiveTab?.AddWorktreeCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRunRepositoryCommands))]
    private Task ApplyPatch() => ActiveTab?.ApplyPatchCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRunRepositoryCommands))]
    private Task FetchLfs() => ActiveTab?.FetchLfsCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRunRepositoryCommands))]
    private Task PullLfs() => ActiveTab?.PullLfsCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRunRepositoryCommands))]
    private Task Pull() => ActiveTab?.PullCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRunRepositoryCommands))]
    private Task Push() => ActiveTab?.PushCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(HasActiveTab))]
    private void ToggleCommands() => ActiveTab?.ToggleCommandsCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(HasActiveTab))]
    private void ToggleHistorySearch() => ActiveTab?.ToggleHistorySearchCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(HasActiveTab))]
    private void ToggleBranchView() => ActiveTab?.ToggleLocationsCommand.Execute(null);

    [RelayCommand]
    public Task LocateGit() => LocateGitAsync();

    [RelayCommand]
    public void TogglePalette()
    {
        if (PaletteOpen)
        {
            ClosePalette();
            return;
        }

        RebuildPalette();
        PaletteQuery = "";
        FilterPalette();
        PaletteOpen = true;
    }

    [RelayCommand]
    public void ClosePalette() => PaletteOpen = false;

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task CloseActive() => ActiveTab is { } tab ? Close(tab) : Task.CompletedTask;

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task NextTab()
    {
        if (Tabs.Count < 2 || ActiveTab is null)
            return Task.CompletedTask;
        var index = Tabs.IndexOf(ActiveTab);
        return Activate(Tabs[(index + 1) % Tabs.Count]);
    }

    [RelayCommand]
    public Task RefreshActive() => ActiveTab?.Refresh() ?? Task.CompletedTask;

    [RelayCommand]
    public Task RunPaletteAsync()
    {
        var item = SelectedPalette ?? PaletteMatches.FirstOrDefault();
        PaletteOpen = false;
        return item is null ? Task.CompletedTask : item.Run();
    }

    public void MovePalette(int delta)
    {
        if (PaletteMatches.Count == 0)
            return;
        var index = SelectedPalette is null ? 0 : PaletteMatches.IndexOf(SelectedPalette);
        if (index < 0)
            index = 0;
        var next = Math.Clamp(index + delta, 0, PaletteMatches.Count - 1);
        SelectedPalette = PaletteMatches[next];
    }

    partial void OnGitReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUseGit));
        NotifyFileCommands();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUseGit));
        OnPropertyChanged(nameof(CanOpenWsl));
        NotifyFileCommands();
    }

    partial void OnHasWslChanged(bool value)
    {
        OnPropertyChanged(nameof(CanOpenWsl));
        OpenWslCommand.NotifyCanExecuteChanged();
    }

    private void NotifyFileCommands()
    {
        OpenFolderCommand.NotifyCanExecuteChanged();
        OpenWslCommand.NotifyCanExecuteChanged();
        CloneCommand.NotifyCanExecuteChanged();
        InitCommand.NotifyCanExecuteChanged();
    }

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatusText));

    private RepositoryViewModel? _menuTab;

    partial void OnActiveTabChanged(RepositoryViewModel? value)
    {
        if (_menuTab is not null)
            _menuTab.PropertyChanged -= OnMenuTabChanged;
        _menuTab = value;
        if (value is not null)
            value.PropertyChanged += OnMenuTabChanged;
        OnPropertyChanged(nameof(HasActiveTab));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(CanRunRepositoryCommands));
        NotifyRepositoryCommands();
    }

    private void OnMenuTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(RepositoryViewModel.CanRunCommands) or nameof(RepositoryViewModel.IsBusy)
            or nameof(RepositoryViewModel.CanStash) or nameof(RepositoryViewModel.CanPopLatestStash)))
            return;
        OnPropertyChanged(nameof(CanRunRepositoryCommands));
        NotifyRepositoryCommands();
    }

    private void NotifyRepositoryCommands()
    {
        OnPropertyChanged(nameof(CanStash));
        OnPropertyChanged(nameof(CanPopLatestStash));
        CreateBranchCommand.NotifyCanExecuteChanged();
        StashCommand.NotifyCanExecuteChanged();
        PopLatestStashCommand.NotifyCanExecuteChanged();
        AddRemoteCommand.NotifyCanExecuteChanged();
        AddWorktreeCommand.NotifyCanExecuteChanged();
        ApplyPatchCommand.NotifyCanExecuteChanged();
        FetchLfsCommand.NotifyCanExecuteChanged();
        PullLfsCommand.NotifyCanExecuteChanged();
        PullCommand.NotifyCanExecuteChanged();
        PushCommand.NotifyCanExecuteChanged();
        ToggleCommandsCommand.NotifyCanExecuteChanged();
        ToggleHistorySearchCommand.NotifyCanExecuteChanged();
        ToggleBranchViewCommand.NotifyCanExecuteChanged();
    }

    partial void OnPaletteQueryChanged(string value) => FilterPalette();

    private async Task InitializeCoreAsync()
    {
        await Task.WhenAll(ProbeAsync(), RefreshWslAsync());
        if (_lifetime.IsCancellationRequested)
            return;
        if (_settings.ReopenTabs)
        {
            foreach (var path in _workspace.OpenTabs)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                var canonical = WslPath.CanonicalWindows(path);
                var openPath = Directory.Exists(canonical) ? canonical : path;
                if (!Directory.Exists(openPath))
                    continue;
                if (Tabs.Any(tab => SameTab(tab, openPath)))
                    continue;
                Tabs.Add(CreateTab(openPath, await BindingForAsync(openPath)));
            }

            var active = Tabs.FirstOrDefault(tab => _workspace.ActiveTab is not null && SameTab(tab, _workspace.ActiveTab));
            if (active is null && Tabs.Count > 0)
                active = Tabs[0];
            if (active is not null)
                await Activate(active);
        }

        _started = true;
    }

    private async Task ProbeAsync()
    {
        string? path = null;
        if (!string.IsNullOrWhiteSpace(_settings.GitExecutable) && File.Exists(_settings.GitExecutable))
            path = _settings.GitExecutable;
        else
            path = GitLocator.FindOnPath();

        if (path is null)
        {
            SetGitProblem("Git was not found on PATH. Use Locate git to pick the executable.");
            return;
        }

        try
        {
            var version = await GitLocator.ProbeAsync(Runner, path, _lifetime.Token);
            GitExecutable = path;
            if (!GitVersions.IsSupported(version))
            {
                SetGitProblem($"Git {version.Raw} is older than 2.43.");
                GitReady = false;
                return;
            }

            GitReady = true;
            HasGitProblem = false;
            GitProblem = "";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            GitExecutable = path;
            GitReady = false;
            SetGitProblem(exception.Message);
        }
    }

    private async Task RefreshWslAsync()
    {
        try
        {
            var distros = await WslProbe.ListUserDistrosAsync(_lifetime.Token);
            _wslDistros.Clear();
            _wslDistros.AddRange(distros);
            HasWsl = _wslDistros.Count > 0;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            _wslDistros.Clear();
            HasWsl = false;
        }
    }

    private async Task<WslGit?> BindingForAsync(string path)
    {
        if (!WslPath.TryParseUnc(path, out var distribution, out _))
            return null;
        return await WslProbe.BindingAsync(distribution, _lifetime.Token);
    }

    public Task OpenRepositoryAsync(string path) => OpenPathAsync(path);

    private async Task OpenFolderAsync()
    {
        if (!CanUseGit || Dialogs is null)
            return;
        var path = await Dialogs.PickFolderAsync("Open repository");
        if (string.IsNullOrWhiteSpace(path))
            return;
        await OpenPathAsync(path);
    }

    private async Task OpenWslAsync()
    {
        if (Dialogs is null || IsBusy)
            return;
        await RefreshWslAsync();
        if (_wslDistros.Count == 0)
        {
            StatusText = "No WSL2 distribution was found.";
            return;
        }

        var distro = await ChooseDistroAsync();
        if (distro is null)
            return;

        IsBusy = true;
        StatusText = "Asking " + distro.Name + "…";
        string? home = null;
        string? start = null;
        try
        {
            home = await WslProbe.HomeAsync(distro.Name, _lifetime.Token);
            if (home is not null)
                start = await WslProbe.ToWindowsAsync(distro.Name, home, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "";
            return;
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        StatusText = "";
        var picked = await PickWslFolderAsync(distro.Name, start, home);
        if (string.IsNullOrWhiteSpace(picked))
            return;

        IsBusy = true;
        StatusText = "Opening " + distro.Name + "…";
        try
        {
            var repo = await WslProbe.ResolveAsync(distro.Name, picked, _lifetime.Token);
            if (repo.Problem is not null)
            {
                StatusText = repo.Problem;
                return;
            }

            StatusText = "";
            await OpenPathAsync(repo.WindowsPath, repo.Git);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Open cancelled.";
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<WslDistro?> ChooseDistroAsync()
    {
        if (_wslDistros.Count == 1)
            return _wslDistros[0];
        var labels = _wslDistros.Select(distro =>
            distro.Name + (distro.IsDefault ? " (default)" : "") + " — " + distro.State).ToArray();
        var picked = await Dialogs!.PickAsync("Open from WSL", "Choose a WSL2 distribution.", labels);
        if (picked is null)
            return null;
        var index = Array.IndexOf(labels, picked);
        return index < 0 ? null : _wslDistros[index];
    }

    private async Task<string?> PickWslFolderAsync(string distribution, string? windowsHome, string? linuxHome)
    {
        if (!string.IsNullOrWhiteSpace(windowsHome))
            return await Dialogs!.PickFolderAsync("Open repository in " + distribution, windowsHome);
        return await Dialogs!.PromptAsync(
            "Open repository in " + distribution,
            "Linux path inside " + distribution,
            linuxHome ?? "/");
    }

    private async Task OpenPathAsync(string path, WslGit? wsl = null)
    {
        path = WslPath.CanonicalWindows(path);
        if (wsl is null && WslPath.TryParseUnc(path, out var distribution, out _))
        {
            try
            {
                wsl = await BindingForAsync(path);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                StatusText = exception.Message;
                return;
            }

            if (wsl is null)
            {
                StatusText = "Git was not found in " + distribution + ".";
                return;
            }
        }

        if (wsl is { Problem: { Length: > 0 } problem })
        {
            StatusText = problem;
            return;
        }

        if (wsl is null && (!GitReady || GitExecutable is null))
            return;
        if (!Directory.Exists(path))
        {
            StatusText = "That folder does not exist.";
            return;
        }

        var existing = Tabs.FirstOrDefault(tab => SameTab(tab, path));
        if (existing is not null)
        {
            await Activate(existing);
            return;
        }

        var tab = CreateTab(path, wsl);
        Tabs.Add(tab);
        await Activate(tab);
        if (!Tabs.Contains(tab))
            return;
        if (tab.Toplevel is not null)
        {
            var duplicate = Tabs.FirstOrDefault(other => other != tab && other.Toplevel is not null && RepoPath.Same(other.Toplevel, tab.Toplevel));
            if (duplicate is not null)
            {
                await Close(tab);
                await Activate(duplicate);
                return;
            }
        }

        Save();
    }

    private async Task CloneAsync()
    {
        if (!CanUseGit || Dialogs is null || GitExecutable is null)
            return;
        var request = await Dialogs.PromptCloneAsync();
        if (request is null)
            return;
        IsBusy = true;
        StatusText = "Cloning…";
        var progress = new Progress<string>(text =>
        {
            if (!string.IsNullOrWhiteSpace(text))
                StatusText = text;
        });
        try
        {
            await RepositoryAdmin.CloneAsync(Runner, GitExecutable, request.Url, request.Destination, progress, _lifetime.Token);
            StatusText = "";
            await OpenPathAsync(request.Destination);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Clone cancelled.";
        }
        catch (GitCommandFailedException exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InitAsync()
    {
        if (!CanUseGit || Dialogs is null || GitExecutable is null)
            return;
        var path = await Dialogs.PickFolderAsync("Init repository");
        if (string.IsNullOrWhiteSpace(path))
            return;
        IsBusy = true;
        StatusText = "Initializing…";
        try
        {
            await RepositoryAdmin.InitAsync(Runner, GitExecutable, path, _lifetime.Token);
            StatusText = "";
            await OpenPathAsync(path);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Init cancelled.";
        }
        catch (GitCommandFailedException exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (Dialogs is null)
            return;
        var edit = await Dialogs.EditSettingsAsync(new SettingsDraft(
            _settings.GitExecutable ?? "",
            _settings.SideBySide,
            _settings.IgnoreWhitespace,
            _settings.Theme,
            _settings.MergeTool ?? "",
            DiffFormatRules.Normalize(_settings.DiffFormats),
            _settings.Palette,
            _settings.DiffFont,
            _settings.DiffFontSize));
        if (edit is null)
            return;
        var path = string.IsNullOrWhiteSpace(edit.GitExecutable) ? null : edit.GitExecutable.Trim();
        var previous = string.IsNullOrWhiteSpace(_settings.GitExecutable) ? null : _settings.GitExecutable;
        var gitChanged = !string.Equals(path, previous, StringComparison.Ordinal);
        var diffChanged = edit.SideBySide != _settings.SideBySide || edit.IgnoreWhitespace != _settings.IgnoreWhitespace;
        var mergeChanged = !string.Equals(
            MergeToolCommand.Normalize(edit.MergeTool),
            MergeToolCommand.Normalize(_settings.MergeTool),
            StringComparison.Ordinal);
        var formatsChanged = !DiffFormatRules.Same(_settings.DiffFormats, edit.DiffFormats);
        _settings.GitExecutable = path;
        _settings.SideBySide = edit.SideBySide;
        _settings.IgnoreWhitespace = edit.IgnoreWhitespace;
        _settings.Theme = ThemePreference.Normalize(edit.Theme);
        _settings.Palette = PalettePreference.Normalize(edit.Palette);
        _settings.MergeTool = MergeToolCommand.Normalize(edit.MergeTool);
        _settings.DiffFormats = DiffFormatRules.Normalize(edit.DiffFormats);
        var font = DiffFontPreference.Normalize(edit.DiffFont);
        _settings.DiffFont = font.Length == 0 ? null : font;
        _settings.DiffFontSize = DiffFontPreference.NormalizeSize(edit.DiffFontSize);
        _store.SaveSettings(_settings);
        AppTheme.Apply(_settings.Theme, _settings.Palette);
        DiffFont.Apply(_settings.DiffFont, _settings.DiffFontSize);
        await Task.WhenAll(
            diffChanged ? ApplyDiffPreferences() : Task.CompletedTask,
            formatsChanged ? ApplyDiffFormats() : Task.CompletedTask,
            mergeChanged ? ApplyMergePreference() : Task.CompletedTask);
        if (gitChanged)
            await ProbeAsync();
    }

    public const string DocumentationUrl = "https://github.com/SneWs/Sextant/blob/master/docs/README.md";

    [RelayCommand]
    private void GetHelp()
    {
        try
        {
            DesktopOpen.Start(DesktopOpen.OpenUrl(DesktopOpen.Current, DocumentationUrl));
        }
        catch (Exception)
        {
            StatusText = "The documentation could not be opened.";
        }
    }

    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        if (Dialogs is null)
            return;
        await Dialogs.ShowAboutAsync();
    }

    private Task ApplyDiffPreferences() =>
        Task.WhenAll(Tabs.Select(tab => tab.ApplyDiffPreferences(_settings.SideBySide, _settings.IgnoreWhitespace)));

    private Task ApplyDiffFormats()
    {
        var formats = DiffFormats;
        return Task.WhenAll(Tabs.Select(tab => tab.ApplyDiffFormats(formats)));
    }

    private Task ApplyMergePreference() => Task.WhenAll(Tabs.Select(tab => tab.ApplyMergePreference()));

    private Task ToggleSavedSideBySide()
    {
        _settings.SideBySide = !_settings.SideBySide;
        _store.SaveSettings(_settings);
        return ApplyDiffPreferences();
    }

    private Task ToggleSavedWhitespace()
    {
        _settings.IgnoreWhitespace = !_settings.IgnoreWhitespace;
        _store.SaveSettings(_settings);
        return ApplyDiffPreferences();
    }

    private async Task LocateGitAsync()
    {
        if (Dialogs is null || IsBusy)
            return;
        var path = await Dialogs.PickGitExecutableAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            var version = await GitLocator.ProbeAsync(Runner, path, _lifetime.Token);
            GitExecutable = path;
            _settings.GitExecutable = path;
            _store.SaveSettings(_settings);
            if (!GitVersions.IsSupported(version))
            {
                GitReady = false;
                SetGitProblem($"Git {version.Raw} is older than 2.43.");
                return;
            }

            GitReady = true;
            HasGitProblem = false;
            GitProblem = "";
            StatusText = version.Raw;
        }
        catch (Exception exception)
        {
            GitReady = false;
            SetGitProblem(exception.Message);
        }
    }

    private RepositoryViewModel CreateTab(string path, WslGit? wsl = null)
    {
        var tab = new RepositoryViewModel(this, path, wsl);
        var layout = RepoLayouts.Resolve(_workspace, path);
        tab.LocationsWidth = layout.LocationsWidth;
        tab.GraphWidth = layout.GraphWidth;
        tab.FilesHeight = layout.FilesHeight;
        tab.ShowLocations = layout.ShowLocations;
        tab.UseHiddenBranches(layout.HiddenBranches);
        tab.SideBySide = _settings.SideBySide;
        tab.IgnoreWhitespace = _settings.IgnoreWhitespace;
        return tab;
    }

    private static bool SameTab(RepositoryViewModel tab, string path)
    {
        if (RepoPath.Same(tab.RequestedPath, path))
            return true;
        return tab.Toplevel is not null && RepoPath.Same(tab.Toplevel, path);
    }

    private void SetGitProblem(string message)
    {
        GitProblem = message;
        HasGitProblem = true;
        GitReady = false;
    }

    private void RebuildPalette()
    {
        _palette.Clear();
        _palette.Add(new PaletteItem { Title = "Open repository", Run = OpenFolderAsync });
        if (HasWsl)
            _palette.Add(new PaletteItem { Title = "Open from WSL", Run = OpenWslAsync });
        _palette.Add(new PaletteItem { Title = "Clone repository", Run = CloneAsync });
        _palette.Add(new PaletteItem { Title = "Init repository", Run = InitAsync });
        _palette.Add(new PaletteItem { Title = "Locate git", Run = LocateGitAsync });
        _palette.Add(new PaletteItem { Title = "Settings", Run = OpenSettingsAsync });
        if (ActiveTab is { } tab)
        {
            _palette.Add(new PaletteItem { Title = "Refresh", Run = tab.Refresh });
            _palette.Add(new PaletteItem { Title = "Fetch", Run = tab.Fetch });
            _palette.Add(new PaletteItem { Title = "Fetch all", Run = tab.FetchAll });
            _palette.Add(new PaletteItem { Title = "Fetch all and clean up", Run = tab.FetchAllAndCleanUp });
            _palette.Add(new PaletteItem { Title = "Pull", Run = tab.Pull });
            _palette.Add(new PaletteItem { Title = "Push", Run = tab.Push });
            _palette.Add(new PaletteItem { Title = "Push ignoring local checks", Run = () => tab.PushIgnoringLocalChecksCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Push with force-with-lease", Run = () => tab.PushForceWithLeaseCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Commit", Run = tab.Commit });
            _palette.Add(new PaletteItem { Title = "Commit without hooks", Run = () => tab.CommitWithoutHooksCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Amend", Run = () => tab.AmendCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Stage all", Run = () => tab.StageAllCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Unstage all", Run = () => tab.UnstageAllCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Discard all", Run = () => tab.DiscardAllCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Checkout branch", Run = tab.CheckoutFromPalette });
            _palette.Add(new PaletteItem { Title = "Create branch", Run = tab.CreateBranch });
            _palette.Add(new PaletteItem { Title = "Merge branch", Run = tab.MergeFromPalette });
            _palette.Add(new PaletteItem { Title = "Search history", Run = () => { tab.ToggleHistorySearchCommand.Execute(null); return Task.CompletedTask; } });
            if (CanStash)
                _palette.Add(new PaletteItem { Title = "Stash", Run = () => StashCommand.ExecuteAsync(null) });
            if (CanPopLatestStash)
                _palette.Add(new PaletteItem { Title = "Pop latest stash", Run = () => PopLatestStashCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Add remote", Run = () => tab.AddRemoteCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Add worktree", Run = () => tab.AddWorktreeCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Apply patch", Run = () => tab.ApplyPatchCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Fetch LFS objects", Run = () => tab.FetchLfsCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Pull LFS files", Run = () => tab.PullLfsCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Toggle locations", Run = () => { tab.ToggleLocationsCommand.Execute(null); return Task.CompletedTask; } });
            _palette.Add(new PaletteItem { Title = "Toggle side-by-side diff", Run = ToggleSavedSideBySide });
            _palette.Add(new PaletteItem { Title = "Toggle ignore whitespace", Run = ToggleSavedWhitespace });
            if (!tab.ShowingWorkingCopy || tab.ShowingBlame)
                _palette.Add(new PaletteItem { Title = "Toggle all files", Run = () => tab.ToggleAllFilesCommand.ExecuteAsync(null) });
            _palette.Add(new PaletteItem { Title = "Toggle blame", Run = () => tab.ToggleBlameCommand.ExecuteAsync(null) });
            if (tab.IsConflicted)
            {
                _palette.Add(new PaletteItem { Title = "Continue", Run = tab.ContinueSequencer });
                _palette.Add(new PaletteItem { Title = "Abort", Run = tab.AbortMerge });
                _palette.Add(new PaletteItem { Title = "Save conflict resolution", Run = tab.SaveConflict });
            }
            _palette.Add(new PaletteItem { Title = "Toggle command log", Run = () => { tab.CommandsOpen = !tab.CommandsOpen; return Task.CompletedTask; } });
        }

        _palette.Add(new PaletteItem { Title = "Next tab", Run = () => NextTabCommand.ExecuteAsync(null) });
        _palette.Add(new PaletteItem { Title = "Close tab", Run = () => CloseActiveCommand.ExecuteAsync(null) });
        _palette.Sort(static (left, right) => string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase));
    }

    private void FilterPalette()
    {
        var tokens = PaletteQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        PaletteMatches.Clear();
        foreach (var item in _palette)
        {
            if (tokens.All(token => item.Title.Contains(token, StringComparison.OrdinalIgnoreCase)))
                PaletteMatches.Add(item);
        }

        SelectedPalette = PaletteMatches.FirstOrDefault();
    }
}
