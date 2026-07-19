using Azure.Messaging.ServiceBus;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Thin Azure receive seam for unit/contract testing without a live Service Bus namespace.
/// </summary>
public interface IServiceBusReceiveAdapter
{
    /// <summary>
    /// Creates a peek-lock receive session for the mapped sub-queue.
    /// </summary>
    IReceiveSession OpenPeekLock(
        string entityPath,
        SubQueue subQueue,
        MessageSource source);

    /// <summary>
    /// Receives and deletes a bounded batch from the mapped sub-queue.
    /// </summary>
    Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAndDeleteAsync(
        string entityPath,
        SubQueue subQueue,
        int maxMessages,
        TimeSpan maxWait,
        CancellationToken cancellationToken);
}
