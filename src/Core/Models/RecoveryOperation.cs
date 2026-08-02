#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Per-item outcome kind for selected-message recovery.
/// </summary>
public enum RecoveryItemResultKind
{
    /// <summary>
    /// Replacement send succeeded and the original was settled (confirmed).
    /// </summary>
    Succeeded,

    /// <summary>
    /// Replacement send failed; original was not settled.
    /// </summary>
    Failed,

    /// <summary>
    /// The operator cancelled the operation before a confirmed end state.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Replacement send may have succeeded, but original settlement was not confirmed.
    /// </summary>
    Uncertain
}

/// <summary>
/// Identity of a selected original message to be retried.
/// </summary>
public sealed record RecoveryItemIdentity(
    string MessageId,
    long SequenceNumber);

/// <summary>
/// Safe per-item outcome for selected-message recovery.
/// </summary>
public sealed record RecoveryItemOutcome(
    string MessageId,
    long SequenceNumber,
    RecoveryItemResultKind Result,
    string SafeMessage);

/// <summary>
/// Manual retry request: items that were not confirmed succeeded.
/// </summary>
public sealed record RecoveryRetryRequest(
    IReadOnlyList<RecoveryItemIdentity> Items);

/// <summary>
/// Typed recovery result: per-item outcomes + retry candidates.
/// </summary>
public sealed record RecoveryOperation(
    OperationOutcome Outcome,
    IReadOnlyList<RecoveryItemOutcome> Items,
    RecoveryRetryRequest RetryRequest);

