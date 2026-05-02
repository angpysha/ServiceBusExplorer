using System.Collections.ObjectModel;
using ReactiveUI;
using ServiceBusExplorer.ViewModels;

namespace ServiceBusExplorer.App;

public record NavItem(string Label, ReactiveObject ContentVm);

public class AppMainViewModel : ReactiveObject
{
    private NavItem? _selectedNavItem;
    private ReactiveObject? _currentContent;

    public string NamespaceName { get; }
    public ObservableCollection<NavItem> NavItems { get; }
    public DashboardViewModel Dashboard { get; }

    public NavItem? SelectedNavItem
    {
        get => _selectedNavItem;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedNavItem, value);
            if (value != null) CurrentContent = value.ContentVm;
        }
    }

    public ReactiveObject? CurrentContent
    {
        get => _currentContent;
        private set => this.RaiseAndSetIfChanged(ref _currentContent, value);
    }

    public AppMainViewModel(MainViewModel mainVm)
    {
        NamespaceName = mainVm.NamespaceName ?? "Service Bus Explorer";
        Dashboard = mainVm.Dashboard;

        NavItems = new ObservableCollection<NavItem>
        {
            new("Dashboard", mainVm.Dashboard),
            new("Queues", mainVm.Queues),
            new("Topics", mainVm.Topics),
            new("Event Hubs", mainVm.EventHubs),
            new("Relays", mainVm.Relays),
            new("Notification Hubs", mainVm.NotificationHubs),
        };

        SelectedNavItem = NavItems[0];
    }
}
