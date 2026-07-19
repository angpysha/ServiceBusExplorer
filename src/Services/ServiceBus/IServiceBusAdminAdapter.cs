#nullable enable
namespace ServiceBusExplorer.Services;

/// <summary>
/// Versioned queue/topic administration seam for unit/contract testing without a live namespace.
/// </summary>
public interface IServiceBusAdminAdapter
{
    Task<IReadOnlyList<QueueAdminSnapshot>> ListQueuesAsync(CancellationToken cancellationToken);
    Task<QueueAdminSnapshot?> GetQueueAsync(string name, CancellationToken cancellationToken);
    Task<QueueAdminSnapshot> CreateQueueAsync(QueueAdminCreateRequest request, CancellationToken cancellationToken);
    Task<QueueAdminSnapshot> UpdateQueueAsync(QueueAdminSnapshot snapshot, CancellationToken cancellationToken);
    Task DeleteQueueAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<TopicAdminSnapshot>> ListTopicsAsync(CancellationToken cancellationToken);
    Task<TopicAdminSnapshot?> GetTopicAsync(string name, CancellationToken cancellationToken);
    Task<TopicAdminSnapshot> CreateTopicAsync(TopicAdminCreateRequest request, CancellationToken cancellationToken);
    Task<TopicAdminSnapshot> UpdateTopicAsync(TopicAdminSnapshot snapshot, CancellationToken cancellationToken);
    Task DeleteTopicAsync(string name, CancellationToken cancellationToken);
}

/// <summary>Authoritative queue properties plus opaque service version.</summary>
public sealed record QueueAdminSnapshot(
    string Name,
    string ServiceVersion,
    TimeSpan LockDuration,
    TimeSpan DefaultMessageTimeToLive,
    TimeSpan AutoDeleteOnIdle,
    bool RequiresDuplicateDetection,
    bool RequiresSession,
    int MaxDeliveryCount,
    long MaxSizeInMegabytes,
    bool EnableBatchedOperations,
    bool EnableDeadLetteringOnMessageExpiration,
    string? ForwardTo,
    string? ForwardDeadLetteredMessagesTo,
    string? UserMetadata,
    TimeSpan DuplicateDetectionHistoryTimeWindow,
    EntityStatus Status,
    long ActiveMessageCount,
    long DeadLetterCount,
    long ScheduledMessageCount,
    long SizeInBytes);

public sealed record QueueAdminCreateRequest(
    string Name,
    TimeSpan? LockDuration,
    TimeSpan? DefaultMessageTimeToLive,
    bool RequiresDuplicateDetection,
    bool RequiresSession,
    int? MaxDeliveryCount);

/// <summary>Authoritative topic properties plus opaque service version.</summary>
public sealed record TopicAdminSnapshot(
    string Name,
    string ServiceVersion,
    TimeSpan DefaultMessageTimeToLive,
    TimeSpan AutoDeleteOnIdle,
    long MaxSizeInMegabytes,
    bool EnableBatchedOperations,
    bool EnablePartitioning,
    bool RequiresDuplicateDetection,
    TimeSpan DuplicateDetectionHistoryTimeWindow,
    string? UserMetadata,
    EntityStatus Status,
    int SubscriptionCount,
    long SizeInBytes);

public sealed record TopicAdminCreateRequest(
    string Name,
    TimeSpan? DefaultMessageTimeToLive,
    bool EnableBatchedOperations,
    bool EnablePartitioning);
