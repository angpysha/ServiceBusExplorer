#nullable enable
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Shared Service Bus entity administration validation (no silent clamping).
/// </summary>
internal static class EntityAdminValidation
{
    public static readonly TimeSpan MinLockDuration = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxLockDuration = TimeSpan.FromMinutes(5);
    public const int MinMaxDeliveryCount = 1;
    public const int MaxMaxDeliveryCount = 2000;

    public static IReadOnlyList<string> ValidateCreateQueue(CreateQueueOptions opts)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(opts.Name))
            errors.Add("Name is required.");

        if (opts.LockDuration is { } lockDuration)
        {
            if (lockDuration < MinLockDuration || lockDuration > MaxLockDuration)
            {
                errors.Add(
                    $"LockDuration must be between {MinLockDuration.TotalSeconds:0}s and {MaxLockDuration.TotalMinutes:0}m (got {lockDuration}).");
            }
        }

        if (opts.DefaultMessageTimeToLive is { } ttl && ttl <= TimeSpan.Zero)
            errors.Add("DefaultMessageTimeToLive must be greater than zero.");

        if (opts.MaxDeliveryCount is { } mdc &&
            (mdc < MinMaxDeliveryCount || mdc > MaxMaxDeliveryCount))
        {
            errors.Add(
                $"MaxDeliveryCount must be between {MinMaxDeliveryCount} and {MaxMaxDeliveryCount} (got {mdc}).");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateCreateTopic(CreateTopicOptions opts)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(opts.Name))
            errors.Add("Name is required.");

        if (opts.DefaultMessageTimeToLive is { } ttl && ttl <= TimeSpan.Zero)
            errors.Add("DefaultMessageTimeToLive must be greater than zero.");

        return errors;
    }

    public static IReadOnlyList<string> ValidateQueueUpdate(
        QueueInfo updated,
        QueueAdminSnapshot current)
    {
        var errors = new List<string>();
        if (!string.Equals(updated.Name, current.Name, StringComparison.OrdinalIgnoreCase))
            errors.Add("Name is immutable and cannot be changed.");

        if (updated.RequiresSession != current.RequiresSession)
            errors.Add("RequiresSession is immutable after create.");

        if (updated.RequiresDuplicateDetection != current.RequiresDuplicateDetection)
            errors.Add("RequiresDuplicateDetection is immutable after create.");

        if (updated.LockDuration < MinLockDuration || updated.LockDuration > MaxLockDuration)
        {
            errors.Add(
                $"LockDuration must be between {MinLockDuration.TotalSeconds:0}s and {MaxLockDuration.TotalMinutes:0}m (got {updated.LockDuration}).");
        }

        if (updated.MaxDeliveryCount < MinMaxDeliveryCount ||
            updated.MaxDeliveryCount > MaxMaxDeliveryCount)
        {
            errors.Add(
                $"MaxDeliveryCount must be between {MinMaxDeliveryCount} and {MaxMaxDeliveryCount} (got {updated.MaxDeliveryCount}).");
        }

        if (updated.DefaultMessageTimeToLive <= TimeSpan.Zero)
            errors.Add("DefaultMessageTimeToLive must be greater than zero.");

        return errors;
    }

    public static IReadOnlyList<string> ValidateTopicUpdate(
        TopicInfo updated,
        TopicAdminSnapshot current)
    {
        var errors = new List<string>();
        if (!string.Equals(updated.Name, current.Name, StringComparison.OrdinalIgnoreCase))
            errors.Add("Name is immutable and cannot be changed.");

        if (updated.EnablePartitioning != current.EnablePartitioning)
            errors.Add("EnablePartitioning is immutable after create.");

        if (updated.RequiresDuplicateDetection != current.RequiresDuplicateDetection)
            errors.Add("RequiresDuplicateDetection is immutable after create.");

        if (updated.DefaultMessageTimeToLive != default &&
            updated.DefaultMessageTimeToLive <= TimeSpan.Zero)
        {
            errors.Add("DefaultMessageTimeToLive must be greater than zero.");
        }

        return errors;
    }
}

public class QueueService : IQueueService
{
    private readonly IServiceBusAdminAdapter _admin;
    private readonly ServiceBusClient? _client;
    private readonly ILogger<QueueService> _log;

    public QueueService(
        IServiceBusAdminAdapter admin,
        ServiceBusClient client,
        ILogger<QueueService> log)
        : this(admin, client, log, allowNullClient: false)
    {
    }

    /// <summary>
    /// Test seam: messaging ports require a client; lifecycle tests may pass null.
    /// </summary>
    public QueueService(
        IServiceBusAdminAdapter admin,
        ServiceBusClient? client,
        ILogger<QueueService> log,
        bool allowNullClient)
    {
        _admin = admin;
        _client = allowNullClient ? client : client ?? throw new ArgumentNullException(nameof(client));
        _log = log;
    }

