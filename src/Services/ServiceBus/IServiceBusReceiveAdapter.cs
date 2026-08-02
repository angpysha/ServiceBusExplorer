using Azure.Messaging.ServiceBus;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Thin Azure receive seam for unit/contract testing without a live Service Bus namespace.
/// </summary>
public interface IServiceBusReceiveAdapter
{
    /// <summary>
    /// Creates a peek-lock receive session for the mapped sub-queue. When
    /// <paramref name="sessionRequest"/> is non-null, accepts the next or specific Service Bus session.
    /// </summary>
    Task<IReceiveSession> OpenPeekLockAsync(
        string entityPath,
        SubQueue subQueue,
        MessageSource source,
        SessionRequest? sessionRequest = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives and deletes a bounded batch from the mapped sub-queue.
    /// </summary>
    Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAndDeleteAsync(
        string entityPath,
        SubQueue subQueue,
        int maxMessages,
        TimeSpan maxWait,
        CancellationToken cancellationToken);

    /// <summary>
    /// Receives a single deferred message by sequence number from the mapped sub-queue.
    /// </summary>
    Task<ServiceBusReceivedMessage> ReceiveDeferredMessageAsync(
        string entityPath,
        SubQueue subQueue,
        long sequenceNumber,
        CancellationToken cancellationToken);
}
