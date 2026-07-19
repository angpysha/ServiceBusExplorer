#nullable enable
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Explicit-source peek-lock and confirmed receive-and-delete orchestration.
/// Settlement eligibility remains owned by later tasks; this service opens handles and
/// enforces that receive-and-delete only runs with outside confirmation evidence.
/// </summary>
public sealed class MessageReceiveService : IMessageReceiveService
{
    public const int MinBatchCount = 1;
    public const int MaxBatchCount = 100;

    private readonly IServiceBusReceiveAdapter _receiveAdapter;
    private readonly ILogger<MessageReceiveService> _log;

    public MessageReceiveService(
        IServiceBusReceiveAdapter receiveAdapter,
        ILogger<MessageReceiveService> log)
    {
        _receiveAdapter = receiveAdapter;
        _log = log;
    }

    public Task<IReceiveSession> OpenPeekLockAsync(
        EntityAddress address,
        MessageSource source,
        SessionRequest? sessionRequest = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        cancellationToken.ThrowIfCancellationRequested();

        if (sessionRequest is not null)
        {
            throw new InvalidOperationException(
                "Session-aware peek-lock receive is not enabled yet. Pass sessionRequest: null.");
        }

        var subQueue = MessageSourceMapper.Map(source);
        var session = _receiveAdapter.OpenPeekLock(address.Path, subQueue, source);

        _log.LogInformation(
            "Opened peek-lock session on {EntityPath} source {Source}",
            address.Path,
            source);

        return Task.FromResult(session);
    }

    public async Task<ReceiveAndDeleteResult> ReceiveAndDeleteAsync(
        ConfirmedReceiveAndDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Confirmation);

        var maxMessages = Math.Clamp(request.MaxMessages, MinBatchCount, MaxBatchCount);
        var maxWait = request.MaxWait ?? TimeSpan.FromSeconds(3);
        var address = request.Confirmation.Address;
        var source = request.Confirmation.Source;
        var subQueue = MessageSourceMapper.Map(source);

        var messages = await _receiveAdapter.ReceiveAndDeleteAsync(
            address.Path,
            subQueue,
            maxMessages,
            maxWait,
            cancellationToken);

        var mapped = messages.Select(MapMessage).ToList();

        _log.LogInformation(
            "Receive-and-delete removed {Count} message(s) from {EntityPath} source {Source}",
            mapped.Count,
            address.Path,
            source);

        return new ReceiveAndDeleteResult(
            mapped,
            ReportsDisplayLossRisk: true,
            SafeMessage: mapped.Count == 0
                ? $"No messages received-and-deleted from {address.Path} ({source})."
                : $"Permanently removed {mapped.Count} message(s) from {address.Path} ({source}). Display copies may be incomplete.");
    }

    private static ReceivedMessage MapMessage(Azure.Messaging.ServiceBus.ServiceBusReceivedMessage m) =>
        new(
            m.MessageId,
            m.Body.ToString(),
            m.ContentType ?? "application/octet-stream",
            m.SequenceNumber,
            m.DeliveryCount,
            m.EnqueuedTime,
            m.ExpiresAt,
            m.CorrelationId,
            m.SessionId,
            m.ApplicationProperties.ToDictionary(kv => kv.Key, kv => kv.Value),
            m.DeadLetterReason,
            m.LockToken);
}
