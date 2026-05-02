using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

public class EventHubService : IEventHubService
{
    private readonly string _connectionString;
    private readonly ILogger<EventHubService> _log;

    public EventHubService(string connectionString, ILogger<EventHubService> log)
    {
        _connectionString = connectionString;
        _log = log;
    }

    public async Task<EventHubInfo> GetAsync(CancellationToken ct = default)
    {
        await using var producer = new EventHubProducerClient(_connectionString);
        var props = await producer.GetEventHubPropertiesAsync(ct);
        return new EventHubInfo(props.Name, props.PartitionIds, props.CreatedOn);
    }

    public async Task<IReadOnlyList<ConsumerGroupInfo>> ListConsumerGroupsAsync(CancellationToken ct = default)
    {
        await using var consumer = new EventHubConsumerClient(
            EventHubConsumerClient.DefaultConsumerGroupName, _connectionString);
        var props = await consumer.GetEventHubPropertiesAsync(ct);
        return props.PartitionIds.Select(_ =>
            new ConsumerGroupInfo(props.Name, EventHubConsumerClient.DefaultConsumerGroupName, props.CreatedOn))
            .Take(1).ToList();
    }

    public async Task<IReadOnlyList<PartitionInfo>> ListPartitionsAsync(CancellationToken ct = default)
    {
        await using var producer = new EventHubProducerClient(_connectionString);
        var props = await producer.GetEventHubPropertiesAsync(ct);
        var results = new List<PartitionInfo>();
        foreach (var id in props.PartitionIds)
            results.Add(await GetPartitionAsync(id, ct));
        return results;
    }

    public async Task<PartitionInfo> GetPartitionAsync(string partitionId, CancellationToken ct = default)
    {
        await using var producer = new EventHubProducerClient(_connectionString);
        var hubProps = await producer.GetEventHubPropertiesAsync(ct);
        var partProps = await producer.GetPartitionPropertiesAsync(partitionId, ct);
        return new PartitionInfo(
            hubProps.Name, partitionId,
            partProps.BeginningSequenceNumber, partProps.LastEnqueuedSequenceNumber,
            partProps.IsEmpty, partProps.LastEnqueuedTime);
    }
}
