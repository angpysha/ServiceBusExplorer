using ReactiveUI;
using ServiceBusExplorer.ViewModels;

namespace ServiceBusExplorer.App;

public class AppMainViewModel : ReactiveObject
{
    public string NamespaceName { get; }
    public DashboardViewModel Dashboard { get; }
    public NavTreeViewModel Tree { get; }

    public ReactiveObject? CurrentContent => Tree.CurrentContent;

    public AppMainViewModel(MainViewModel mainVm)
    {
        NamespaceName = mainVm.NamespaceName ?? "Service Bus Explorer";
        Dashboard = mainVm.Dashboard;
        Tree = new NavTreeViewModel(mainVm);

        Tree.WhenAnyValue(x => x.CurrentContent)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(CurrentContent)));
    }
}
