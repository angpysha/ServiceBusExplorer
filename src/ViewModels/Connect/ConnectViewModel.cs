using System.Reactive;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class ConnectViewModel : ReactiveObject
{
    private string _connectionString = "";
    private ServiceBusAuthMode _authMode = ServiceBusAuthMode.Sas;
    private string? _tenantId;
    private string? _entityPath;
    private bool _loadQueues = true;
    private bool _loadTopics = true;
    private bool _loadEventHubs = true;
    private bool _loadRelays = true;
    private bool _loadNotificationHubs = true;
    private bool _isConnecting;
    private string? _errorMessage;

    public string ConnectionString
    {
        get => _connectionString;
        set => this.RaiseAndSetIfChanged(ref _connectionString, value);
    }

    public ServiceBusAuthMode AuthMode
    {
        get => _authMode;
        set => this.RaiseAndSetIfChanged(ref _authMode, value);
    }

    public string? TenantId
    {
        get => _tenantId;
        set => this.RaiseAndSetIfChanged(ref _tenantId, value);
    }

    public string? EntityPath
    {
        get => _entityPath;
        set => this.RaiseAndSetIfChanged(ref _entityPath, value);
    }

    public bool LoadQueues
    {
        get => _loadQueues;
        set => this.RaiseAndSetIfChanged(ref _loadQueues, value);
    }

    public bool LoadTopics
    {
        get => _loadTopics;
        set => this.RaiseAndSetIfChanged(ref _loadTopics, value);
    }

    public bool LoadEventHubs
    {
        get => _loadEventHubs;
        set => this.RaiseAndSetIfChanged(ref _loadEventHubs, value);
    }

    public bool LoadRelays
    {
        get => _loadRelays;
        set => this.RaiseAndSetIfChanged(ref _loadRelays, value);
    }

    public bool LoadNotificationHubs
    {
        get => _loadNotificationHubs;
        set => this.RaiseAndSetIfChanged(ref _loadNotificationHubs, value);
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set => this.RaiseAndSetIfChanged(ref _isConnecting, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public static IReadOnlyList<ServiceBusAuthMode> AuthModes { get; } =
        Enum.GetValues<ServiceBusAuthMode>();

    public ReactiveCommand<Unit, ConnectionOptions> ConnectCommand { get; }

    public ConnectViewModel()
    {
        var canConnect = this.WhenAnyValue(
            x => x.ConnectionString,
            x => x.IsConnecting,
            (cs, connecting) => !string.IsNullOrWhiteSpace(cs) && !connecting);

        ConnectCommand = ReactiveCommand.Create(
            () => new ConnectionOptions(ConnectionString, AuthMode, TenantId, EntityPath),
            canConnect);
    }
}
