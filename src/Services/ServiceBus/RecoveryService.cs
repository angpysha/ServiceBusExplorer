#nullable enable
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Selected-message recovery that sends replacements before settling originals.
/// </summary>
public sealed class RecoveryService : IRecoveryService
{
    private const string OperationName = "Recovery";
    private static readonly TimeSpan DefaultReceiveWait = TimeSpan.FromSeconds(3);

    private readonly IQueueService _queueService;
    private readonly IMessageReceiveService _receiveService;
    private readonly ILogger<RecoveryService> _log;

    public RecoveryService(
        IQueueService queueService,
        IMessageReceiveService receiveService,
        ILogger<RecoveryService> log)
    {
        _queueService = queueService;
        _receiveService = receiveService;
        _log = log;
    }

    public async Task<RecoveryOperation> RecoverAsync(
        RecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceAddress);
        ArgumentNullException.ThrowIfNull(request.DestinationAddress);
        ArgumentNullException.ThrowIfNull(request.SelectedMessages);

        if (request.SelectedMessages.Count == 0)
        {
            var empty = Array.Empty<RecoveryItemOutcome>();
            return new RecoveryOperation(
                OperationOutcome.Succeeded(
                    OperationName,
                    request.DestinationAddress.Path,
                    request.Source,
                    confirmedCount: 0,
                    safeMessage: $"No messages selected for {OperationName}."),
                empty,
                new RecoveryRetryRequest(Array.Empty<RecoveryItemIdentity>()));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return BuildCancelled(request, request.SelectedMessages.ToArray());
        }

        await using var session = await _receiveService.OpenPeekLockAsync(
            request.SourceAddress,
            request.Source,
            sessionRequest: null,
            cancellationToken);

        var maxMessages = Math.Max(1, request.SelectedMessages.Count);
        IReadOnlyList<ReceivedMessage> received;
        try
        {
            received = await session.ReceiveBatchAsync(
                maxMessages,
                maxWait: DefaultReceiveWait,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return BuildCancelled(request, request.SelectedMessages.ToArray());
        }

        var originalsBySequence = new Dictionary<long, ReceivedMessage>();
        foreach (var message in received)
        {
            if (!originalsBySequence.ContainsKey(message.SequenceNumber))
                originalsBySequence[message.SequenceNumber] = message;
        }

        var outcomes = new List<RecoveryItemOutcome>(request.SelectedMessages.Count);

        for (var i = 0; i < request.SelectedMessages.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // No further work should be claimed after the operator cancelled.
                outcomes.AddRange(
                    BuildCancelledRemaining(request.SelectedMessages, i));
                break;
            }

            var selected = request.SelectedMessages[i];

            if (!originalsBySequence.TryGetValue(selected.SequenceNumber, out var original))
            {
                outcomes.Add(
                    new RecoveryItemOutcome(
                        selected.MessageId,
                        selected.SequenceNumber,
                        RecoveryItemResultKind.Uncertain,
                        "Original message was not available in the opened receive session."));
                continue;
            }

            var replacement = MapReplacementOutbound(
                selected,
                request.DiagnosticPropertyTreatment);

            bool replacementSent;
            try
            {
                await _queueService.SendAsync(
                    request.DestinationAddress.Path,
                    replacement,
                    cancellationToken);

                replacementSent = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                outcomes.AddRange(
                    BuildCancelledRemainingFromCurrent(request.SelectedMessages, i));
                break;
            }
            catch (Exception ex)
            {
                outcomes.Add(
                    new RecoveryItemOutcome(
                        selected.MessageId,
                        selected.SequenceNumber,
                        RecoveryItemResultKind.Failed,
                        ServiceBusFailureTranslator.ToSafeMessage(ex)));
                continue;
            }

            if (!replacementSent)
            {
                // Defensive guard — should not happen due to try/assign above.
                outcomes.Add(
                    new RecoveryItemOutcome(
                        selected.MessageId,
                        selected.SequenceNumber,
                        RecoveryItemResultKind.Uncertain,
                        "Replacement send did not complete successfully."));
                continue;
            }

            // Ordering guarantee: original settlement is attempted only after replacement send succeeded.
            try
            {
                var settled = await _receiveService.CompleteAsync(
                    session,
                    original,
                    cancellationToken);

                outcomes.Add(
                    settled.Result == SettlementResultKind.Succeeded
                        ? new RecoveryItemOutcome(
                            selected.MessageId,
                            selected.SequenceNumber,
                            RecoveryItemResultKind.Succeeded,
                            "Replacement send succeeded and original was settled.")
                        : new RecoveryItemOutcome(
                            selected.MessageId,
                            selected.SequenceNumber,
                            RecoveryItemResultKind.Uncertain,
                            settled.SafeMessage));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                outcomes.AddRange(
                    BuildUncertainRemainingFromCurrent(request.SelectedMessages, i));
                break;
            }
            catch (Exception ex)
            {
                outcomes.Add(
                    new RecoveryItemOutcome(
                        selected.MessageId,
                        selected.SequenceNumber,
                        RecoveryItemResultKind.Uncertain,
                        ServiceBusFailureTranslator.ToSafeMessage(ex)));
            }
        }

        var retryCandidates = outcomes
            .Where(static o => o.Result != RecoveryItemResultKind.Succeeded)
            .Select(static o => new RecoveryItemIdentity(o.MessageId, o.SequenceNumber))
            .ToList();

        var retryRequest = new RecoveryRetryRequest(retryCandidates);

        var confirmedCount = outcomes.Count(o => o.Result == RecoveryItemResultKind.Succeeded);