    public async Task<IReadOnlyList<QueueInfo>> ListAsync(CancellationToken ct = default)
    {
        var snapshots = await _admin.ListQueuesAsync(ct);
        return snapshots.Select(MapQueue).ToList();
    }

    public async Task<QueueInfo> GetAsync(string name, CancellationToken ct = default)
    {
        var snapshot = await _admin.GetQueueAsync(name, ct);
        if (snapshot is null)
            throw new InvalidOperationException($"Queue '{name}' was not found.");
        return MapQueue(snapshot);
    }

    public async Task<EntityLifecycleResult<QueueInfo>> CreateAsync(
        CreateQueueOptions opts,
        CancellationToken ct = default)
    {
        var errors = EntityAdminValidation.ValidateCreateQueue(opts);
        if (errors.Count > 0)
        {
            return EntityLifecycleResult<QueueInfo>.ValidationFailed(
                "Queue create options are invalid.",
                errors.ToArray());
        }

        try
        {
            var created = await _admin.CreateQueueAsync(
                new QueueAdminCreateRequest(
                    opts.Name,
                    opts.LockDuration,
                    opts.DefaultMessageTimeToLive,
                    opts.RequiresDuplicateDetection,
                    opts.RequiresSession,
                    opts.MaxDeliveryCount),
                ct);
            var entity = MapQueue(created);
            _log.LogInformation("Created queue {QueueName}", entity.Name);
            return EntityLifecycleResult<QueueInfo>.Succeeded(
                entity,
                entity.ServiceVersion,
                $"Queue '{entity.Name}' was created.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Failed to create queue {QueueName}", opts.Name);
            return EntityLifecycleResult<QueueInfo>.Failed(
                $"Failed to create queue '{opts.Name}'.");
        }
    }

    public async Task<EntityLifecycleResult<QueueInfo>> UpdateAsync(
        QueueInfo updated,
        CancellationToken ct = default)
    {
        var current = await _admin.GetQueueAsync(updated.Name, ct);
        if (current is null)
        {
            return EntityLifecycleResult<QueueInfo>.NotFound(
                $"Queue '{updated.Name}' was not found.");
        }

        var errors = EntityAdminValidation.ValidateQueueUpdate(updated, current);
        if (errors.Count > 0)
        {
            return EntityLifecycleResult<QueueInfo>.ValidationFailed(
                "Queue update options are invalid or unsupported.",
                errors.ToArray());
        }

        if (!string.IsNullOrEmpty(updated.ServiceVersion) &&
            !string.Equals(updated.ServiceVersion, current.ServiceVersion, StringComparison.Ordinal))
        {
            var refreshed = MapQueue(current);
            return EntityLifecycleResult<QueueInfo>.Conflict(
                refreshed,
                refreshed.ServiceVersion,
                $"Queue '{updated.Name}' was modified by another client; refresh and retry.");
        }

        try
        {
            var snapshot = current with
            {
                LockDuration = updated.LockDuration,
                DefaultMessageTimeToLive = updated.DefaultMessageTimeToLive,
                AutoDeleteOnIdle = updated.AutoDeleteOnIdle,
                MaxDeliveryCount = updated.MaxDeliveryCount,
                MaxSizeInMegabytes = updated.MaxSizeInMegabytes,
                EnableBatchedOperations = updated.EnableBatchedOperations,
                EnableDeadLetteringOnMessageExpiration = updated.EnableDeadLetteringOnMessageExpiration,
                ForwardTo = updated.ForwardTo,
                ForwardDeadLetteredMessagesTo = updated.ForwardDeadLetteredMessagesTo,
                UserMetadata = updated.UserMetadata,
                Status = updated.Status
            };
            var saved = await _admin.UpdateQueueAsync(snapshot, ct);
            var entity = MapQueue(saved);
            return EntityLifecycleResult<QueueInfo>.Succeeded(
                entity,
                entity.ServiceVersion,
                $"Queue '{entity.Name}' was updated.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Failed to update queue {QueueName}", updated.Name);
            var after = await _admin.GetQueueAsync(updated.Name, ct);
            if (after is not null)
            {
                var refreshed = MapQueue(after);
                return EntityLifecycleResult<QueueInfo>.Failed(
                    $"Failed to update queue '{updated.Name}'.");
            }

            return EntityLifecycleResult<QueueInfo>.Failed(
                $"Failed to update queue '{updated.Name}'.");
        }
    }

    public async Task<EntityLifecycleResult<QueueInfo?>> DeleteAsync(
        string name,
        CancellationToken ct = default)
    {
        var existing = await _admin.GetQueueAsync(name, ct);
        if (existing is null)
        {
            return EntityLifecycleResult<QueueInfo?>.NotFound($"Queue '{name}' was not found.");
        }

        try
        {
            await _admin.DeleteQueueAsync(name, ct);
            var stillThere = await _admin.GetQueueAsync(name, ct);
            if (stillThere is not null)
            {
                return EntityLifecycleResult<QueueInfo?>.Failed(
                    $"Queue '{name}' delete did not remove the entity.");
            }

            return EntityLifecycleResult<QueueInfo?>.Succeeded(
                null,
                null,
                $"Queue '{name}' was deleted.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Failed to delete queue {QueueName}", name);
            return EntityLifecycleResult<QueueInfo?>.Failed($"Failed to delete queue '{name}'.");
        }
    }

