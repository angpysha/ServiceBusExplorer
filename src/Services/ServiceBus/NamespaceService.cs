using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

public class NamespaceService : INamespaceService
{
    private readonly ServiceBusAdministrationClient? _admin;
    private readonly IQueueService? _queueService;
    private readonly ITopicService? _topicService;
    private readonly ILogger<NamespaceService> _log;

    public NamespaceService(
        ILogger<NamespaceService> log,
        ServiceBusAdministrationClient? admin = null,
        IQueueService? queueService = null,
        ITopicService? topicService = null)
    {
        _log = log;
        _admin = admin;
        _queueService = queueService;
        _topicService = topicService;
    }

    public async Task<bool> TestConnectionAsync(ConnectionOptions opts, CancellationToken ct = default)
    {
        try
        {
            var client = new ServiceBusAdministrationClient(opts.ConnectionString);
            await client.GetNamespacePropertiesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Connection test failed");
            return false;
        }
    }

    public async Task<string> GetNamespaceNameAsync(CancellationToken ct = default)
    {
        if (_admin is null)
            return "Service Bus";

        var props = await _admin.GetNamespacePropertiesAsync(ct);
        return props.Value.Name;
    }

    public Task<NamespaceBrowseResult<QueueInfo>> BrowseQueuesAsync(
        NamespaceBrowseRequest request,
        CancellationToken ct = default) =>
        BrowseAsync(
            request,
            BrowseSurface.Queues,
            _queueService is null
                ? null
                : async token => await _queueService.ListAsync(token).ConfigureAwait(false),
            name => CreateScopedQueue(name),
            ct);

    public Task<NamespaceBrowseResult<TopicInfo>> BrowseTopicsAsync(
        NamespaceBrowseRequest request,
        CancellationToken ct = default) =>
        BrowseAsync(
            request,
            BrowseSurface.Topics,
            _topicService is null
                ? null
                : async token => await _topicService.ListAsync(token).ConfigureAwait(false),
            name => CreateScopedTopic(name),
            ct);

    private async Task<NamespaceBrowseResult<T>> BrowseAsync<T>(
        NamespaceBrowseRequest request,
        BrowseSurface surface,
        Func<CancellationToken, Task<IReadOnlyList<T>>>? listNamespaceAsync,
        Func<string, T> createScopedEntity,
        CancellationToken ct)
        where T : class
    {
        ct.ThrowIfCancellationRequested();

        if (request.Surface != surface)
        {
            return NamespaceBrowseResult<T>.Empty(
                "Internal browse surface mismatch.");
        }

        if (request.Scope == ConnectionScope.Entity)
        {
            return BrowseEntityScope(request, surface, createScopedEntity);
        }

        if (!request.Capabilities.CanBrowseEntities)
        {
            return NamespaceBrowseResult<T>.Empty(
                "Browsing is not permitted for this connection.");
        }

        if (listNamespaceAsync is null)
        {
            return NamespaceBrowseResult<T>.Empty(
                "Namespace administration is unavailable for this connection.");
        }

        try
        {
            var items = await listNamespaceAsync(ct).ConfigureAwait(false);
            return NamespaceBrowseResult<T>.FromItems(items);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Namespace browse failed for {Surface}", surface);
            return NamespaceBrowseResult<T>.Empty(
                "Unable to browse the namespace. Check permissions and try again.");
        }
    }

    private static NamespaceBrowseResult<T> BrowseEntityScope<T>(
        NamespaceBrowseRequest request,
        BrowseSurface surface,
        Func<string, T> createScopedEntity)
        where T : class
    {
        var kind = EntityScopeHelper.ParseKind(request.EntityPath, request.EntityKind);
        if (!EntityScopeHelper.PermitsSurface(kind, surface))
            return NamespaceBrowseResult<T>.Empty();

        var entityName = EntityScopeHelper.GetRootEntityName(request.EntityPath);
        if (string.IsNullOrWhiteSpace(entityName))
        {
            return NamespaceBrowseResult<T>.Empty(
                "Entity path is required for entity-scoped browsing.");
        }

        return NamespaceBrowseResult<T>.FromItems([createScopedEntity(entityName)]);
    }

    private static QueueInfo CreateScopedQueue(string name) =>
        new(
            name,
            ActiveMessageCount: 0,
            DeadLetterCount: 0,
            ScheduledMessageCount: 0,
            LockDuration: TimeSpan.FromMinutes(1),
            RequiresDuplicateDetection: false,
            RequiresSession: false,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            Status: EntityStatus.Unknown);

    private static TopicInfo CreateScopedTopic(string name) =>
        new(
            name,
            SubscriptionCount: 0,
            SizeInBytes: 0,
            EnableBatchedOperations: true,
            EnablePartitioning: false,
            Status: EntityStatus.Unknown);
}
