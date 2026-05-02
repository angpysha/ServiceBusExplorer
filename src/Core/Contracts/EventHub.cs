namespace ServiceBusExplorer;

public record EventHubInfo(
    string Name,
    string[] PartitionIds,
    DateTimeOffset CreatedAt);

public record ConsumerGroupInfo(
    string EventHubName,
    string Name,
    DateTimeOffset CreatedAt);

public record PartitionInfo(
    string EventHubName,
    string PartitionId,
    long BeginningSequenceNumber,
    long LastEnqueuedSequenceNumber,
    bool IsEmpty,
    DateTimeOffset LastEnqueuedTime);
