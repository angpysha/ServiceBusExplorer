using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using CoreEntityStatus = ServiceBusExplorer.EntityStatus;
using SBEntityStatus = Azure.Messaging.ServiceBus.Administration.EntityStatus;

namespace ServiceBusExplorer.Services;

public class QueueService : IQueueService
{
    private readonly ServiceBusAdministrationClient _admin;
    private readonly ServiceBusClient _client;
    private readonly ILogger<QueueService> _log;

    public QueueService(ServiceBusAdministrationClient admin, ServiceBusClient client,
        ILogger<QueueService> log)
    {
        _admin = admin;
        _client = client;
        _log = log;
    }

    public async Task<IReadOnlyList<QueueInfo>> ListAsync(CancellationToken ct = default)
    {
        var propsList = new List<(QueueProperties props, QueueRuntimeProperties runtime)>();
        var runtimeMap = new Dictionary<string, QueueRuntimeProperties>();
        await foreach (var r in _admin.GetQueuesRuntimePropertiesAsync(ct))
            runtimeMap[r.Name] = r;
        await foreach (var p in _admin.GetQueuesAsync(ct))
        {
            runtimeMap.TryGetValue(p.Name, out var runtime);
            propsList.Add((p, runtime!));
        }
        return propsList
            .Where(t => t.runtime != null)
            .Select(t => MapFull(t.props, t.runtime))
            .ToList();
    }

    public async Task<QueueInfo> GetAsync(string name, CancellationToken ct = default)
    {
        var runtime = await _admin.GetQueueRuntimePropertiesAsync(name, ct);
        var props = await _admin.GetQueueAsync(name, ct);
        return MapFull(props.Value, runtime.Value);
    }

    public async Task<QueueInfo> CreateAsync(CreateQueueOptions opts, CancellationToken ct = default)
    {
        var createOpts = new Azure.Messaging.ServiceBus.Administration.CreateQueueOptions(opts.Name);
        if (opts.LockDuration.HasValue) createOpts.LockDuration = opts.LockDuration.Value;
        if (opts.DefaultMessageTimeToLive.HasValue) createOpts.DefaultMessageTimeToLive = opts.DefaultMessageTimeToLive.Value;
        createOpts.RequiresDuplicateDetection = opts.RequiresDuplicateDetection;
        createOpts.RequiresSession = opts.RequiresSession;
        if (opts.MaxDeliveryCount.HasValue) createOpts.MaxDeliveryCount = opts.MaxDeliveryCount.Value;

        var created = await _admin.CreateQueueAsync(createOpts, ct);
        var runtime = await _admin.GetQueueRuntimePropertiesAsync(opts.Name, ct);
        return MapFull(created.Value, runtime.Value);
    }

    public async Task<QueueInfo> UpdateAsync(QueueInfo updated, CancellationToken ct = default)
    {
        var existing = await _admin.GetQueueAsync(updated.Name, ct);
        var props = existing.Value;
        props.LockDuration = updated.LockDuration;
        props.DefaultMessageTimeToLive = updated.DefaultMessageTimeToLive;
        props.AutoDeleteOnIdle = updated.AutoDeleteOnIdle == default ? props.AutoDeleteOnIdle : updated.AutoDeleteOnIdle;
        props.MaxDeliveryCount = updated.MaxDeliveryCount;
        props.MaxSizeInMegabytes = (int)updated.MaxSizeInMegabytes;
        props.EnableBatchedOperations = updated.EnableBatchedOperations;
        props.DeadLetteringOnMessageExpiration = updated.EnableDeadLetteringOnMessageExpiration;
        if (updated.ForwardTo != null) props.ForwardTo = updated.ForwardTo;
        if (updated.ForwardDeadLetteredMessagesTo != null) props.ForwardDeadLetteredMessagesTo = updated.ForwardDeadLetteredMessagesTo;
        if (updated.UserMetadata != null) props.UserMetadata = updated.UserMetadata;
        props.Status = MapStatus(updated.Status);

        var result = await _admin.UpdateQueueAsync(props, ct);
        var runtime = await _admin.GetQueueRuntimePropertiesAsync(updated.Name, ct);
        return MapFull(result.Value, runtime.Value);
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default) =>
        await _admin.DeleteQueueAsync(name, ct);

    public async Task<IReadOnlyList<ReceivedMessage>> PeekAsync(string name, int maxCount,
        MessageSubQueue sub = MessageSubQueue.None, CancellationToken ct = default)
    {
        var subQueue = sub switch
        {
            MessageSubQueue.DeadLetter => SubQueue.DeadLetter,
            MessageSubQueue.TransferDeadLetter => SubQueue.TransferDeadLetter,
            _ => SubQueue.None
        };
        await using var receiver = _client.CreateReceiver(name,
            new ServiceBusReceiverOptions { SubQueue = subQueue });
        var messages = await receiver.PeekMessagesAsync(maxCount, cancellationToken: ct);
        return messages.Select(MapMessage).ToList();
    }

