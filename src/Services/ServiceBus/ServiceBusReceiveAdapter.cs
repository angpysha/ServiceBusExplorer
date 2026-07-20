using Azure.Messaging.ServiceBus;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Production Azure receive adapter using <see cref="ServiceBusClient"/>.
/// </summary>
public sealed class ServiceBusReceiveAdapter(ServiceBusClient client) : IServiceBusReceiveAdapter
{
    public async Task<IReceiveSession> OpenPeekLockAsync(
        string entityPath,
        SubQueue subQueue,
        MessageSource source,
        SessionRequest? sessionRequest = null,
        CancellationToken cancellationToken = default)
    {
        var options = new ServiceBusReceiverOptions
        {
            SubQueue = subQueue,
            ReceiveMode = ServiceBusReceiveMode.PeekLock
        };

        if (sessionRequest is null)
        {
            var receiver = client.CreateReceiver(entityPath, options);
            return new ReceiveSession(receiver, entityPath, source);
        }

        if (subQueue != SubQueue.None)
        {
            throw new InvalidOperationException(
                "Session-aware receive is only supported for the active source.");
        }

        var sessionOptions = new ServiceBusSessionReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock
        };

        ServiceBusSessionReceiver sessionReceiver = sessionRequest.SessionId is { } sessionId
            ? await client.AcceptSessionAsync(entityPath, sessionId, sessionOptions, cancellationToken)
            : await client.AcceptNextSessionAsync(entityPath, sessionOptions, cancellationToken);

        return new ReceiveSession(sessionReceiver, entityPath, source);
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

    public async Task<ServiceBusReceivedMessage> ReceiveDeferredMessageAsync(
        string entityPath,
        SubQueue subQueue,
        long sequenceNumber,
        CancellationToken cancellationToken)
    {
        await using var receiver = client.CreateReceiver(
            entityPath,
            new ServiceBusReceiverOptions
            {
                SubQueue = subQueue,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });
        return await receiver.ReceiveDeferredMessageAsync(sequenceNumber, cancellationToken);
    }
}
