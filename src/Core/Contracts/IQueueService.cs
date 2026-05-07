#nullable enable
namespace ServiceBusExplorer;

public interface IQueueService
{
    Task<IReadOnlyList<QueueInfo>> ListAsync(CancellationToken ct = default);
    Task<QueueInfo> GetAsync(string name, CancellationToken ct = default);
    Task<QueueInfo> CreateAsync(CreateQueueOptions opts, CancellationToken ct = default);
    Task<QueueInfo> UpdateAsync(QueueInfo updated, CancellationToken ct = default);
    Task DeleteAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<ReceivedMessage>> PeekAsync(string name, int maxCount,
        MessageSubQueue sub = MessageSubQueue.None, CancellationToken ct = default);
    Task SendAsync(string name, OutboundMessage message, CancellationToken ct = default);
    Task PurgeAsync(string name, MessageSubQueue sub = MessageSubQueue.None,
        CancellationToken ct = default);

    /// <summary>
    /// Open a receive session for the given entity+sub-queue in PeekLock mode.
    /// The caller must dispose the session when finished.
    /// </summary>
    Task<IReceiveSession> OpenReceiveSessionAsync(string name,
        MessageSubQueue sub = MessageSubQueue.None, CancellationToken ct = default);
}
