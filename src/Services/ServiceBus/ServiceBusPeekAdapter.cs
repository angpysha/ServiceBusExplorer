using Azure.Messaging.ServiceBus;

namespace ServiceBusExplorer.Services;

public sealed class ServiceBusPeekAdapter(ServiceBusClient client) : IServiceBusPeekAdapter
{
    public async Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekMessagesAsync(
        string entityPath,
        SubQueue subQueue,
        int maxCount,
        long? fromSequenceNumber,
        CancellationToken cancellationToken)
    {
        await using var receiver = client.CreateReceiver(
            entityPath,
            new ServiceBusReceiverOptions { SubQueue = subQueue });
        var messages = await receiver.PeekMessagesAsync(
            maxCount,
            fromSequenceNumber,
            cancellationToken);
        return messages;
    }
}
