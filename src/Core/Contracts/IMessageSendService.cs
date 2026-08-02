#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Outcome status for a message send attempt.
/// </summary>
public enum MessageSendStatus
{
    Succeeded,
    ValidationFailed,
    Failed
}

/// <summary>
/// Typed, secret-safe result of sending a <see cref="MessageDraft"/>.
/// Never carries body bytes, custom property values, credentials, or raw secret material.
/// </summary>
public sealed record MessageSendResult(
    MessageSendStatus Status,
    string SafeMessage,
    ConnectionFailureCategory? FailureCategory = null,
    string? ValidationErrorCode = null);

/// <summary>
/// Application port for composing and sending a validated message draft on the current-path backend.
/// </summary>
public interface IMessageSendService
{
    /// <summary>
    /// Validates <paramref name="draft"/> and sends it to <see cref="SendTargetContext.ActualDestinationPath"/>.
    /// On validation or send failure the draft is left unchanged for the caller.
    /// </summary>
    Task<MessageSendResult> SendAsync(
        SendTargetContext target,
        MessageDraft draft,
        int sendCount = 1,
        CancellationToken cancellationToken = default);
}
