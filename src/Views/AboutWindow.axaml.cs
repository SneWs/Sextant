using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Sextant.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = "Version " + AppInfo.Version;
        CopyrightText.Text = AppInfo.Copyright;
        LicenseText.Text = AppInfo.License;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
