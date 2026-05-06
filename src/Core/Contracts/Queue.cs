#nullable enable
namespace ServiceBusExplorer;

public enum EntityStatus { Active, Disabled, SendDisabled, ReceiveDisabled, Unknown }

public record QueueInfo(
    string Name,
    long ActiveMessageCount,
    long DeadLetterCount,
    long ScheduledMessageCount,
    TimeSpan LockDuration,
    bool RequiresDuplicateDetection,
    bool RequiresSession,
    TimeSpan DefaultMessageTimeToLive,
    EntityStatus Status,
    // Extended fields for editing
    TimeSpan AutoDeleteOnIdle = default,
    int MaxDeliveryCount = 10,
    long MaxSizeInMegabytes = 1024,
    bool EnableBatchedOperations = true,
    string? ForwardTo = null,
    string? ForwardDeadLetteredMessagesTo = null,
    string? UserMetadata = null,
    TimeSpan DuplicateDetectionHistoryTimeWindow = default,
    long SizeInBytes = 0,
    bool EnableDeadLetteringOnMessageExpiration = false);

public record CreateQueueOptions(
    string Name,
    TimeSpan? LockDuration = null,
    TimeSpan? DefaultMessageTimeToLive = null,
    bool RequiresDuplicateDetection = false,
    bool RequiresSession = false,
    int? MaxDeliveryCount = null);
