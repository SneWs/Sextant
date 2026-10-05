using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Sextant.Services;
using Sextant.Views;

namespace Sextant.Git.Tests;

[Collection(HeadlessCollection.Name)]
public class DialogFocusTests
{
    [Fact]
    public async Task Prompt_focuses_the_first_field_and_selects_existing_text()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            var name = new TextBox { Text = "topic" };
            var other = new TextBox { Text = "later" };
            var window = new Window
            {
                Width = 520,
                Height = 240,
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = "Branch name" },
                        new Button { Content = "Cancel" },
                        name,
                        other,
                        new Button { Content = "OK", IsDefault = true },
                    },
                },
            };
            DialogFocus.WhenShown(window);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Same(name, window.FocusManager?.GetFocusedElement());
            Assert.Equal("topic", name.SelectedText);

            var hidden = new TextBox { Text = "hidden", IsVisible = false };
            var folder = new TextBox();
            var page = new StackPanel
            {
                Children =
                {
                    hidden,
                    new Button { Content = "Browse…" },
                    folder,
                },
            };
            Assert.Same(folder, DialogFocus.FirstField(page));

            var off = new CheckBox { Content = "off", IsEnabled = false };
            var on = new CheckBox { Content = "on" };
            var checks = new StackPanel { Children = { off, on } };
            Assert.Same(on, DialogFocus.FirstField(checks));

            var list = new ListBox();
            var pick = new Window
            {
                Width = 520,
                Height = 240,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "Checkout" },
                        list,
                        new Button { Content = "OK", IsDefault = true },
                    },
                },
            };
            DialogFocus.WhenShown(pick);
            pick.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(list, pick.FocusManager?.GetFocusedElement());
            pick.Close();

            var verb = new ComboBox();
            var rebase = new StackPanel
            {
                Children =
                {
                    new Button { Content = "Up" },
                    verb,
                    new TextBox { Text = "message" },
                },
            };
            Assert.Same(verb, DialogFocus.FirstField(rebase));

            var first = new RadioButton { Content = "Follow system", GroupName = "theme" };
            var dark = new RadioButton { Content = "Dark", GroupName = "theme", IsChecked = true };
            var themes = new StackPanel { Children = { first, dark } };
            Assert.Same(dark, DialogFocus.FirstField(themes));

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Service_dialogs_focus_their_first_field()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(async () =>
        {
            var owner = new Window { Width = 800, Height = 600 };
            owner.Show();
            var service = new AvaloniaDialogService(owner);

            Dispatcher.UIThread.Post(ReadPrompt, DispatcherPriority.SystemIdle);
            Assert.Null(await service.PromptAsync("Create branch", "Branch name", "feature"));

            Dispatcher.UIThread.Post(ReadClone, DispatcherPriority.SystemIdle);
            Assert.Null(await service.PromptCloneAsync());

            Dispatcher.UIThread.Post(ReadPick, DispatcherPriority.SystemIdle);
            Assert.Null(await service.PickAsync("Checkout", "Branch", ["main", "dev"]));

            owner.Close();

            void ReadPrompt()
            {
                var dialog = Assert.Single(owner.OwnedWindows);
                var box = Assert.IsType<TextBox>(dialog.FocusManager?.GetFocusedElement());
                Assert.Equal("feature", box.SelectedText);
                dialog.Close();
            }

            void ReadClone()
            {
                var dialog = Assert.Single(owner.OwnedWindows);
                var box = Assert.IsType<TextBox>(dialog.FocusManager?.GetFocusedElement());
                Assert.Equal("https://example.com/repo.git", box.PlaceholderText);
                dialog.Close();
            }

            void ReadPick()
            {
                var dialog = Assert.Single(owner.OwnedWindows);
                Assert.IsType<ListBox>(dialog.FocusManager?.GetFocusedElement());
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Settings_opens_on_the_selected_appearance_option()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(DialogFocusApp));
        await session.Dispatch(() =>
        {
            var window = new SettingsWindow(new SettingsDraft("", false, false, "dark", ""));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var focused = window.FocusManager?.GetFocusedElement();
            var radio = Assert.IsType<RadioButton>(focused);
            Assert.Equal("Dark", radio.Content);
            Assert.True(radio.IsChecked);
            window.Close();
        }, CancellationToken.None);
    }
}

public class DialogFocusApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
    }
}