        var allSucceeded = confirmedCount == outcomes.Count;
        var anyFailedOrUncertain = outcomes.Any(o => o.Result is RecoveryItemResultKind.Failed or RecoveryItemResultKind.Uncertain);
        var allCancelled = outcomes.Count == outcomes.Count(o => o.Result == RecoveryItemResultKind.Cancelled);

        OperationOutcome overall = allSucceeded
            ? OperationOutcome.Succeeded(
                OperationName,
                request.DestinationAddress.Path,
                request.Source,
                confirmedCount,
                $"Recovered {confirmedCount} message(s) to {request.DestinationAddress.Path} ({request.Source}).")
            : anyFailedOrUncertain
                ? OperationOutcome.Failed(
                    OperationName,
                    request.DestinationAddress.Path,
                    request.Source,
                    $"Recovery completed with failed/uncertain items for {request.DestinationAddress.Path} ({request.Source}).",
                    confirmedCount,
                    hasUncertainRemainder: true)
                : OperationOutcome.Cancelled(
                    OperationName,
                    request.DestinationAddress.Path,
                    request.Source,
                    $"Recovery was cancelled for remaining items in {request.DestinationAddress.Path} ({request.Source}).",
                    confirmedCount,
                    hasUncertainRemainder: confirmedCount > 0 || !allCancelled);

        _log.LogInformation(
            "Recovery finished: {Total} selected, {Confirmed} confirmed succeeded, {Retry} retry candidates.",
            request.SelectedMessages.Count,
            confirmedCount,
            retryCandidates.Count);

        return new RecoveryOperation(overall, outcomes, retryRequest);
    }

    private static IReadOnlyList<RecoveryItemOutcome> BuildCancelledRemaining(
        IReadOnlyList<ObservedMessage> selected,
        int fromIndex)
    {
        var remaining = new List<RecoveryItemOutcome>(selected.Count - fromIndex);
        for (var i = fromIndex; i < selected.Count; i++)
        {
            remaining.Add(
                new RecoveryItemOutcome(
                    selected[i].MessageId,
                    selected[i].SequenceNumber,
                    RecoveryItemResultKind.Cancelled,
                    "Recovery was cancelled by the operator."));
        }

        return remaining;
    }

    private static IReadOnlyList<RecoveryItemOutcome> BuildCancelledRemainingFromCurrent(
        IReadOnlyList<ObservedMessage> selected,
        int currentIndex)
    {
        // Cancellation during replacement send means no confirmed success can be asserted.
        return BuildCancelledRemaining(selected, currentIndex);
    }

    private static IReadOnlyList<RecoveryItemOutcome> BuildUncertainRemainingFromCurrent(
        IReadOnlyList<ObservedMessage> selected,
        int currentIndex)
    {
        var remaining = new List<RecoveryItemOutcome>(selected.Count - currentIndex);
        for (var i = currentIndex; i < selected.Count; i++)
        {
            remaining.Add(
                new RecoveryItemOutcome(
                    selected[i].MessageId,
                    selected[i].SequenceNumber,
                    RecoveryItemResultKind.Uncertain,
                    "Recovery did not confirm original settlement after replacement send."));
        }

        return remaining;
    }

    private static RecoveryOperation BuildCancelled(
        RecoveryRequest request,
        IReadOnlyList<ObservedMessage> selected)
    {
        var cancelled = selected
            .Select(static m => new RecoveryItemOutcome(
                m.MessageId,
                m.SequenceNumber,
                RecoveryItemResultKind.Cancelled,
                "Recovery was cancelled by the operator."))
            .ToList();

        var retryCandidates = cancelled
            .Select(static o => new RecoveryItemIdentity(o.MessageId, o.SequenceNumber))
            .ToList();

        return new RecoveryOperation(
            OperationOutcome.Cancelled(
                OperationName,
                request.DestinationAddress.Path,
                request.Source,
                $"Recovery was cancelled before any replacements were confirmed for {request.DestinationAddress.Path} ({request.Source}).",
                confirmedCount: 0,
                hasUncertainRemainder: false),
            cancelled,
            new RecoveryRetryRequest(retryCandidates));
    }

    private static OutboundMessage MapReplacementOutbound(
        ObservedMessage original,
        DiagnosticPropertyTreatment diagnosticPropertyTreatment)
    {
        var bodyText = original.Body.DisplayText ?? string.Empty;

        var properties = diagnosticPropertyTreatment == DiagnosticPropertyTreatment.RetainAsCustom
            ? original.Properties
            : FilterDeadLetterDiagnostics(original.Properties);

        return new OutboundMessage(
            Body: bodyText,
            ContentType: original.Body.ContentType ?? "application/octet-stream",
            MessageId: original.MessageId,
            CorrelationId: original.CorrelationId,
            SessionId: original.SessionId,
            Properties: properties?.Count == 0 ? null : properties);
    }

    private static IReadOnlyDictionary<string, object>? FilterDeadLetterDiagnostics(
        IReadOnlyDictionary<string, object> properties)
    {
        if (properties.Count == 0)
            return null;

        var filtered = new Dictionary<string, object>(properties.Count, StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            if (IsDeadLetterDiagnosticKey(key))
                continue;

            filtered[key] = value;
        }

        return filtered.Count == 0 ? null : filtered;
    }

    private static bool IsDeadLetterDiagnosticKey(string key) =>
        key.Equals("DeadLetterReason", StringComparison.OrdinalIgnoreCase)
            || key.Equals("DeadLetterErrorDescription", StringComparison.OrdinalIgnoreCase)
            || key.Equals("NServiceBus.Transport.Recovery", StringComparison.OrdinalIgnoreCase);
}

