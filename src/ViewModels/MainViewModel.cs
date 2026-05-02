using System.Reactive;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class MainViewModel : ReactiveObject
{
    private ConnectionOptions? _connection;
    private bool _isConnected;
    private string? _namespaceName;
    private string? _errorMessage;

    public ConnectionOptions? Connection
    {
        get => _connection;
        set => this.RaiseAndSetIfChanged(ref _connection, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set => this.RaiseAndSetIfChanged(ref _isConnected, value);
    }

    public string? NamespaceName
    {
        get => _namespaceName;
        private set => this.RaiseAndSetIfChanged(ref _namespaceName, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public QueueListViewModel Queues { get; }
    public TopicListViewModel Topics { get; }
    public EventHubListViewModel EventHubs { get; }
    public RelayListViewModel Relays { get; }
    public NotificationHubListViewModel NotificationHubs { get; }
    public DashboardViewModel Dashboard { get; }

    public ReactiveCommand<ConnectionOptions, Unit> ConnectCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public MainViewModel(
        INamespaceService namespaceService,
        QueueListViewModel queues,
        TopicListViewModel topics,
        EventHubListViewModel eventHubs,
        RelayListViewModel relays,
        NotificationHubListViewModel notificationHubs,
        DashboardViewModel dashboard)
    {
        Queues = queues;
        Topics = topics;
        EventHubs = eventHubs;
        Relays = relays;
        NotificationHubs = notificationHubs;
        Dashboard = dashboard;

        ConnectCommand = ReactiveCommand.CreateFromTask<ConnectionOptions, Unit>(async opts =>
        {
            ErrorMessage = null;
            try
            {
                var ok = await namespaceService.TestConnectionAsync(opts);
                if (ok)
                {
                    Connection = opts;
                    IsConnected = true;
                    NamespaceName = await namespaceService.GetNamespaceNameAsync();
                }
                else
                {
                    ErrorMessage = "Connection failed. Check the connection string and try again.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                IsConnected = false;
            }
            return Unit.Default;
        });

        RefreshCommand = ReactiveCommand.CreateFromTask(() =>
        {
            Queues.RefreshCommand.Execute().Subscribe();
            Topics.RefreshCommand.Execute().Subscribe();
            return Task.CompletedTask;
        });
    }
}