    public async Task SendAsync(string name, OutboundMessage message, CancellationToken ct = default)
    {
        await using var sender = _client.CreateSender(name);
        var msg = new ServiceBusMessage(message.Body)
        {
            ContentType = message.ContentType
        };
        if (message.MessageId != null) msg.MessageId = message.MessageId;
        if (message.CorrelationId != null) msg.CorrelationId = message.CorrelationId;
        if (message.SessionId != null) msg.SessionId = message.SessionId;
        if (message.To != null) msg.To = message.To;
        if (message.ScheduledEnqueueTime.HasValue)
            msg.ScheduledEnqueueTime = message.ScheduledEnqueueTime.Value;
        if (message.Properties != null)
            foreach (var (k, v) in message.Properties)
                msg.ApplicationProperties[k] = v;
        await sender.SendMessageAsync(msg, ct);
    }

    public async Task PurgeAsync(string name, MessageSubQueue sub = MessageSubQueue.None,
        CancellationToken ct = default)
    {
        var subQueue = sub switch
        {
            MessageSubQueue.DeadLetter => SubQueue.DeadLetter,
            MessageSubQueue.TransferDeadLetter => SubQueue.TransferDeadLetter,
            _ => SubQueue.None
        };
        await using var receiver = _client.CreateReceiver(name,
            new ServiceBusReceiverOptions
            {
                SubQueue = subQueue,
                ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
            });
        while (!ct.IsCancellationRequested)
        {
            var batch = await receiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(1), ct);
            if (batch.Count == 0) break;
        }
    }

    public Task<IReceiveSession> OpenReceiveSessionAsync(string name,
        MessageSubQueue sub = MessageSubQueue.None, CancellationToken ct = default)
    {
        var subQueue = sub switch
        {
            MessageSubQueue.DeadLetter => SubQueue.DeadLetter,
            MessageSubQueue.TransferDeadLetter => SubQueue.TransferDeadLetter,
            _ => SubQueue.None
        };
        var receiver = _client.CreateReceiver(name,
            new ServiceBusReceiverOptions { SubQueue = subQueue });
        return Task.FromResult<IReceiveSession>(new ReceiveSession(receiver));
    }

    private static QueueInfo MapFull(Azure.Messaging.ServiceBus.Administration.QueueProperties p,
        QueueRuntimeProperties r) => new(
        Name: p.Name,
        ActiveMessageCount: r.ActiveMessageCount,
        DeadLetterCount: r.DeadLetterMessageCount,
        ScheduledMessageCount: r.ScheduledMessageCount,
        LockDuration: p.LockDuration,
        RequiresDuplicateDetection: p.RequiresDuplicateDetection,
        RequiresSession: p.RequiresSession,
        DefaultMessageTimeToLive: p.DefaultMessageTimeToLive,
        Status: MapEntityStatus(p.Status),
        AutoDeleteOnIdle: p.AutoDeleteOnIdle,
        MaxDeliveryCount: p.MaxDeliveryCount,
        MaxSizeInMegabytes: p.MaxSizeInMegabytes,
        EnableBatchedOperations: p.EnableBatchedOperations,
        ForwardTo: string.IsNullOrEmpty(p.ForwardTo) ? null : p.ForwardTo,
        ForwardDeadLetteredMessagesTo: string.IsNullOrEmpty(p.ForwardDeadLetteredMessagesTo) ? null : p.ForwardDeadLetteredMessagesTo,
        UserMetadata: string.IsNullOrEmpty(p.UserMetadata) ? null : p.UserMetadata,
        DuplicateDetectionHistoryTimeWindow: p.DuplicateDetectionHistoryTimeWindow,
        SizeInBytes: r.SizeInBytes,
        EnableDeadLetteringOnMessageExpiration: p.DeadLetteringOnMessageExpiration);

    // Peek messages don't have a valid lock token — LockToken stays null (default)
    private static ReceivedMessage MapMessage(ServiceBusReceivedMessage m) => new(
        m.MessageId, m.Body.ToString(), m.ContentType ?? "application/octet-stream",
        m.SequenceNumber, m.DeliveryCount, m.EnqueuedTime, m.ExpiresAt,
        m.CorrelationId, m.SessionId,
        m.ApplicationProperties.ToDictionary(kv => kv.Key, kv => kv.Value),
        m.DeadLetterReason, LockToken: null);

    private static CoreEntityStatus MapEntityStatus(SBEntityStatus s)
    {
        if (s == SBEntityStatus.Disabled) return CoreEntityStatus.Disabled;
        if (s == SBEntityStatus.SendDisabled) return CoreEntityStatus.SendDisabled;
        if (s == SBEntityStatus.ReceiveDisabled) return CoreEntityStatus.ReceiveDisabled;
        if (s == SBEntityStatus.Active) return CoreEntityStatus.Active;
        return CoreEntityStatus.Unknown;
    }

    private static SBEntityStatus MapStatus(CoreEntityStatus s)
    {
        if (s == CoreEntityStatus.Disabled) return SBEntityStatus.Disabled;
        if (s == CoreEntityStatus.SendDisabled) return SBEntityStatus.SendDisabled;
        if (s == CoreEntityStatus.ReceiveDisabled) return SBEntityStatus.ReceiveDisabled;
        return SBEntityStatus.Active;
    }
}
