using Azure.Messaging.ServiceBus;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Thin Azure peek seam for unit testing without a live Service Bus namespace.
/// </summary>
public interface IServiceBusPeekAdapter
{
    Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekMessagesAsync(
        string entityPath,
        SubQueue subQueue,
        int maxCount,
        long? fromSequenceNumber,
        CancellationToken cancellationToken);
}
