using Avalonia.Controls;
using ServiceBusExplorer.ViewModels;

namespace ServiceBusExplorer.App.Views.Connect;

public partial class ConnectView : UserControl
{
    public ConnectView() => InitializeComponent();

    // Restore only non-secret metadata; SAS must always be entered again.
    private void OnHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ConnectionProfile profile
            && DataContext is ConnectViewModel vm)
        {
            vm.ApplyProfile(profile);
            ConnectionStringBox.Focus();
        }
    }
}
