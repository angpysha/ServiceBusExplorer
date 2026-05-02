#nullable enable
namespace ServiceBusExplorer;

public interface INotificationHubService
{
    Task<IReadOnlyList<NotificationHubInfo>> ListAsync(CancellationToken ct = default);
    Task<NotificationHubInfo> GetAsync(string name, CancellationToken ct = default);
    Task<NotificationHubInfo> CreateAsync(string name, CancellationToken ct = default);
    Task DeleteAsync(string name, CancellationToken ct = default);
}
