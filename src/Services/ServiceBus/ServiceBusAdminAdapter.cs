#nullable enable
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Production admin adapter: Azure Service Bus administration + <c>UpdatedAt</c> version tokens.
/// </summary>
public sealed class ServiceBusAdminAdapter : IServiceBusAdminAdapter
{
    private readonly ServiceBusAdministrationClient _admin;

    public ServiceBusAdminAdapter(ServiceBusAdministrationClient admin)
    {
        _admin = admin;
    }

    public async Task<IReadOnlyList<QueueAdminSnapshot>> ListQueuesAsync(CancellationToken cancellationToken)
    {
        var runtimeMap = new Dictionary<string, QueueRuntimeProperties>(StringComparer.OrdinalIgnoreCase);
        await foreach (var runtime in _admin.GetQueuesRuntimePropertiesAsync(cancellationToken))
            runtimeMap[runtime.Name] = runtime;

        var list = new List<QueueAdminSnapshot>();
        await foreach (var props in _admin.GetQueuesAsync(cancellationToken))
        {
            if (runtimeMap.TryGetValue(props.Name, out var runtime))
                list.Add(MapQueue(props, runtime));
        }

        return list;
    }

    public async Task<QueueAdminSnapshot?> GetQueueAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var props = await _admin.GetQueueAsync(name, cancellationToken);
            var runtime = await _admin.GetQueueRuntimePropertiesAsync(name, cancellationToken);
            return MapQueue(props.Value, runtime.Value);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            return null;
        }
    }

    public async Task<QueueAdminSnapshot> CreateQueueAsync(
        QueueAdminCreateRequest request,
        CancellationToken cancellationToken)
    {
        var options = new Azure.Messaging.ServiceBus.Administration.CreateQueueOptions(request.Name)
        {
            RequiresDuplicateDetection = request.RequiresDuplicateDetection,
            RequiresSession = request.RequiresSession
        };
        if (request.LockDuration.HasValue)
            options.LockDuration = request.LockDuration.Value;
        if (request.DefaultMessageTimeToLive.HasValue)
            options.DefaultMessageTimeToLive = request.DefaultMessageTimeToLive.Value;
        if (request.MaxDeliveryCount.HasValue)
            options.MaxDeliveryCount = request.MaxDeliveryCount.Value;

        var created = await _admin.CreateQueueAsync(options, cancellationToken);
        var runtime = await _admin.GetQueueRuntimePropertiesAsync(request.Name, cancellationToken);
        return MapQueue(created.Value, runtime.Value);
    }

    public async Task<QueueAdminSnapshot> UpdateQueueAsync(
        QueueAdminSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var existing = await _admin.GetQueueAsync(snapshot.Name, cancellationToken);
        var props = existing.Value;
        props.LockDuration = snapshot.LockDuration;
        props.DefaultMessageTimeToLive = snapshot.DefaultMessageTimeToLive;
        props.AutoDeleteOnIdle = snapshot.AutoDeleteOnIdle == default
            ? props.AutoDeleteOnIdle
            : snapshot.AutoDeleteOnIdle;
        props.MaxDeliveryCount = snapshot.MaxDeliveryCount;
        props.MaxSizeInMegabytes = snapshot.MaxSizeInMegabytes;
        props.EnableBatchedOperations = snapshot.EnableBatchedOperations;
        props.DeadLetteringOnMessageExpiration = snapshot.EnableDeadLetteringOnMessageExpiration;
        if (snapshot.ForwardTo != null)
            props.ForwardTo = snapshot.ForwardTo;
        if (snapshot.ForwardDeadLetteredMessagesTo != null)
            props.ForwardDeadLetteredMessagesTo = snapshot.ForwardDeadLetteredMessagesTo;
        if (snapshot.UserMetadata != null)
            props.UserMetadata = snapshot.UserMetadata;
        props.Status = MapStatus(snapshot.Status);

        var updated = await _admin.UpdateQueueAsync(props, cancellationToken);
        var runtime = await _admin.GetQueueRuntimePropertiesAsync(snapshot.Name, cancellationToken);
        return MapQueue(updated.Value, runtime.Value);
    }

    public Task DeleteQueueAsync(string name, CancellationToken cancellationToken) =>
        _admin.DeleteQueueAsync(name, cancellationToken);

    public async Task<IReadOnlyList<TopicAdminSnapshot>> ListTopicsAsync(CancellationToken cancellationToken)
    {
        var runtimeMap = new Dictionary<string, TopicRuntimeProperties>(StringComparer.OrdinalIgnoreCase);
        await foreach (var runtime in _admin.GetTopicsRuntimePropertiesAsync(cancellationToken))
            runtimeMap[runtime.Name] = runtime;

        var list = new List<TopicAdminSnapshot>();
        await foreach (var props in _admin.GetTopicsAsync(cancellationToken))
        {
            if (runtimeMap.TryGetValue(props.Name, out var runtime))
                list.Add(MapTopic(props, runtime));
        }

        return list;
    }

    public async Task<TopicAdminSnapshot?> GetTopicAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var props = await _admin.GetTopicAsync(name, cancellationToken);
            var runtime = await _admin.GetTopicRuntimePropertiesAsync(name, cancellationToken);
            return MapTopic(props.Value, runtime.Value);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            return null;
        }
    }

    public async Task<TopicAdminSnapshot> CreateTopicAsync(
        TopicAdminCreateRequest request,
        CancellationToken cancellationToken)
    {
        var options = new Azure.Messaging.ServiceBus.Administration.CreateTopicOptions(request.Name)
        {
            EnableBatchedOperations = request.EnableBatchedOperations,
            EnablePartitioning = request.EnablePartitioning
        };
        if (request.DefaultMessageTimeToLive.HasValue)
            options.DefaultMessageTimeToLive = request.DefaultMessageTimeToLive.Value;

        var created = await _admin.CreateTopicAsync(options, cancellationToken);
        var runtime = await _admin.GetTopicRuntimePropertiesAsync(request.Name, cancellationToken);
        return MapTopic(created.Value, runtime.Value);
    }

    public async Task<TopicAdminSnapshot> UpdateTopicAsync(
        TopicAdminSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var existing = await _admin.GetTopicAsync(snapshot.Name, cancellationToken);
        var props = existing.Value;
        props.EnableBatchedOperations = snapshot.EnableBatchedOperations;
        props.DefaultMessageTimeToLive = snapshot.DefaultMessageTimeToLive == default
            ? props.DefaultMessageTimeToLive
            : snapshot.DefaultMessageTimeToLive;
        props.AutoDeleteOnIdle = snapshot.AutoDeleteOnIdle == default
            ? props.AutoDeleteOnIdle
            : snapshot.AutoDeleteOnIdle;
        props.MaxSizeInMegabytes = snapshot.MaxSizeInMegabytes;
        props.DuplicateDetectionHistoryTimeWindow =
            snapshot.DuplicateDetectionHistoryTimeWindow == default
                ? props.DuplicateDetectionHistoryTimeWindow
                : snapshot.DuplicateDetectionHistoryTimeWindow;
        if (snapshot.UserMetadata != null)
            props.UserMetadata = snapshot.UserMetadata;
        props.Status = MapStatus(snapshot.Status);

        var updated = await _admin.UpdateTopicAsync(props, cancellationToken);
        var runtime = await _admin.GetTopicRuntimePropertiesAsync(snapshot.Name, cancellationToken);
        return MapTopic(updated.Value, runtime.Value);
    }

    public Task DeleteTopicAsync(string name, CancellationToken cancellationToken) =>
        _admin.DeleteTopicAsync(name, cancellationToken);

    internal static string FormatVersion(DateTimeOffset updatedAt) =>
        updatedAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static QueueAdminSnapshot MapQueue(QueueProperties props, QueueRuntimeProperties runtime) =>
        new(
            props.Name,
            FormatVersion(runtime.UpdatedAt),
            props.LockDuration,
            props.DefaultMessageTimeToLive,
            props.AutoDeleteOnIdle,
            props.RequiresDuplicateDetection,
            props.RequiresSession,
            props.MaxDeliveryCount,
            props.MaxSizeInMegabytes,
            props.EnableBatchedOperations,
            props.DeadLetteringOnMessageExpiration,
            string.IsNullOrEmpty(props.ForwardTo) ? null : props.ForwardTo,
            string.IsNullOrEmpty(props.ForwardDeadLetteredMessagesTo)
                ? null
                : props.ForwardDeadLetteredMessagesTo,
            string.IsNullOrEmpty(props.UserMetadata) ? null : props.UserMetadata,
            props.DuplicateDetectionHistoryTimeWindow,
            MapEntityStatus(props.Status),
            runtime.ActiveMessageCount,
            runtime.DeadLetterMessageCount,
            runtime.ScheduledMessageCount,
            runtime.SizeInBytes);

    private static TopicAdminSnapshot MapTopic(TopicProperties props, TopicRuntimeProperties runtime) =>
        new(
            props.Name,
            FormatVersion(runtime.UpdatedAt),
            props.DefaultMessageTimeToLive,
            props.AutoDeleteOnIdle,
            props.MaxSizeInMegabytes,
            props.EnableBatchedOperations,
            props.EnablePartitioning,
            props.RequiresDuplicateDetection,
            props.DuplicateDetectionHistoryTimeWindow,
            string.IsNullOrEmpty(props.UserMetadata) ? null : props.UserMetadata,
            MapEntityStatus(props.Status),
            runtime.SubscriptionCount,
            runtime.SizeInBytes);

    private static EntityStatus MapEntityStatus(Azure.Messaging.ServiceBus.Administration.EntityStatus status)
    {
        if (status == Azure.Messaging.ServiceBus.Administration.EntityStatus.Disabled)
            return EntityStatus.Disabled;
        if (status == Azure.Messaging.ServiceBus.Administration.EntityStatus.SendDisabled)
            return EntityStatus.SendDisabled;
        if (status == Azure.Messaging.ServiceBus.Administration.EntityStatus.ReceiveDisabled)
            return EntityStatus.ReceiveDisabled;
        if (status == Azure.Messaging.ServiceBus.Administration.EntityStatus.Active)
            return EntityStatus.Active;
        return EntityStatus.Unknown;
    }

    private static Azure.Messaging.ServiceBus.Administration.EntityStatus MapStatus(EntityStatus status)
    {
        if (status == EntityStatus.Disabled)
            return Azure.Messaging.ServiceBus.Administration.EntityStatus.Disabled;
        if (status == EntityStatus.SendDisabled)
            return Azure.Messaging.ServiceBus.Administration.EntityStatus.SendDisabled;
        if (status == EntityStatus.ReceiveDisabled)
            return Azure.Messaging.ServiceBus.Administration.EntityStatus.ReceiveDisabled;
        return Azure.Messaging.ServiceBus.Administration.EntityStatus.Active;
    }
}
