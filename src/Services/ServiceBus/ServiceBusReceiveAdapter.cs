using Azure.Messaging.ServiceBus;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Production Azure receive adapter using <see cref="ServiceBusClient"/>.
/// </summary>
public sealed class ServiceBusReceiveAdapter(ServiceBusClient client) : IServiceBusReceiveAdapter
{
    public IReceiveSession OpenPeekLock(
        string entityPath,
        SubQueue subQueue,
        MessageSource source)
    {
        var receiver = client.CreateReceiver(
            entityPath,
            new ServiceBusReceiverOptions
            {
                SubQueue = subQueue,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });
        return new ReceiveSession(receiver, entityPath, source);
    }

    public async Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAndDeleteAsync(
        string entityPath,
        SubQueue subQueue,
        int maxMessages,
        TimeSpan maxWait,
        CancellationToken cancellationToken)
    {
        await using var receiver = client.CreateReceiver(
            entityPath,
            new ServiceBusReceiverOptions
            {
                SubQueue = subQueue,
                ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
            });
        return await receiver.ReceiveMessagesAsync(maxMessages, maxWait, cancellationToken);
    }
}
