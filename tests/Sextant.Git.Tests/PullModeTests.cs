using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Sextant.Services;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class PullModeTests
{
    [Fact]
    public async Task Git_page_saves_rebase_or_merge()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            var savedMerge = Show(PullPreference.Merge);
            Assert.Equal(PullPreference.Rebase, Choose(savedMerge, "Rebase"));
            savedMerge.Close();

            var savedRebase = Show(null);
            Assert.Equal(PullPreference.Merge, Choose(savedRebase, "Merge"));
            savedRebase.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Merge_setting_merges_on_pull_and_pull_with_rebase_still_rebases()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        using var origin = new TempRepo();
        origin.WriteFile("a.txt", "one\n");
        origin.WriteFile("notes.txt", "note\n");
        origin.CommitAll("base");
        var mergeClone = Clone(origin);
        var rebaseClone = Clone(origin);
        try
        {
            CommitLocal(origin, mergeClone);
            CommitLocal(origin, rebaseClone);
            origin.WriteFile("a.txt", "two\n");
            origin.CommitAll("second");
            File.WriteAllText(Path.Combine(mergeClone, "notes.txt"), "note edited\n");
            File.WriteAllText(Path.Combine(rebaseClone, "notes.txt"), "note edited\n");

            await session.Dispatch(async () =>
            {
                var merge = await Open(origin.Git, mergeClone);
                var rebase = await Open(origin.Git, rebaseClone);
                try
                {
                    await merge.Pull();
                    Assert.False(merge.HasBanner, merge.Banner);
                    await rebase.PullRebase();
                    Assert.False(rebase.HasBanner, rebase.Banner);
                    Assert.Contains("--no-rebase", merge.CommandLog);
                    Assert.Contains("--autostash", merge.CommandLog);
                    Assert.DoesNotContain(" pull --rebase", merge.CommandLog);
                    Assert.Contains(" pull --rebase", rebase.CommandLog);
                    Assert.Contains("--autostash", rebase.CommandLog);
                    Assert.DoesNotContain("--no-rebase", rebase.CommandLog);
                }
                finally
                {
                    await merge.DisposeAsync();
                    await rebase.DisposeAsync();
                }

                return 0;
            }, CancellationToken.None);

            Assert.Equal("note edited\n", Read(mergeClone));
            Assert.Equal("note edited\n", Read(rebaseClone));
            var parents = origin.RunCapture("-C", mergeClone, "rev-list", "--parents", "-n", "1", "HEAD").Trim();
            Assert.Equal(3, parents.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
            var log = origin.RunCapture("-C", rebaseClone, "log", "--format=%s", "-2");
            Assert.Equal(new[] { "local", "second" }, log.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            DeleteDirectory(mergeClone);
            DeleteDirectory(rebaseClone);
        }
    }

    private static SettingsWindow Show(string? pullMode)
    {
        var window = new SettingsWindow(new SettingsDraft("", false, false, "system", "", PullMode: pullMode));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.GetLogicalDescendants().OfType<ListBox>().Single().SelectedIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var label = window.GetLogicalDescendants().OfType<TextBlock>().Single(block => block.Text == "When pulling");
        Assert.True(label.IsEffectivelyVisible);
        return window;
    }

    private static string? Choose(SettingsWindow window, string content)
    {
        var option = window.GetLogicalDescendants().OfType<RadioButton>().Single(radio => radio.Content as string == content);
        Assert.False(option.IsChecked);
        option.IsChecked = true;
        window.GetLogicalDescendants().OfType<Button>().Single(button => button.Content as string == "OK")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return window.Result?.PullMode;
    }

    private static async Task<RepositoryViewModel> Open(string git, string directory)
    {
        var tab = new RepositoryViewModel(new PullHost(git), directory);
        await tab.EnsureLoadedAsync();
        return tab;
    }

    private static string Clone(TempRepo origin)
    {
        var clone = Path.Combine(Path.GetTempPath(), "sextant-clone-" + Guid.NewGuid().ToString("N"));
        origin.Run("clone", origin.Directory, clone);
        origin.SetIdentity(clone);
        return clone;
    }

    private static void CommitLocal(TempRepo origin, string directory)
    {
        File.WriteAllText(Path.Combine(directory, "local.txt"), "local\n");
        origin.Run("-C", directory, "add", "local.txt");
        origin.Run("-C", directory, "commit", "-m", "local");
    }

    private static string Read(string directory) =>
        File.ReadAllText(Path.Combine(directory, "notes.txt")).Replace("\r\n", "\n");

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

    private sealed class PullHost(string git) : IWorkspaceHost
    {
        public IDialogService? Dialogs => null;

        public GitProcessRunner Runner { get; } = new();

        public string? GitExecutable { get; } = git;

        public string? MergeTool => null;

        public string PullMode => PullPreference.Merge;

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
