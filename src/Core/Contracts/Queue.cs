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
    EntityStatus Status);

public record CreateQueueOptions(
    string Name,
    TimeSpan? LockDuration = null,
    TimeSpan? DefaultMessageTimeToLive = null,
    bool RequiresDuplicateDetection = false,
    bool RequiresSession = false,
    int? MaxDeliveryCount = null);
