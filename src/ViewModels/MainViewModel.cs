#nullable enable
using System.Reactive;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class MainViewModel : ReactiveObject
{
    private readonly INamespaceService _namespaceService;
    private ConnectionOptions? _connection;
    private bool _isConnected;
    private string? _namespaceName;
    private string? _errorMessage;
    private ConnectionScope _scope = ConnectionScope.Namespace;
    private CapabilitySet _capabilities = CapabilitySet.ForNamespaceScope(adminProbeSucceeded: false);
    private string? _scopedEntityPath;
    private ScopedEntityKind _entityKind = ScopedEntityKind.None;

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

    public ConnectionScope Scope
    {
        get => _scope;
        private set
        {
            this.RaiseAndSetIfChanged(ref _scope, value);
            this.RaisePropertyChanged(nameof(IsEntityScoped));
            this.RaisePropertyChanged(nameof(ScopeBannerText));
        }
    }

    public CapabilitySet Capabilities
    {
        get => _capabilities;
        private set
        {
            this.RaiseAndSetIfChanged(ref _capabilities, value);
            this.RaisePropertyChanged(nameof(CanBrowseNamespace));
            this.RaisePropertyChanged(nameof(ShowQueuesPanel));
            this.RaisePropertyChanged(nameof(ShowTopicsPanel));
        }
    }

    public string? ScopedEntityPath
    {
        get => _scopedEntityPath;
        private set
        {
            this.RaiseAndSetIfChanged(ref _scopedEntityPath, value);
            this.RaisePropertyChanged(nameof(IsEntityScoped));
            this.RaisePropertyChanged(nameof(ScopeBannerText));
        }
    }

    public ScopedEntityKind EntityKind
    {
        get => _entityKind;
        private set
        {
            this.RaiseAndSetIfChanged(ref _entityKind, value);
            this.RaisePropertyChanged(nameof(ShowQueuesPanel));
            this.RaisePropertyChanged(nameof(ShowTopicsPanel));
        }
    }

    public bool IsEntityScoped => Scope == ConnectionScope.Entity;

    public bool CanBrowseNamespace => Capabilities.CanBrowseEntities;

    public bool ShowQueuesPanel =>
        Capabilities.CanBrowseEntities || EntityScopeHelper.PermitsSurface(EntityKind, BrowseSurface.Queues);

    public bool ShowTopicsPanel =>
        Capabilities.CanBrowseEntities || EntityScopeHelper.PermitsSurface(EntityKind, BrowseSurface.Topics);

    public string ScopeBannerText =>
        IsEntityScoped
            ? $"Entity scope: {ScopedEntityPath}"
            : "Namespace scope";

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
        DashboardViewModel dashboard,
        LiveConnectionContext? liveContext = null)
    {
        _namespaceService = namespaceService;
        Queues = queues;
        Topics = topics;
        EventHubs = eventHubs;
        Relays = relays;
        NotificationHubs = notificationHubs;
        Dashboard = dashboard;

        if (liveContext is not null)
            ApplyConnectionScope(liveContext);

        ConnectCommand = ReactiveCommand.CreateFromTask<ConnectionOptions, Unit>(async opts =>
        {
            ErrorMessage = null;
            try
            {
                var ok = await _namespaceService.TestConnectionAsync(opts);
                if (ok)
                {
                    Connection = opts;
                    IsConnected = true;
                    NamespaceName = await _namespaceService.GetNamespaceNameAsync();
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

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAllowedSurfacesAsync);
    }

    public void ApplyConnectionScope(LiveConnectionContext context, ScopedEntityKind? entityKind = null)
    {
        Scope = context.Scope;
        Capabilities = context.Capabilities;
        ScopedEntityPath = context.EntityPath;
        EntityKind = EntityScopeHelper.ParseKind(context.EntityPath, entityKind);
        IsConnected = context.State == ConnectionState.Connected;
        NamespaceName ??= context.NamespaceEndpoint;

        Queues.ApplyConnectionScope(Scope, ScopedEntityPath, Capabilities, EntityKind);
        Topics.ApplyConnectionScope(Scope, ScopedEntityPath, Capabilities, EntityKind);

        if (!ShowQueuesPanel)
            Queues.ClearBrowseResults();
        if (!ShowTopicsPanel)
            Topics.ClearBrowseResults();
    }

    private async Task RefreshAllowedSurfacesAsync()
    {
        if (ShowQueuesPanel)
            Queues.RefreshCommand.Execute().Subscribe();

        if (ShowTopicsPanel)
            Topics.RefreshCommand.Execute().Subscribe();

        await Task.CompletedTask;
    }
}
