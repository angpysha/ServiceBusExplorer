using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ServiceBusExplorer.App.Views.Dialogs;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "1.0.0";
        VersionText.Text = $"Version {version}";
    }

    private void OnGitHub(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "https://github.com/angpysha/ServiceBusExplorer") { UseShellExecute = true });
        }
        catch { }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
