#nullable enable
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Validates a <see cref="MessageDraft"/> and sends via the current-path <see cref="IQueueService.SendAsync"/>.
/// </summary>
public sealed class MessageSendService : IMessageSendService
{
    public const int MinSendCount = 1;
    public const int MaxSendCount = 1000;

    private readonly IQueueService _queueService;
    private readonly ILogger<MessageSendService> _log;

    public MessageSendService(IQueueService queueService, ILogger<MessageSendService> log)
    {
        _queueService = queueService;
        _log = log;
    }

    public async Task<MessageSendResult> SendAsync(
        SendTargetContext target,
        MessageDraft draft,
        int sendCount = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(draft);

        var validation = draft.Validate();
        if (!validation.IsValid)
        {
            return new MessageSendResult(
                MessageSendStatus.ValidationFailed,
                $"{target.FailurePrefix}: {validation.Message}",
                ConnectionFailureCategory.Validation,
                validation.ErrorCode);
        }

        if (sendCount is < MinSendCount or > MaxSendCount)
        {
            return new MessageSendResult(
                MessageSendStatus.ValidationFailed,
                $"{target.FailurePrefix}: Message count must be between {MinSendCount} and {MaxSendCount}.",
                ConnectionFailureCategory.Validation,
                "InvalidSendCount");
        }

        if (draft.ScheduleDelay is { } scheduleDelay)
        {
            if (DurationConstraint.ScheduledEnqueueDelay.Validate(scheduleDelay) is { } scheduleError)
            {
                return new MessageSendResult(
                    MessageSendStatus.ValidationFailed,
                    $"{target.FailurePrefix}: {scheduleError}",
                    ConnectionFailureCategory.Validation,
                    MessageDraft.ErrorInvalidScheduleDelay);
            }
        }

        try
        {
            var outbound = MapToOutbound(draft);
            for (var i = 0; i < sendCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _queueService.SendAsync(target.ActualDestinationPath, outbound, cancellationToken);
                if (i < sendCount - 1)
                    await Task.Delay(50, cancellationToken);
            }

            _log.LogInformation(
                "Sent {Count} message(s) to {EntityPath} ({Kind})",
                sendCount,
                target.ActualDestinationPath,
                target.RequestedKind);

            return new MessageSendResult(
                MessageSendStatus.Succeeded,
                target.SuccessDescription);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var category = ServiceBusFailureTranslator.Classify(ex);
            var safe = ServiceBusFailureTranslator.ToSafeMessage(ex);

            _log.LogWarning(
                "Send to {EntityPath} failed with category {Category}",
                target.ActualDestinationPath,
                category);

            return new MessageSendResult(
                MessageSendStatus.Failed,
                $"{target.FailurePrefix}. {safe}",
                category);
        }
    }

    private static OutboundMessage MapToOutbound(MessageDraft draft)
    {
        IReadOnlyDictionary<string, object>? properties = null;
        if (draft.CustomProperties.Count > 0)
        {
            properties = draft.CustomProperties.ToDictionary(
                static p => p.Name,
                static p => p.Value!,
                StringComparer.Ordinal);
        }

        DateTimeOffset? scheduled = draft.AbsoluteScheduledEnqueueTime;
        if (scheduled is null && draft.ScheduleDelay is { } delay)
            scheduled = DateTimeOffset.Now.Add(delay.ToTimeSpan());

        return new OutboundMessage(
            Body: draft.GetBodyText(),
            ContentType: string.IsNullOrWhiteSpace(draft.ContentType)
                ? "application/json"
                : draft.ContentType,
            MessageId: NullIfWhiteSpace(draft.MessageId),
            CorrelationId: NullIfWhiteSpace(draft.CorrelationId),
            SessionId: NullIfWhiteSpace(draft.SessionId),
            To: NullIfWhiteSpace(draft.To),
            Subject: NullIfWhiteSpace(draft.Subject),
            ReplyTo: NullIfWhiteSpace(draft.ReplyTo),
            ReplyToSessionId: NullIfWhiteSpace(draft.ReplyToSessionId),
            PartitionKey: NullIfWhiteSpace(draft.PartitionKey),
            TimeToLive: draft.TimeToLive?.ToTimeSpan(),
            Properties: properties,
            ScheduledEnqueueTime: scheduled);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
