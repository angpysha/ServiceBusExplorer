#nullable enable
namespace ServiceBusExplorer;

public enum MessageSubQueue { None, DeadLetter, TransferDeadLetter }

public record OutboundMessage(
    string Body,
    string ContentType = "application/json",
    string? MessageId = null,
    string? CorrelationId = null,
    string? SessionId = null,
    string? To = null,
    IReadOnlyDictionary<string, object>? Properties = null,
    DateTimeOffset? ScheduledEnqueueTime = null);

public record ReceivedMessage(
    string MessageId,
    string Body,
    string ContentType,
    long SequenceNumber,
    int DeliveryCount,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? ExpiresAt,
    string? CorrelationId,
    string? SessionId,
    IReadOnlyDictionary<string, object> Properties,
    string? DeadLetterReason = null,
    string? LockToken = null);
