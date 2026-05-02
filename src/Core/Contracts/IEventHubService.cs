#nullable enable
namespace ServiceBusExplorer;

public interface IEventHubService
{
    Task<EventHubInfo> GetAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ConsumerGroupInfo>> ListConsumerGroupsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PartitionInfo>> ListPartitionsAsync(CancellationToken ct = default);
    Task<PartitionInfo> GetPartitionAsync(string partitionId, CancellationToken ct = default);
}
