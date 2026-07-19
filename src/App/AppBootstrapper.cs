using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceBusExplorer.App.Services;
using ServiceBusExplorer.App.Services.Credentials;
using ServiceBusExplorer.Services;
using ServiceBusExplorer.ViewModels;

namespace ServiceBusExplorer.App;

public class AppBootstrapper : IDisposable
{
    private readonly IServiceProvider _appServices;
    private readonly IConfirmationService _confirmationService = new AvaloniaConfirmationService();
    private readonly ICredentialVault _credentialVault;
    private readonly IConnectionProfileStore _profileStore;
    private readonly IConnectionContextFactory _connectionContextFactory;
    private IServiceProvider? _connectionServices;
    private LiveConnectionContext? _liveContext;

    public readonly SettingsService Settings = new();
    public readonly ObservableLoggerProvider LogSink = new();

    public AppBootstrapper()
    {
        _credentialVault = CreatePlatformCredentialVault();
        _profileStore = new JsonConnectionProfileStore(Settings, _credentialVault);
        _connectionContextFactory = new ConnectionContextFactory(_credentialVault);

        var sc = new ServiceCollection();
        sc.AddLogging(b => { b.AddConsole(); b.AddProvider(LogSink); });
        sc.AddSingleton(Settings);
        sc.AddSingleton(_confirmationService);
        sc.AddSingleton<ICredentialVault>(_credentialVault);
        sc.AddSingleton<IConnectionProfileStore>(_profileStore);
        sc.AddSingleton<IConnectionContextFactory>(_connectionContextFactory);
        sc.AddTransient<ConnectViewModel>(sp => new ConnectViewModel(
            sp.GetRequiredService<ICredentialVault>(),
            sp.GetRequiredService<IConnectionProfileStore>()));
        _appServices = sc.BuildServiceProvider();
    }

    public T GetAppService<T>() where T : notnull =>
        _appServices.GetRequiredService<T>();

    public async Task<(AppMainViewModel Main, string ProfileId)> ConnectAsync(
        ConnectionRequest request,
        ConnectViewModel connectVm,
        CancellationToken cancellationToken = default)
    {
        var createResult = await _connectionContextFactory
            .CreateAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!createResult.Success)
        {
            if (createResult.RequiresManualSas && createResult.PreservedCredentialReference is not null)
            {
                connectVm.SetVaultRetrieveFailure(
                    createResult.PreservedCredentialReference,
                    createResult.FailureMessage ?? "Enter SAS for this connection.");
            }

            throw new InvalidOperationException(
                createResult.FailureMessage ?? "Connection failed. Check the connection details and try again.");
        }

        if (createResult.Context is null ||
            createResult.ServiceHandles is not ConnectionServiceHandles handles)
        {
            createResult.Context?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw new InvalidOperationException("Connection succeeded but service handles were unavailable.");
        }

        await DisposeLiveContextAsync().ConfigureAwait(false);
        _liveContext = createResult.Context;

        var sc = new ServiceCollection();
        sc.AddLogging(b => { b.AddConsole(); b.AddProvider(LogSink); });
        sc.AddSingleton(_confirmationService);
        sc.AddSingleton(handles.Client);
        if (handles.AdminClient is not null)
            sc.AddSingleton(handles.AdminClient);

        sc.AddSingleton(_liveContext);
        sc.AddSingleton<INamespaceService, NamespaceService>();
        sc.AddSingleton<IQueueService, QueueService>();
        sc.AddSingleton<IServiceBusPeekAdapter, ServiceBusPeekAdapter>();
        sc.AddSingleton<IMessageBrowseService, MessageBrowseService>();
        sc.AddSingleton<IMessageSendService, MessageSendService>();
        sc.AddSingleton<ITopicService, TopicService>();
        sc.AddSingleton<ISubscriptionService, SubscriptionService>();
        sc.AddSingleton<IRelayService, RelayService>();
        sc.AddSingleton<IEventHubService>(sp =>
            new EventHubService(BuildSyntheticConnectionString(request),
                sp.GetRequiredService<ILogger<EventHubService>>()));
        sc.AddSingleton<INotificationHubService>(sp =>
            new NotificationHubService(BuildSyntheticConnectionString(request),
                sp.GetRequiredService<ILogger<NotificationHubService>>()));

        sc.AddSingleton<QueueListViewModel>();
        sc.AddSingleton<TopicListViewModel>();
        sc.AddSingleton<EventHubDetailViewModel>();
        sc.AddSingleton<EventHubListViewModel>();
        sc.AddSingleton<RelayListViewModel>();
        sc.AddSingleton<NotificationHubListViewModel>();
        sc.AddSingleton<DashboardViewModel>();
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<AppMainViewModel>();

        var provider = sc.BuildServiceProvider();
        (_connectionServices as IDisposable)?.Dispose();
        _connectionServices = provider;

        var recordOptions = new ConnectionOptions(
            BuildSyntheticConnectionString(request),
            request.AuthMode,
            request.TenantId,
            request.EntityPath);
        var settings = Settings.RecordConnection(recordOptions);
        var profileId = settings.ConnectionHistory[0].Id;

        return (_connectionServices.GetRequiredService<AppMainViewModel>(), profileId);
    }

    public async Task DisconnectAsync()
    {
        (_connectionServices as IDisposable)?.Dispose();
        _connectionServices = null;
        await DisposeLiveContextAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        (_appServices as IDisposable)?.Dispose();
    }

    private async Task DisposeLiveContextAsync()
    {
        if (_liveContext is null)
            return;

        await _liveContext.DisposeAsync().ConfigureAwait(false);
        _liveContext = null;
    }

    private static ICredentialVault CreatePlatformCredentialVault()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsCredentialVault();
        if (OperatingSystem.IsMacOS())
            return new MacOsCredentialVault();
        if (OperatingSystem.IsLinux())
            return new LinuxCredentialVault();

        throw new PlatformNotSupportedException(
            "Credential vault integration is supported only on Windows, macOS, and Linux.");
    }

    private static string BuildSyntheticConnectionString(ConnectionRequest request)
    {
        if (request.SasCredential is not null)
            return request.SasCredential.Reveal();

        return $"Endpoint=sb://{request.NamespaceEndpoint}/";
    }
}
