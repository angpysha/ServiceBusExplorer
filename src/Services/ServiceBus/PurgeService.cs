#nullable enable
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Bounded receive-and-delete purge. Confirmation is required outside this adapter.
/// Cancels between batches, reports confirmed count plus uncertain remainder, and never
/// automatically retries the whole operation after partial progress.
/// </summary>
public sealed class PurgeService : IPurgeService
{
    public const int MinBatchCount = 1;
    public const int MaxBatchCount = 100;
    public static readonly TimeSpan DefaultReceiveWait = TimeSpan.FromSeconds(1);

    private const string OperationName = "Purge";

    private readonly IServiceBusReceiveAdapter _receiveAdapter;
    private readonly ILogger<PurgeService> _log;
    private readonly TimeSpan _receiveWait;

    public PurgeService(
        IServiceBusReceiveAdapter receiveAdapter,
        ILogger<PurgeService> log,
        TimeSpan? receiveWait = null)
    {
        _receiveAdapter = receiveAdapter;
        _log = log;
        _receiveWait = receiveWait ?? DefaultReceiveWait;
    }

    public async Task<OperationOutcome> PurgeAsync(
        EntityAddress target,
        MessageSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        // Exhaustive mapping — ArgumentOutOfRangeException for unknown values.
        var subQueue = MessageSourceMapper.Map(source);
        var path = target.Path;
        long confirmed = 0;

        if (cancellationToken.IsCancellationRequested)
        {
            return OperationOutcome.Cancelled(
                OperationName,
                path,
                source,
                $"Purge of {path} ({source}) was cancelled before any messages were removed.");
        }

        try
        {
            while (true)
            {
                // Cancellable between batches.
                if (cancellationToken.IsCancellationRequested)
                {
                    return BuildCancelledAfterProgress(path, source, confirmed);
                }

                IReadOnlyList<Azure.Messaging.ServiceBus.ServiceBusReceivedMessage> batch;
                try
                {
                    batch = await _receiveAdapter.ReceiveAndDeleteAsync(
                        path,
                        subQueue,
                        MaxBatchCount,
                        _receiveWait,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return confirmed > 0
                        ? BuildCancelledAfterProgress(path, source, confirmed)
                        : OperationOutcome.Cancelled(
                            OperationName,
                            path,
                            source,
                            $"Purge of {path} ({source}) was cancelled before any messages were removed.");
                }

                if (batch.Count == 0)
                {
                    _log.LogInformation(
                        "Purge completed on {EntityPath} source {Source}; confirmed {Confirmed}",
                        path,
                        source,
                        confirmed);

                    return OperationOutcome.Succeeded(
                        OperationName,
                        path,
                        source,
                        confirmed,
                        confirmed == 0
                            ? $"No messages to purge from {path} ({source})."
                            : $"Purged {confirmed} message(s) from {path} ({source}).");
                }

                confirmed += batch.Count;

                // Check again between batches — do not start another receive after cancel.
                if (cancellationToken.IsCancellationRequested)
                {
                    return BuildCancelledAfterProgress(path, source, confirmed);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(
                "Purge failed on {EntityPath} source {Source} after {Confirmed} confirmed removal(s)",
                path,
                source,
                confirmed);

            return OperationOutcome.Failed(
                OperationName,
                path,
                source,
                confirmed > 0
                    ? $"Purge of {path} ({source}) failed after {confirmed} confirmed removal(s). Remaining messages are uncertain; do not assume the source is empty. Manual remainder purge only — no automatic whole-operation retry."
                    : $"Purge of {path} ({source}) failed before any messages were confirmed removed.",
                confirmed,
                hasUncertainRemainder: confirmed > 0);
        }
    }

    private static OperationOutcome BuildCancelledAfterProgress(
        string path,
        MessageSource source,
        long confirmed) =>
        OperationOutcome.Cancelled(
            OperationName,
            path,
            source,
            $"Purge of {path} ({source}) was cancelled after {confirmed} confirmed removal(s). Remaining messages are uncertain; do not assume the source is empty.",
            confirmed,
            hasUncertainRemainder: true);
}
