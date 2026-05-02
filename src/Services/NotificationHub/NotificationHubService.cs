using Microsoft.Azure.NotificationHubs;
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

public class NotificationHubService : INotificationHubService
{
    private readonly NamespaceManager _nsManager;
    private readonly ILogger<NotificationHubService> _log;

    public NotificationHubService(string connectionString, ILogger<NotificationHubService> log)
    {
        _nsManager = NamespaceManager.CreateFromConnectionString(connectionString);
        _log = log;
    }

    public async Task<IReadOnlyList<NotificationHubInfo>> ListAsync(CancellationToken ct = default)
    {
        var hubs = await _nsManager.GetNotificationHubsAsync();
        return hubs.Select(Map).ToList();
    }

    public async Task<NotificationHubInfo> GetAsync(string name, CancellationToken ct = default)
    {
        var hub = await _nsManager.GetNotificationHubAsync(name);
        return Map(hub);
    }

    public async Task<NotificationHubInfo> CreateAsync(string name, CancellationToken ct = default)
    {
        var description = new NotificationHubDescription(name);
        var created = await _nsManager.CreateNotificationHubAsync(description);
        return Map(created);
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default) =>
        await _nsManager.DeleteNotificationHubAsync(name);

    private static NotificationHubInfo Map(NotificationHubDescription h) => new(
        h.Path, h.Path,
        h.ApnsCredential?.Endpoint,
        h.FcmCredential?.GoogleApiKey,
        0);
}
