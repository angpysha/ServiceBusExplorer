#nullable enable
namespace ServiceBusExplorer;

public record TopicInfo(
    string Name,
    int SubscriptionCount,
    long SizeInBytes,
    bool EnableBatchedOperations,
    bool EnablePartitioning,
    EntityStatus Status);

public record CreateTopicOptions(
    string Name,
    TimeSpan? DefaultMessageTimeToLive = null,
    bool EnableBatchedOperations = true,
    bool EnablePartitioning = false);
