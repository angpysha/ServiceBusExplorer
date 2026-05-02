using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceBusExplorer.Services;
using ServiceBusExplorer.ViewModels;

namespace ServiceBusExplorer.App;

public class AppBootstrapper : IDisposable
{
    private readonly IServiceProvider _appServices;
    private IServiceProvider? _connectionServices;

    public AppBootstrapper()
    {
        var sc = new ServiceCollection();
        sc.AddLogging(b => b.AddConsole());
        sc.AddTransient<ConnectViewModel>();
        _appServices = sc.BuildServiceProvider();
    }

    public T GetAppService<T>() where T : notnull =>
        _appServices.GetRequiredService<T>();

    public async Task<AppMainViewModel> ConnectAsync(ConnectionOptions opts)
    {
        var sc = new ServiceCollection();
        sc.AddLogging(b => b.AddConsole());

        sc.AddSingleton(_ => new ServiceBusAdministrationClient(opts.ConnectionString));
        sc.AddSingleton(_ => new ServiceBusClient(opts.ConnectionString));

        sc.AddSingleton<INamespaceService, NamespaceService>();
        sc.AddSingleton<IQueueService, QueueService>();
        sc.AddSingleton<ITopicService, TopicService>();
        sc.AddSingleton<ISubscriptionService, SubscriptionService>();
        sc.AddSingleton<IRelayService, RelayService>();
        sc.AddSingleton<IEventHubService>(sp =>
            new EventHubService(opts.ConnectionString,
                sp.GetRequiredService<ILogger<EventHubService>>()));
        sc.AddSingleton<INotificationHubService>(sp =>
            new NotificationHubService(opts.ConnectionString,
                sp.GetRequiredService<ILogger<NotificationHubService>>()));

        sc.AddSingleton<QueueListViewModel>();
        sc.AddSingleton<TopicListViewModel>();
        sc.AddSingleton<EventHubListViewModel>();
        sc.AddSingleton<RelayListViewModel>();
        sc.AddSingleton<NotificationHubListViewModel>();
        sc.AddSingleton<DashboardViewModel>();
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<AppMainViewModel>();

        var provider = sc.BuildServiceProvider();

        var nsService = provider.GetRequiredService<INamespaceService>();
        var ok = await nsService.TestConnectionAsync(opts);
        if (!ok)
        {
            (provider as IDisposable)?.Dispose();
            throw new InvalidOperationException("Connection failed. Check the connection string and try again.");
        }

        (_connectionServices as IDisposable)?.Dispose();
        _connectionServices = provider;

        return _connectionServices.GetRequiredService<AppMainViewModel>();
    }

    public void Dispose()
    {
        (_connectionServices as IDisposable)?.Dispose();
        (_appServices as IDisposable)?.Dispose();
    }
}
