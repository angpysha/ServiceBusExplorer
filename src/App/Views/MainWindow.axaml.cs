using Avalonia.Controls;
using Avalonia.Interactivity;
using ServiceBusExplorer.App.Views.Dialogs;

namespace ServiceBusExplorer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnToggleLogPanel(object? sender, RoutedEventArgs e)
    {
        LogPanel.IsVisible = !LogPanel.IsVisible;
        if (LogPanel.IsVisible)
        {
            // Scroll to last entry
            LogListBox.ScrollIntoView(LogListBox.ItemCount > 0 ? LogListBox.ItemCount - 1 : 0);
        }
    }

    private void OnClearLog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.LogSink.Entries.Clear();
    }

    private void OnAbout(object? sender, RoutedEventArgs e)
    {
        var dlg = new AboutDialog();
        dlg.ShowDialog(this);
    }
}
