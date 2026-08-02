#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// A live receive session that holds message locks so they can be settled
/// (completed, abandoned, or dead-lettered) after inspection.
/// Dispose the session when done — outstanding locks expire automatically on Azure.
/// </summary>
public interface IReceiveSession : IAsyncDisposable
{
    /// <summary>Entity path this session was opened against.</summary>
    string EntityPath { get; }

    /// <summary>Explicit message source this session was opened against.</summary>
    MessageSource Source { get; }

    /// <summary>Accepted session id when this is a session receiver; otherwise null.</summary>
    string? SessionId { get; }

    /// <summary>Current session lock expiry when <see cref="IsSessionReceiver"/> is true.</summary>
    DateTimeOffset? SessionLockedUntil { get; }

    /// <summary>True when opened against a session-enabled entity.</summary>
    bool IsSessionReceiver { get; }

    /// <summary>True after the broker reports session lock loss.</summary>
    bool IsSessionLockLost { get; }

    /// <summary>True after <see cref="IAsyncDisposable.DisposeAsync"/> has completed.</summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Cancelled when the session is disposed or aborted. Callers may link work to this token.
    /// </summary>
    CancellationToken SessionAborted { get; }

    /// <summary>Receive up to <paramref name="maxMessages"/> messages with a peek-lock.</summary>
    Task<IReadOnlyList<ReceivedMessage>> ReceiveBatchAsync(
        int maxMessages = 20, TimeSpan? maxWait = null, CancellationToken ct = default);

    /// <summary>
    /// Current settlement state for a received message (refreshes lock expiry).
    /// Unknown tokens are <see cref="SettlementState.Ineligible"/>.
    /// </summary>
    SettlementState GetSettlementState(ReceivedMessage message, DateTimeOffset? utcNow = null);

    /// <summary>Complete (delete) a received message — single attempt, typed outcome.</summary>
    Task<SettlementItemOutcome> CompleteAsync(ReceivedMessage message, CancellationToken ct = default);

    /// <summary>Abandon a message — it becomes visible again for redelivery.</summary>
    Task<SettlementItemOutcome> AbandonAsync(ReceivedMessage message, CancellationToken ct = default);

    /// <summary>Move a message to the dead-letter sub-queue.</summary>
    Task<SettlementItemOutcome> DeadLetterAsync(
        ReceivedMessage message, string? reason = null, CancellationToken ct = default);

    /// <summary>Defer a message — it must be received explicitly by sequence number.</summary>
    Task<SettlementItemOutcome> DeferAsync(ReceivedMessage message, CancellationToken ct = default);

    /// <summary>
    /// Renews the session lock when supported. Returns false when not a session receiver or lock is lost.
    /// </summary>
    Task<bool> TryRenewSessionLockAsync(CancellationToken ct = default);
}
