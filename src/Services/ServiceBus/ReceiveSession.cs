using Azure.Messaging.ServiceBus;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Wraps a live <see cref="ServiceBusReceiver"/> in PeekLock mode so that
/// received messages can be settled (completed, abandoned, dead-lettered, deferred)
/// using the same receiver that received them — a requirement of the Azure SDK.
/// Enforces settlement eligibility: peeked/unknown, expired, lost, and terminal locks
/// are rejected without a second broker attempt.
/// </summary>
internal sealed class ReceiveSession : IReceiveSession
{
    private readonly ServiceBusReceiver _receiver;
    private readonly ServiceBusSessionReceiver? _sessionReceiver;
    private readonly Dictionary<string, ServiceBusReceivedMessage> _pending = new();
    private readonly SettlementTracker _tracker = new();
    private readonly CancellationTokenSource _abortCts = new();
    private int _disposed;
    private int _sessionLockLost;

    internal ReceiveSession(
        ServiceBusReceiver receiver,
        string entityPath,
        MessageSource source)
    {
        _receiver = receiver;
        _sessionReceiver = receiver as ServiceBusSessionReceiver;
        EntityPath = entityPath;
        Source = source;
    }

    /// <summary>
    /// Backward-compatible constructor for callers that only supply a receiver.
    /// Prefer the overload that records entity path and source.
    /// </summary>
    internal ReceiveSession(ServiceBusReceiver receiver)
        : this(receiver, receiver.EntityPath, MessageSource.Active)
    {
    }

    public string EntityPath { get; }

    public MessageSource Source { get; }

    public string? SessionId => _sessionReceiver?.SessionId;

    public DateTimeOffset? SessionLockedUntil => _sessionReceiver?.SessionLockedUntil;

    public bool IsSessionReceiver => _sessionReceiver is not null;

    public bool IsSessionLockLost => Volatile.Read(ref _sessionLockLost) != 0;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public CancellationToken SessionAborted =>
        IsDisposed ? new CancellationToken(canceled: true) : _abortCts.Token;

    public async Task<IReadOnlyList<ReceivedMessage>> ReceiveBatchAsync(
        int maxMessages = 20, TimeSpan? maxWait = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _abortCts.Token);
        var timeout = maxWait ?? TimeSpan.FromSeconds(3);
        try
        {
            var msgs = await _receiver.ReceiveMessagesAsync(maxMessages, timeout, linked.Token);
            var result = new List<ReceivedMessage>(msgs.Count);
            foreach (var m in msgs)
            {
                _pending[m.LockToken] = m;
                _tracker.Register(m.LockToken, m.LockedUntil);
                result.Add(MapMessage(m));
            }

            return result;
        }
        catch (ServiceBusException ex) when (IsSessionLockFailure(ex))
        {
            MarkSessionLockLost();
            throw;
        }
    }

    public SettlementState GetSettlementState(ReceivedMessage message, DateTimeOffset? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _tracker.GetState(message.LockToken, utcNow ?? DateTimeOffset.UtcNow);
    }

    public Task<SettlementItemOutcome> CompleteAsync(
        ReceivedMessage message, CancellationToken ct = default) =>
        SettleAsync(message, SettlementAction.Complete, ct,
            (sdk, token) => _receiver.CompleteMessageAsync(sdk, token));

    public Task<SettlementItemOutcome> AbandonAsync(
        ReceivedMessage message, CancellationToken ct = default) =>
        SettleAsync(message, SettlementAction.Abandon, ct,
            (sdk, token) => _receiver.AbandonMessageAsync(sdk, cancellationToken: token));

    public Task<SettlementItemOutcome> DeadLetterAsync(
        ReceivedMessage message, string? reason = null, CancellationToken ct = default) =>
        SettleAsync(message, SettlementAction.DeadLetter, ct,
            (sdk, token) => _receiver.DeadLetterMessageAsync(
                sdk, deadLetterReason: reason, cancellationToken: token));

    public Task<SettlementItemOutcome> DeferAsync(
        ReceivedMessage message, CancellationToken ct = default) =>
        SettleAsync(message, SettlementAction.Defer, ct,
            (sdk, token) => _receiver.DeferMessageAsync(sdk, cancellationToken: token));

    public async Task<bool> TryRenewSessionLockAsync(CancellationToken ct = default)
    {
        if (_sessionReceiver is null || IsSessionLockLost)
        {
            return false;
        }

        ThrowIfDisposed();
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _abortCts.Token);
            await _sessionReceiver.RenewSessionLockAsync(linked.Token);
            return true;
        }
        catch (ServiceBusException ex) when (IsSessionLockFailure(ex))
        {
            MarkSessionLockLost();
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            _abortCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }

        await _receiver.DisposeAsync();
        _abortCts.Dispose();
    }

    private async Task<SettlementItemOutcome> SettleAsync(
        ReceivedMessage message,
        SettlementAction action,
        CancellationToken ct,
        Func<ServiceBusReceivedMessage, CancellationToken, Task> brokerSettle)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(message);

        if (IsSessionLockLost)
        {
            return _tracker.MarkFailed(
                message,
                action,
                "Session lock was lost. Further message actions are disabled.");
        }

        var rejection = _tracker.TryBegin(message, action, DateTimeOffset.UtcNow);
        if (rejection is not null)
            return rejection;

        if (message.LockToken is null || !_pending.TryGetValue(message.LockToken, out var sdkMessage))
        {
            return _tracker.MarkFailed(
                message,
                action,
                "Message lock is not held by this receive session.");
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _abortCts.Token);
            await brokerSettle(sdkMessage, linked.Token);
            _pending.Remove(message.LockToken);
            return _tracker.MarkSucceeded(message, action);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessageLockLost)
        {
            _pending.Remove(message.LockToken);
            return _tracker.MarkLockLost(message, action);
        }
        catch (ServiceBusException ex) when (IsSessionLockFailure(ex))
        {
            MarkSessionLockLost();
            return _tracker.MarkFailed(
                message,
                action,
                "Session lock was lost. Further message actions are disabled.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return _tracker.MarkFailed(message, action, "Settlement failed: " + ex.Message);
        }
    }

    private void MarkSessionLockLost() => Interlocked.Exchange(ref _sessionLockLost, 1);

    private static bool IsSessionLockFailure(ServiceBusException ex) =>
        ex.Reason == ServiceBusFailureReason.SessionLockLost;

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(ReceiveSession));
    }

    private static ReceivedMessage MapMessage(ServiceBusReceivedMessage m) => new(
        m.MessageId, m.Body.ToString(), m.ContentType ?? "application/octet-stream",
        m.SequenceNumber, m.DeliveryCount, m.EnqueuedTime, m.ExpiresAt,
        m.CorrelationId, m.SessionId,
        m.ApplicationProperties.ToDictionary(kv => kv.Key, kv => kv.Value),
        m.DeadLetterReason, m.LockToken);
}
