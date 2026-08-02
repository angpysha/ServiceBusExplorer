#nullable enable
using System.Text;

namespace ServiceBusExplorer;

/// <summary>
/// Supported typed values for Service Bus application properties.
/// </summary>
public enum MessagePropertyType
{
    String,
    Boolean,
    Byte,
    SByte,
    Int16,
    Int32,
    Int64,
    Single,
    Double,
    Decimal,
    Guid,
    DateTime,
    DateTimeOffset
}

/// <summary>
/// A single typed custom application property on a message draft.
/// </summary>
public sealed record TypedMessageProperty(string Name, MessagePropertyType Type, object? Value);

/// <summary>
/// Result of validating a <see cref="MessageDraft"/>.
/// </summary>
public sealed record MessageDraftValidationResult(bool IsValid, string? ErrorCode, string? Message)
{
    public static MessageDraftValidationResult Success() => new(true, null, null);

    public static MessageDraftValidationResult Fail(string errorCode, string message) =>
        new(false, errorCode, message);
}

/// <summary>
/// In-memory compose-and-send draft. Survives validation and send failures until the caller clears it.
/// </summary>
public sealed class MessageDraft
{
    public const string ErrorEmptyBody = "EmptyBody";
    public const string ErrorReservedPropertyName = "ReservedPropertyName";
    public const string ErrorDuplicatePropertyName = "DuplicatePropertyName";
    public const string ErrorInvalidPropertyValue = "InvalidPropertyValue";
    public const string ErrorInvalidScheduleDelay = "InvalidScheduleDelay";

    private static readonly HashSet<string> ReservedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "MessageId",
        "ContentType",
        "CorrelationId",
        "SessionId",
        "Subject",
        "Label",
        "To",
        "ReplyTo",
        "ReplyToSessionId",
        "PartitionKey",
        "ViaPartitionKey",
        "ScheduledEnqueueTime",
        "ScheduledEnqueueTimeUtc",
        "TimeToLive",
        "TTL",
        "EnqueuedTime",
        "EnqueuedTimeUtc",
        "SequenceNumber",
        "LockToken",
        "DeliveryCount",
        "DeadLetterSource",
        "DeadLetterReason",
        "DeadLetterErrorDescription"
    };

    public string DestinationPath { get; set; } = "";

    /// <summary>Raw body bytes. Never written to routine diagnostics.</summary>
    public byte[] BodyBytes { get; set; } = [];

    public MessageBodyKind BodyRepresentation { get; set; } = MessageBodyKind.Text;

    public string? ContentType { get; set; }

    public string? MessageId { get; set; }

    public string? CorrelationId { get; set; }

    public string? SessionId { get; set; }

    public string? ReplyTo { get; set; }

    public string? ReplyToSessionId { get; set; }

    public string? To { get; set; }

    public string? Subject { get; set; }

    public string? PartitionKey { get; set; }

    /// <summary>
    /// Optional message TTL using full <see cref="DurationValue"/> precision with no product day cap.
    /// </summary>
    public DurationValue? TimeToLive { get; set; }

    /// <summary>
    /// Optional relative schedule delay. Composer may apply a separate Azure constraint.
    /// </summary>
    public DurationValue? ScheduleDelay { get; set; }

    public DateTimeOffset? AbsoluteScheduledEnqueueTime { get; set; }

    public IList<TypedMessageProperty> CustomProperties { get; } = new List<TypedMessageProperty>();

    public string GetBodyText() => Encoding.UTF8.GetString(BodyBytes);

    public void SetBodyText(string text, MessageBodyKind representation = MessageBodyKind.Text)
    {
        BodyBytes = Encoding.UTF8.GetBytes(text ?? "");
        BodyRepresentation = string.IsNullOrEmpty(text)
            ? MessageBodyKind.Empty
            : representation;
    }

    public MessageDraftValidationResult Validate()
    {
        if (BodyBytes.Length == 0 || string.IsNullOrWhiteSpace(GetBodyText()))
        {
            return MessageDraftValidationResult.Fail(
                ErrorEmptyBody,
                "Body is required.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in CustomProperties)
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                return MessageDraftValidationResult.Fail(
                    ErrorReservedPropertyName,
                    "Custom property name is required.");
            }

            if (ReservedPropertyNames.Contains(property.Name))
            {
                return MessageDraftValidationResult.Fail(
                    ErrorReservedPropertyName,
                    $"Custom property '{property.Name}' conflicts with a reserved broker property name.");
            }

            if (!seen.Add(property.Name))
            {
                return MessageDraftValidationResult.Fail(
                    ErrorDuplicatePropertyName,
                    $"Custom property '{property.Name}' duplicates another property name (case-insensitive).");
            }

            if (!IsValueCompatible(property))
            {
                return MessageDraftValidationResult.Fail(
                    ErrorInvalidPropertyValue,
                    $"Custom property '{property.Name}' value is incompatible with type {property.Type}.");
            }
        }

        if (ScheduleDelay is { } delay && delay.TotalMilliseconds < 0)
        {
            return MessageDraftValidationResult.Fail(
                ErrorInvalidScheduleDelay,
                "Schedule delay must be a non-negative whole-millisecond duration.");
        }

        return MessageDraftValidationResult.Success();
    }

    public static bool IsReservedPropertyName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && ReservedPropertyNames.Contains(name);

    private static bool IsValueCompatible(TypedMessageProperty property)
    {
        if (property.Value is null)
            return true;

        return property.Type switch
        {
            MessagePropertyType.String => property.Value is string,
            MessagePropertyType.Boolean => property.Value is bool,
            MessagePropertyType.Byte => property.Value is byte,
            MessagePropertyType.SByte => property.Value is sbyte,
            MessagePropertyType.Int16 => property.Value is short,
            MessagePropertyType.Int32 => property.Value is int,
            MessagePropertyType.Int64 => property.Value is long,
            MessagePropertyType.Single => property.Value is float,
            MessagePropertyType.Double => property.Value is double,
            MessagePropertyType.Decimal => property.Value is decimal,
            MessagePropertyType.Guid => property.Value is Guid,
            MessagePropertyType.DateTime => property.Value is DateTime,
            MessagePropertyType.DateTimeOffset => property.Value is DateTimeOffset,
            _ => false
        };
    }
}
