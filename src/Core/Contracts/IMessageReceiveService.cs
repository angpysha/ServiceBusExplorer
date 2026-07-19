#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Optional Service Bus session acquisition request for peek-lock receive.
/// Non-null requests are reserved for session-aware receive (T023).
/// </summary>
/// <param name="SessionId">
/// Specific session id to acquire, or <c>null</c> to request the next available session
/// when session-aware receive is enabled.
/// </param>
public sealed record SessionRequest(string? SessionId = null);

/// <summary>
/// Opaque proof that the user confirmed receive-and-delete for a specific entity and source.
/// Can only be created from <see cref="ConfirmationResult.Confirmed"/>.
/// </summary>
public sealed class ReceiveAndDeleteConfirmation
{
    private ReceiveAndDeleteConfirmation(EntityAddress address, MessageSource source)
    {
        Address = address;
        Source = source;
    }

    public EntityAddress Address { get; }
    public MessageSource Source { get; }

    /// <summary>
    /// Creates confirmation evidence only when <paramref name="result"/> is
    /// <see cref="ConfirmationResult.Confirmed"/>.
    /// </summary>
    public static ReceiveAndDeleteConfirmation Create(
        ConfirmationResult result,
        EntityAddress address,
        MessageSource source)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (result != ConfirmationResult.Confirmed)
        {
            throw new InvalidOperationException(
                "Receive-and-delete requires ConfirmationResult.Confirmed from outside this adapter.");
        }

        return new ReceiveAndDeleteConfirmation(address, source);
    }

    /// <summary>
    /// Attempts to create confirmation evidence; returns <c>false</c> when cancelled.
    /// </summary>
    public static bool TryCreate(
        ConfirmationResult result,
        EntityAddress address,
        MessageSource source,
        out ReceiveAndDeleteConfirmation? confirmation)
    {
        if (result != ConfirmationResult.Confirmed)
        {
            confirmation = null;
            return false;
        }

        confirmation = Create(result, address, source);
        return true;
    }
}

/// <summary>
/// Confirmed receive-and-delete batch request. The confirmation token must be produced by the
/// application orchestration layer after <see cref="IConfirmationService"/> completes.
/// </summary>
public sealed record ConfirmedReceiveAndDeleteRequest(
    ReceiveAndDeleteConfirmation Confirmation,
    int MaxMessages,
    TimeSpan? MaxWait = null);

/// <summary>
/// Typed result of a confirmed receive-and-delete batch.
/// Always reports display-loss risk because messages are removed from the broker.
/// </summary>
public sealed record ReceiveAndDeleteResult(
    IReadOnlyList<ReceivedMessage> Messages,
    bool ReportsDisplayLossRisk,
    string SafeMessage);

/// <summary>
/// Application port for explicit-source peek-lock receive and confirmed receive-and-delete.
/// </summary>
public interface IMessageReceiveService
{
    /// <summary>
    /// Opens a peek-lock receive handle for the explicit source. The returned session is
    /// cancellable and async-disposable; dispose releases the underlying receiver.
    /// </summary>
    Task<IReceiveSession> OpenPeekLockAsync(
        EntityAddress address,
        MessageSource source,
        SessionRequest? sessionRequest = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives and permanently deletes a bounded batch. Requires a previously confirmed
    /// operation token; confirmation MUST complete outside this adapter.
    /// </summary>
    Task<ReceiveAndDeleteResult> ReceiveAndDeleteAsync(
        ConfirmedReceiveAndDeleteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects settlement of peeked observed messages without invoking the receive session.
    /// </summary>
    SettlementItemOutcome RejectPeekedSettlement(ObservedMessage message, SettlementAction action);

    /// <summary>Single-attempt complete against the live peek-lock session.</summary>
    Task<SettlementItemOutcome> CompleteAsync(
        IReceiveSession session,
        ReceivedMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>Single-attempt abandon against the live peek-lock session.</summary>
    Task<SettlementItemOutcome> AbandonAsync(
        IReceiveSession session,
        ReceivedMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>Single-attempt defer against the live peek-lock session.</summary>
    Task<SettlementItemOutcome> DeferAsync(
        IReceiveSession session,
        ReceivedMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>Single-attempt dead-letter against the live peek-lock session.</summary>
    Task<SettlementItemOutcome> DeadLetterAsync(
        IReceiveSession session,
        ReceivedMessage message,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Settles each message once for <paramref name="action"/>. Partial outcomes are returned;
    /// confirmed successes MUST NOT be automatically repeated by callers.
    /// </summary>
    Task<SettlementBatchOutcome> SettleBatchAsync(
        IReceiveSession session,
        IReadOnlyList<ReceivedMessage> messages,
        SettlementAction action,
        string? deadLetterReason = null,
        CancellationToken cancellationToken = default);
}
