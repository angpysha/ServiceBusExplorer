#nullable enable
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Explicit-source peek-lock, confirmed receive-and-delete, and settlement orchestration.
/// Settlement methods are single-attempt per currently eligible lock and return typed outcomes.
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

    public SettlementItemOutcome RejectPeekedSettlement(
        ObservedMessage message,
        SettlementAction action)
    {
        ArgumentNullException.ThrowIfNull(message);
        return SettlementTracker.RejectPeeked(message, action);
    }

    public Task<SettlementItemOutcome> CompleteAsync(
        IReceiveSession session,
        ReceivedMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        return session.CompleteAsync(message, cancellationToken);
    }

    public Task<SettlementItemOutcome> AbandonAsync(
        IReceiveSession session,
        ReceivedMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        return session.AbandonAsync(message, cancellationToken);
    }

    public Task<SettlementItemOutcome> DeferAsync(
        IReceiveSession session,
        ReceivedMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        return session.DeferAsync(message, cancellationToken);
    }

    public Task<SettlementItemOutcome> DeadLetterAsync(
        IReceiveSession session,
        ReceivedMessage message,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        return session.DeadLetterAsync(message, reason, cancellationToken);
    }

    public async Task<SettlementBatchOutcome> SettleBatchAsync(
        IReceiveSession session,
        IReadOnlyList<ReceivedMessage> messages,
        SettlementAction action,
        string? deadLetterReason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(messages);

        var outcomes = new List<SettlementItemOutcome>(messages.Count);
        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip messages that already reached a confirmed success terminal in this session —
            // never automatically repeat a successful settlement.
            var state = session.GetSettlementState(message);
            if (state is SettlementState.Completed
                or SettlementState.Abandoned
                or SettlementState.Deferred
                or SettlementState.DeadLettered)
            {
                outcomes.Add(new SettlementItemOutcome(
                    message.MessageId,
                    message.SequenceNumber,
                    action,
                    SettlementResultKind.RejectedIneligible,
                    state,
                    state,
                    SettlementStateMachine.DescribeIneligibility(state),
                    message.LockToken));
                continue;
            }

            var outcome = action switch
            {
                SettlementAction.Complete => await session.CompleteAsync(message, cancellationToken),
                SettlementAction.Abandon => await session.AbandonAsync(message, cancellationToken),
                SettlementAction.Defer => await session.DeferAsync(message, cancellationToken),
                SettlementAction.DeadLetter => await session.DeadLetterAsync(
                    message, deadLetterReason, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
            };
            outcomes.Add(outcome);
        }

        return new SettlementBatchOutcome(outcomes);
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
