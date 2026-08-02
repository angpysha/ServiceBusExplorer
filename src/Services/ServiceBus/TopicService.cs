#nullable enable
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

public class TopicService : ITopicService
{
    private readonly IServiceBusAdminAdapter _admin;
    private readonly ILogger<TopicService> _log;

    public TopicService(IServiceBusAdminAdapter admin, ILogger<TopicService> log)
    {
        _admin = admin;
        _log = log;
    }

    public async Task<IReadOnlyList<TopicInfo>> ListAsync(CancellationToken ct = default)
    {
        var snapshots = await _admin.ListTopicsAsync(ct);
        return snapshots.Select(MapTopic).ToList();
    }

    public async Task<TopicInfo> GetAsync(string name, CancellationToken ct = default)
    {
        var snapshot = await _admin.GetTopicAsync(name, ct);
        if (snapshot is null)
            throw new InvalidOperationException($"Topic '{name}' was not found.");
        return MapTopic(snapshot);
    }

    public async Task<EntityLifecycleResult<TopicInfo>> CreateAsync(
        CreateTopicOptions opts,
        CancellationToken ct = default)
    {
        var errors = EntityAdminValidation.ValidateCreateTopic(opts);
        if (errors.Count > 0)
        {
            return EntityLifecycleResult<TopicInfo>.ValidationFailed(
                "Topic create options are invalid.",
                errors.ToArray());
        }

        try
        {
            var created = await _admin.CreateTopicAsync(
                new TopicAdminCreateRequest(
                    opts.Name,
                    opts.DefaultMessageTimeToLive,
                    opts.EnableBatchedOperations,
                    opts.EnablePartitioning),
                ct);
            var entity = MapTopic(created);
            _log.LogInformation("Created topic {TopicName}", entity.Name);
            return EntityLifecycleResult<TopicInfo>.Succeeded(
                entity,
                entity.ServiceVersion,
                $"Topic '{entity.Name}' was created.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Failed to create topic {TopicName}", opts.Name);
            return EntityLifecycleResult<TopicInfo>.Failed(
                $"Failed to create topic '{opts.Name}'.");
        }
    }

    public async Task<EntityLifecycleResult<TopicInfo>> UpdateAsync(
        TopicInfo updated,
        CancellationToken ct = default)
    {
        var current = await _admin.GetTopicAsync(updated.Name, ct);
        if (current is null)
        {
            return EntityLifecycleResult<TopicInfo>.NotFound(
                $"Topic '{updated.Name}' was not found.");
        }

        var errors = EntityAdminValidation.ValidateTopicUpdate(updated, current);
        if (errors.Count > 0)
        {
            return EntityLifecycleResult<TopicInfo>.ValidationFailed(
                "Topic update options are invalid or unsupported.",
                errors.ToArray());
        }

        if (!string.IsNullOrEmpty(updated.ServiceVersion) &&
            !string.Equals(updated.ServiceVersion, current.ServiceVersion, StringComparison.Ordinal))
        {
            var refreshed = MapTopic(current);
            return EntityLifecycleResult<TopicInfo>.Conflict(
                refreshed,
                refreshed.ServiceVersion,
                $"Topic '{updated.Name}' was modified by another client; refresh and retry.");
        }

        try
        {
            var snapshot = current with
            {
                DefaultMessageTimeToLive = updated.DefaultMessageTimeToLive == default
                    ? current.DefaultMessageTimeToLive
                    : updated.DefaultMessageTimeToLive,
                AutoDeleteOnIdle = updated.AutoDeleteOnIdle == default
                    ? current.AutoDeleteOnIdle
                    : updated.AutoDeleteOnIdle,
                MaxSizeInMegabytes = updated.MaxSizeInMegabytes,
                EnableBatchedOperations = updated.EnableBatchedOperations,
                DuplicateDetectionHistoryTimeWindow =
                    updated.DuplicateDetectionHistoryTimeWindow == default
                        ? current.DuplicateDetectionHistoryTimeWindow
                        : updated.DuplicateDetectionHistoryTimeWindow,
                UserMetadata = updated.UserMetadata,
                Status = updated.Status
            };
            var saved = await _admin.UpdateTopicAsync(snapshot, ct);
            var entity = MapTopic(saved);
            return EntityLifecycleResult<TopicInfo>.Succeeded(
                entity,
                entity.ServiceVersion,
                $"Topic '{entity.Name}' was updated.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Failed to update topic {TopicName}", updated.Name);
            return EntityLifecycleResult<TopicInfo>.Failed(
                $"Failed to update topic '{updated.Name}'.");
        }
    }

    public async Task<EntityLifecycleResult<TopicInfo?>> DeleteAsync(
        string name,
        CancellationToken ct = default)
    {
        var existing = await _admin.GetTopicAsync(name, ct);
        if (existing is null)
        {
            return EntityLifecycleResult<TopicInfo?>.NotFound($"Topic '{name}' was not found.");
        }

        try
        {
            await _admin.DeleteTopicAsync(name, ct);
            var stillThere = await _admin.GetTopicAsync(name, ct);
            if (stillThere is not null)
            {
                return EntityLifecycleResult<TopicInfo?>.Failed(
                    $"Topic '{name}' delete did not remove the entity.");
            }

            return EntityLifecycleResult<TopicInfo?>.Succeeded(
                null,
                null,
                $"Topic '{name}' was deleted.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Failed to delete topic {TopicName}", name);
            return EntityLifecycleResult<TopicInfo?>.Failed($"Failed to delete topic '{name}'.");
        }
    }

    private static TopicInfo MapTopic(TopicAdminSnapshot s) => new(
        Name: s.Name,
        SubscriptionCount: s.SubscriptionCount,
        SizeInBytes: s.SizeInBytes,
        EnableBatchedOperations: s.EnableBatchedOperations,
        EnablePartitioning: s.EnablePartitioning,
        Status: s.Status,
        DefaultMessageTimeToLive: s.DefaultMessageTimeToLive,
        AutoDeleteOnIdle: s.AutoDeleteOnIdle,
        MaxSizeInMegabytes: s.MaxSizeInMegabytes,
        UserMetadata: s.UserMetadata,
        DuplicateDetectionHistoryTimeWindow: s.DuplicateDetectionHistoryTimeWindow,
        RequiresDuplicateDetection: s.RequiresDuplicateDetection,
        ServiceVersion: s.ServiceVersion);
}
