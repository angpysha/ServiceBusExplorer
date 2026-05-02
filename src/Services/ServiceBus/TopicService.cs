using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using CoreEntityStatus = ServiceBusExplorer.EntityStatus;
using SBEntityStatus = Azure.Messaging.ServiceBus.Administration.EntityStatus;

namespace ServiceBusExplorer.Services;

public class TopicService : ITopicService
{
    private readonly ServiceBusAdministrationClient _admin;
    private readonly ILogger<TopicService> _log;

    public TopicService(ServiceBusAdministrationClient admin, ILogger<TopicService> log)
    {
        _admin = admin;
        _log = log;
    }

    public async Task<IReadOnlyList<TopicInfo>> ListAsync(CancellationToken ct = default)
    {
        var results = new List<TopicInfo>();
        await foreach (var props in _admin.GetTopicsRuntimePropertiesAsync(ct))
            results.Add(MapRuntime(props));
        return results;
    }

    public async Task<TopicInfo> GetAsync(string name, CancellationToken ct = default)
    {
        var runtime = await _admin.GetTopicRuntimePropertiesAsync(name, ct);
        var props = await _admin.GetTopicAsync(name, ct);
        return MapFull(props.Value, runtime.Value);
    }

    public async Task<TopicInfo> CreateAsync(CreateTopicOptions opts, CancellationToken ct = default)
    {
        var createOpts = new Azure.Messaging.ServiceBus.Administration.CreateTopicOptions(opts.Name)
        {
            EnableBatchedOperations = opts.EnableBatchedOperations,
            EnablePartitioning = opts.EnablePartitioning
        };
        if (opts.DefaultMessageTimeToLive.HasValue)
            createOpts.DefaultMessageTimeToLive = opts.DefaultMessageTimeToLive.Value;

        var created = await _admin.CreateTopicAsync(createOpts, ct);
        var runtime = await _admin.GetTopicRuntimePropertiesAsync(opts.Name, ct);
        return MapFull(created.Value, runtime.Value);
    }

    public async Task<TopicInfo> UpdateAsync(TopicInfo updated, CancellationToken ct = default)
    {
        var existing = await _admin.GetTopicAsync(updated.Name, ct);
        var props = existing.Value;
        props.EnableBatchedOperations = updated.EnableBatchedOperations;
        props.Status = MapStatus(updated.Status);

        var result = await _admin.UpdateTopicAsync(props, ct);
        var runtime = await _admin.GetTopicRuntimePropertiesAsync(updated.Name, ct);
        return MapFull(result.Value, runtime.Value);
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default) =>
        await _admin.DeleteTopicAsync(name, ct);

    private static TopicInfo MapRuntime(TopicRuntimeProperties p) => new(
        p.Name, p.SubscriptionCount, p.SizeInBytes, true, false, CoreEntityStatus.Unknown);

    private static TopicInfo MapFull(TopicProperties p, TopicRuntimeProperties r) => new(
        p.Name, r.SubscriptionCount, r.SizeInBytes,
        p.EnableBatchedOperations, p.EnablePartitioning, MapEntityStatus(p.Status));

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
