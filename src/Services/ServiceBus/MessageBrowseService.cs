using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

public sealed class MessageBrowseService : IMessageBrowseService
{
    public const int MinPageCount = 1;
    public const int MaxPageCount = 100;

    private readonly IServiceBusPeekAdapter _peekAdapter;
    private readonly ILogger<MessageBrowseService> _log;

    public MessageBrowseService(IServiceBusPeekAdapter peekAdapter, ILogger<MessageBrowseService> log)
    {
        _peekAdapter = peekAdapter;
        _log = log;
    }

    public async Task<MessageBrowseResult> PeekAsync(
        EntityAddress address,
        MessageSource source,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var maxCount = Math.Clamp(page.MaxCount, MinPageCount, MaxPageCount);
        var subQueue = MessageSourceMapper.Map(source);

        try
        {
            var messages = await _peekAdapter.PeekMessagesAsync(
                address.Path,
                subQueue,
                maxCount,
                page.FromSequenceNumber,
                cancellationToken);

            if (messages.Count == 0)
            {
                return new MessageBrowseResult([], null, SourceAvailability.Empty);
            }

            var observed = messages
                .Select(message => MapMessage(message, source))
                .ToList();

            BrowseContinuation? continuation = observed.Count == maxCount
                ? new BrowseContinuation(observed[^1].SequenceNumber + 1)
                : null;

            _log.LogInformation(
                "Peeked {Count} messages from {EntityPath} source {Source}",
                observed.Count,
                address.Path,
                source);

            return new MessageBrowseResult(observed, continuation, SourceAvailability.Available);
        }
        catch (ServiceBusException ex) when (IsUnavailable(ex))
        {
            _log.LogWarning(
                ex,
                "Message source {Source} unavailable for {EntityPath}",
                source,
                address.Path);

            return new MessageBrowseResult([], null, SourceAvailability.Unavailable);
        }
    }

    private static bool IsUnavailable(ServiceBusException ex) =>
        ex.Reason is ServiceBusFailureReason.MessagingEntityNotFound
            or ServiceBusFailureReason.MessagingEntityAlreadyExists
            or ServiceBusFailureReason.ServiceTimeout
            or ServiceBusFailureReason.ServiceCommunicationProblem
            or ServiceBusFailureReason.GeneralError
            or ServiceBusFailureReason.QuotaExceeded;

    private static ObservedMessage MapMessage(ServiceBusReceivedMessage message, MessageSource source) =>
        new(
            message.MessageId,
            source,
            MessageReceiveKind.Peeked,
            message.SequenceNumber,
            message.DeliveryCount,
            message.EnqueuedTime,
            message.ScheduledEnqueueTime,
            MessageBodyMapper.Map(message.Body.ToMemory(), message.ContentType),
            message.ApplicationProperties.ToDictionary(static kv => kv.Key, static kv => kv.Value),
            message.SessionId,
            message.CorrelationId,
            message.DeadLetterReason);
}
