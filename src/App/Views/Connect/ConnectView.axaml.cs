using Avalonia.Controls;
using ServiceBusExplorer.ViewModels;

namespace ServiceBusExplorer.App.Views.Connect;

public partial class ConnectView : UserControl
{
    public ConnectView() => InitializeComponent();

    // When a history item is selected, copy it to the ConnectionString field
    private void OnHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is string cs
            && DataContext is ConnectViewModel vm)
        {
            vm.ConnectionString = cs;
        }
    }
}
