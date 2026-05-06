#nullable enable
namespace ServiceBusExplorer;

public record TopicInfo(
    string Name,
    int SubscriptionCount,
    long SizeInBytes,
    bool EnableBatchedOperations,
    bool EnablePartitioning,
    EntityStatus Status,
    // Extended fields for editing
    TimeSpan DefaultMessageTimeToLive = default,
    TimeSpan AutoDeleteOnIdle = default,
    long MaxSizeInMegabytes = 1024,
    string? UserMetadata = null,
    TimeSpan DuplicateDetectionHistoryTimeWindow = default,
    bool RequiresDuplicateDetection = false);

public record CreateTopicOptions(
    string Name,
    TimeSpan? DefaultMessageTimeToLive = null,
    bool EnableBatchedOperations = true,
    bool EnablePartitioning = false);
