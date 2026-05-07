using Azure.Messaging.ServiceBus;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Wraps a live <see cref="ServiceBusReceiver"/> in PeekLock mode so that
/// received messages can be settled (completed, abandoned, dead-lettered, deferred)
/// using the same receiver that received them — a requirement of the Azure SDK.
/// </summary>
internal sealed class ReceiveSession : IReceiveSession
{
    private readonly ServiceBusReceiver _receiver;
    // Map lock-token → original SDK message, needed for settlement calls
    private readonly Dictionary<string, ServiceBusReceivedMessage> _pending = new();

    internal ReceiveSession(ServiceBusReceiver receiver) => _receiver = receiver;

    public async Task<IReadOnlyList<ReceivedMessage>> ReceiveBatchAsync(
        int maxMessages = 20, TimeSpan? maxWait = null, CancellationToken ct = default)
    {
        var timeout = maxWait ?? TimeSpan.FromSeconds(3);
        var msgs = await _receiver.ReceiveMessagesAsync(maxMessages, timeout, ct);
        var result = new List<ReceivedMessage>(msgs.Count);
        foreach (var m in msgs)
        {
            _pending[m.LockToken] = m;
            result.Add(MapMessage(m));
        }
        return result;
    }

    public async Task CompleteAsync(ReceivedMessage message, CancellationToken ct = default)
    {
        if (message.LockToken != null && _pending.TryGetValue(message.LockToken, out var m))
        {
            await _receiver.CompleteMessageAsync(m, ct);
            _pending.Remove(message.LockToken);
        }
    }

    public async Task AbandonAsync(ReceivedMessage message, CancellationToken ct = default)
    {
        if (message.LockToken != null && _pending.TryGetValue(message.LockToken, out var m))
        {
            await _receiver.AbandonMessageAsync(m, cancellationToken: ct);
            _pending.Remove(message.LockToken);
        }
    }

    public async Task DeadLetterAsync(ReceivedMessage message, string? reason = null,
        CancellationToken ct = default)
    {
        if (message.LockToken != null && _pending.TryGetValue(message.LockToken, out var m))
        {
            await _receiver.DeadLetterMessageAsync(m, deadLetterReason: reason, cancellationToken: ct);
            _pending.Remove(message.LockToken);
        }
    }

    public async Task DeferAsync(ReceivedMessage message, CancellationToken ct = default)
    {
        if (message.LockToken != null && _pending.TryGetValue(message.LockToken, out var m))
        {
            await _receiver.DeferMessageAsync(m, cancellationToken: ct);
            _pending.Remove(message.LockToken);
        }
    }

    public ValueTask DisposeAsync() => _receiver.DisposeAsync();

    private static ReceivedMessage MapMessage(ServiceBusReceivedMessage m) => new(
        m.MessageId, m.Body.ToString(), m.ContentType ?? "application/octet-stream",
        m.SequenceNumber, m.DeliveryCount, m.EnqueuedTime, m.ExpiresAt,
        m.CorrelationId, m.SessionId,
        m.ApplicationProperties.ToDictionary(kv => kv.Key, kv => kv.Value),
        m.DeadLetterReason, m.LockToken);
}