    public async Task<IReadOnlyList<ReceivedMessage>> PeekAsync(
        string name,
        int maxCount,
        MessageSource source,
        CancellationToken ct = default)
    {
        var client = RequireClient();
        await using var receiver = client.CreateReceiver(name,
            new ServiceBusReceiverOptions { SubQueue = MessageSourceMapper.Map(source) });
        var messages = await receiver.PeekMessagesAsync(maxCount, cancellationToken: ct);
        return messages.Select(MapMessage).ToList();
    }

    public async Task SendAsync(string name, OutboundMessage message, CancellationToken ct = default)
    {
        var client = RequireClient();
        await using var sender = client.CreateSender(name);
        var msg = new ServiceBusMessage(message.Body)
        {
            ContentType = message.ContentType
        };
        if (message.MessageId != null) msg.MessageId = message.MessageId;
        if (message.CorrelationId != null) msg.CorrelationId = message.CorrelationId;
        if (message.SessionId != null) msg.SessionId = message.SessionId;
        if (message.To != null) msg.To = message.To;
        if (message.Subject != null) msg.Subject = message.Subject;
        if (message.ReplyTo != null) msg.ReplyTo = message.ReplyTo;
        if (message.ReplyToSessionId != null) msg.ReplyToSessionId = message.ReplyToSessionId;
        if (message.PartitionKey != null) msg.PartitionKey = message.PartitionKey;
        if (message.TimeToLive.HasValue) msg.TimeToLive = message.TimeToLive.Value;
        if (message.ScheduledEnqueueTime.HasValue)
            msg.ScheduledEnqueueTime = message.ScheduledEnqueueTime.Value;
        if (message.Properties != null)
            foreach (var (k, v) in message.Properties)
                msg.ApplicationProperties[k] = v;
        await sender.SendMessageAsync(msg, ct);
    }

    public async Task PurgeAsync(
        string name,
        MessageSource source,
        CancellationToken ct = default)
    {
        var client = RequireClient();
        await using var receiver = client.CreateReceiver(name,
            new ServiceBusReceiverOptions
            {
                SubQueue = MessageSourceMapper.Map(source),
                ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
            });
        while (!ct.IsCancellationRequested)
        {
            var batch = await receiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(1), ct);
            if (batch.Count == 0) break;
        }
    }

    public Task<IReceiveSession> OpenReceiveSessionAsync(
        string name,
        MessageSource source,
        CancellationToken ct = default)
    {
        var client = RequireClient();
        var receiver = client.CreateReceiver(name,
            new ServiceBusReceiverOptions { SubQueue = MessageSourceMapper.Map(source) });
        return Task.FromResult<IReceiveSession>(new ReceiveSession(receiver));
    }

    private ServiceBusClient RequireClient() =>
        _client ?? throw new InvalidOperationException(
            "ServiceBusClient is required for messaging operations.");

    private static QueueInfo MapQueue(QueueAdminSnapshot s) => new(
        Name: s.Name,
        ActiveMessageCount: s.ActiveMessageCount,
        DeadLetterCount: s.DeadLetterCount,
        ScheduledMessageCount: s.ScheduledMessageCount,
        LockDuration: s.LockDuration,
        RequiresDuplicateDetection: s.RequiresDuplicateDetection,
        RequiresSession: s.RequiresSession,
        DefaultMessageTimeToLive: s.DefaultMessageTimeToLive,
        Status: s.Status,
        AutoDeleteOnIdle: s.AutoDeleteOnIdle,
        MaxDeliveryCount: s.MaxDeliveryCount,
        MaxSizeInMegabytes: s.MaxSizeInMegabytes,
        EnableBatchedOperations: s.EnableBatchedOperations,
        ForwardTo: s.ForwardTo,
        ForwardDeadLetteredMessagesTo: s.ForwardDeadLetteredMessagesTo,
        UserMetadata: s.UserMetadata,
        DuplicateDetectionHistoryTimeWindow: s.DuplicateDetectionHistoryTimeWindow,
        SizeInBytes: s.SizeInBytes,
        EnableDeadLetteringOnMessageExpiration: s.EnableDeadLetteringOnMessageExpiration,
        ServiceVersion: s.ServiceVersion);

    private static ReceivedMessage MapMessage(ServiceBusReceivedMessage m) => new(
        m.MessageId, m.Body.ToString(), m.ContentType ?? "application/octet-stream",
        m.SequenceNumber, m.DeliveryCount, m.EnqueuedTime, m.ExpiresAt,
        m.CorrelationId, m.SessionId,
        m.ApplicationProperties.ToDictionary(kv => kv.Key, kv => kv.Value),
        m.DeadLetterReason, LockToken: null);
}
