#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// How a peeked message body is represented for safe display.
/// </summary>
public enum MessageBodyKind
{
    Text,
    Json,
    Binary,
    Truncated,
    Unavailable,
    Empty
}

/// <summary>
/// Non-destructive receive classification for an observed message.
/// </summary>
public enum MessageReceiveKind
{
    Peeked,
    Locked
}

/// <summary>
/// Availability of a message source for browse operations.
/// </summary>
public enum SourceAvailability
{
    Available,
    Empty,
    Unavailable
}

/// <summary>
/// Safe, bounded body representation for display and deliberate copy.
/// </summary>
public sealed record MessageBodyRepresentation(
    MessageBodyKind Kind,
    string? DisplayText,
    long? FullLengthBytes = null,
    string? ContentType = null);

/// <summary>
/// A non-destructively observed Service Bus message with explicit source tagging.
/// </summary>
public sealed record ObservedMessage(
    string MessageId,
    MessageSource Source,
    MessageReceiveKind ReceiveKind,
    long SequenceNumber,
    int DeliveryCount,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? ScheduledEnqueueTime,
    MessageBodyRepresentation Body,
    IReadOnlyDictionary<string, object> Properties,
    string? SessionId,
    string? CorrelationId = null,
    string? DeadLetterReason = null,
    SettlementState SettlementState = SettlementState.Peeked,
    DateTimeOffset? LockedUntil = null,
    string? LockToken = null)
{
    /// <summary>
    /// True when the message is currently eligible for settlement at <paramref name="utcNow"/>.
    /// Peeked, expired, lost, and terminal messages are never settleable.
    /// </summary>
    public bool IsSettleableAt(DateTimeOffset utcNow)
    {
        var effective = SettlementState;
        if (ReceiveKind == MessageReceiveKind.Peeked || SettlementState == SettlementState.Peeked)
        {
            effective = SettlementState.Peeked;
        }

        effective = SettlementStateMachine.RefreshForClock(effective, LockedUntil, utcNow);
        return SettlementStateMachine.CanSettle(effective);
    }
}

/// <summary>
/// Identifies a queue or subscription entity path for browse operations.
/// </summary>
public sealed record EntityAddress(string Path);

/// <summary>
/// Bounded paging request for non-destructive peek.
/// </summary>
public sealed record PageRequest(int MaxCount, long? FromSequenceNumber = null);

/// <summary>
/// Continuation metadata for the next browse page.
/// </summary>
public sealed record BrowseContinuation(long FromSequenceNumber);

/// <summary>
/// Result of a bounded, source-tagged browse peek.
/// </summary>
public sealed record MessageBrowseResult(
    IReadOnlyList<ObservedMessage> Messages,
    BrowseContinuation? Continuation,
    SourceAvailability Availability);
