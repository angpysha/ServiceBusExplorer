#nullable enable
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Retrieves deferred messages by explicit active source and sequence number.
/// Authorization and supported-source checks run before broker I/O.
/// </summary>
public sealed class DeferredMessageService : IDeferredMessageService
{
    private readonly IServiceBusReceiveAdapter _receiveAdapter;
    private readonly ILogger<DeferredMessageService> _log;

    public DeferredMessageService(
        IServiceBusReceiveAdapter receiveAdapter,
        ILogger<DeferredMessageService> log)
    {
        _receiveAdapter = receiveAdapter;
        _log = log;
    }

    public async Task<DeferredRetrievalOutcome> RetrieveAsync(
        DeferredRetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Address);
        ArgumentNullException.ThrowIfNull(request.Capabilities);
        cancellationToken.ThrowIfCancellationRequested();

        var prerequisite = ObservedMessage.CheckRetrievalPrerequisites(
            request.Capabilities,
            request.Source);

        if (prerequisite == DeferredRetrievalEligibility.Unauthorized)
        {
            return new DeferredRetrievalOutcome(
                DeferredRetrievalResultKind.RejectedUnauthorized,
                null,
                "Deferred retrieval is not authorized for the current connection authorization capabilities.");
        }

        if (prerequisite == DeferredRetrievalEligibility.UnsupportedSource)
        {
            return new DeferredRetrievalOutcome(
                DeferredRetrievalResultKind.RejectedUnsupportedSource,
                null,
                $"Deferred retrieval requires MessageSource.Active; {request.Source} is not supported.");
        }

        var subQueue = MessageSourceMapper.Map(request.Source);

        try
        {
            var message = await _receiveAdapter.ReceiveDeferredMessageAsync(
                request.Address.Path,
                subQueue,
                request.SequenceNumber,
                cancellationToken);

            var observed = MapMessage(message, request.Source);

            _log.LogInformation(
                "Retrieved deferred message {MessageId} (sequence {SequenceNumber}) from {EntityPath}",
                observed.MessageId,
                observed.SequenceNumber,
                request.Address.Path);

            return new DeferredRetrievalOutcome(
                DeferredRetrievalResultKind.Succeeded,
                observed,
                $"Retrieved deferred message {observed.MessageId} from {request.Address.Path}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessageNotFound)
        {
            _log.LogWarning(
                "Deferred message sequence {SequenceNumber} was not found on {EntityPath}",
                request.SequenceNumber,
                request.Address.Path);

            return new DeferredRetrievalOutcome(
                DeferredRetrievalResultKind.NotFound,
                null,
                $"No deferred message with sequence number {request.SequenceNumber} was found on {request.Address.Path}.");
        }
        catch (Exception ex)
        {
            _log.LogError(
                ex,
                "Deferred retrieval failed for sequence {SequenceNumber} on {EntityPath}",
                request.SequenceNumber,
                request.Address.Path);

            return new DeferredRetrievalOutcome(
                DeferredRetrievalResultKind.Failed,
                null,
                ServiceBusFailureTranslator.ToSafeMessage(ex));
        }
    }

    private static ObservedMessage MapMessage(ServiceBusReceivedMessage message, MessageSource source)
    {
        var lockedUntil = message.LockedUntil == DateTimeOffset.MinValue
            ? (DateTimeOffset?)null
            : message.LockedUntil;

        return new ObservedMessage(
            message.MessageId,
            source,
            MessageReceiveKind.Locked,
            message.SequenceNumber,
            message.DeliveryCount,
            message.EnqueuedTime,
            message.ScheduledEnqueueTime,
            MessageBodyMapper.Map(message.Body.ToMemory(), message.ContentType),
            message.ApplicationProperties.ToDictionary(static kv => kv.Key, static kv => kv.Value),
            message.SessionId,
            message.CorrelationId,
            message.DeadLetterReason,
            SettlementState: SettlementState.Locked,
            LockedUntil: lockedUntil,
            LockToken: message.LockToken);
    }
}
