#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// A live receive session that holds message locks so they can be settled
/// (completed, abandoned, or dead-lettered) after inspection.
/// Dispose the session when done — outstanding locks expire automatically on Azure.
/// </summary>
public interface IReceiveSession : IAsyncDisposable
{
    /// <summary>Receive up to <paramref name="maxMessages"/> messages with a peek-lock.</summary>
    Task<IReadOnlyList<ReceivedMessage>> ReceiveBatchAsync(
        int maxMessages = 20, TimeSpan? maxWait = null, CancellationToken ct = default);

    /// <summary>Complete (delete) a received message.</summary>
    Task CompleteAsync(ReceivedMessage message, CancellationToken ct = default);

    /// <summary>Abandon a message — it becomes visible again for redelivery.</summary>
    Task AbandonAsync(ReceivedMessage message, CancellationToken ct = default);

    /// <summary>Move a message to the dead-letter sub-queue.</summary>
    Task DeadLetterAsync(ReceivedMessage message, string? reason = null, CancellationToken ct = default);

    /// <summary>Defer a message — it must be received explicitly by sequence number.</summary>
    Task DeferAsync(ReceivedMessage message, CancellationToken ct = default);
}
