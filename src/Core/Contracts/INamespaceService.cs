#nullable enable
namespace ServiceBusExplorer;

public interface INamespaceService
{
    Task<bool> TestConnectionAsync(ConnectionOptions opts, CancellationToken ct = default);

    Task<string> GetNamespaceNameAsync(CancellationToken ct = default);

    Task<NamespaceBrowseResult<QueueInfo>> BrowseQueuesAsync(
        NamespaceBrowseRequest request,
        CancellationToken ct = default);

    Task<NamespaceBrowseResult<TopicInfo>> BrowseTopicsAsync(
        NamespaceBrowseRequest request,
        CancellationToken ct = default);
}
