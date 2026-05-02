using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using CoreEntityStatus = ServiceBusExplorer.EntityStatus;
using SBEntityStatus = Azure.Messaging.ServiceBus.Administration.EntityStatus;

namespace ServiceBusExplorer.Services;

public class SubscriptionService : ISubscriptionService
{
    private readonly ServiceBusAdministrationClient _admin;
    private readonly ILogger<SubscriptionService> _log;

    public SubscriptionService(ServiceBusAdministrationClient admin, ILogger<SubscriptionService> log)
    {
        _admin = admin;
        _log = log;
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListAsync(string topicName, CancellationToken ct = default)
    {
        var results = new List<SubscriptionInfo>();
        await foreach (var props in _admin.GetSubscriptionsRuntimePropertiesAsync(topicName, ct))
            results.Add(MapRuntime(topicName, props));
        return results;
    }

    public async Task<SubscriptionInfo> GetAsync(string topicName, string name, CancellationToken ct = default)
    {
        var runtime = await _admin.GetSubscriptionRuntimePropertiesAsync(topicName, name, ct);
        var props = await _admin.GetSubscriptionAsync(topicName, name, ct);
        return MapFull(props.Value, runtime.Value);
    }

    public async Task<SubscriptionInfo> CreateAsync(CreateSubscriptionOptions opts, CancellationToken ct = default)
    {
        var createOpts = new Azure.Messaging.ServiceBus.Administration.CreateSubscriptionOptions(opts.TopicName, opts.Name);
        if (opts.LockDuration.HasValue) createOpts.LockDuration = opts.LockDuration.Value;
        if (opts.MaxDeliveryCount.HasValue) createOpts.MaxDeliveryCount = opts.MaxDeliveryCount.Value;
        if (opts.ForwardTo != null) createOpts.ForwardTo = opts.ForwardTo;

        var created = await _admin.CreateSubscriptionAsync(createOpts, ct);
        var runtime = await _admin.GetSubscriptionRuntimePropertiesAsync(opts.TopicName, opts.Name, ct);
        return MapFull(created.Value, runtime.Value);
    }

    public async Task<SubscriptionInfo> UpdateAsync(SubscriptionInfo updated, CancellationToken ct = default)
    {
        var existing = await _admin.GetSubscriptionAsync(updated.TopicName, updated.Name, ct);
        var props = existing.Value;
        props.LockDuration = updated.LockDuration;
        props.MaxDeliveryCount = updated.MaxDeliveryCount;
        props.Status = MapStatus(updated.Status);

        var result = await _admin.UpdateSubscriptionAsync(props, ct);
        var runtime = await _admin.GetSubscriptionRuntimePropertiesAsync(updated.TopicName, updated.Name, ct);
        return MapFull(result.Value, runtime.Value);
    }

    public async Task DeleteAsync(string topicName, string name, CancellationToken ct = default) =>
        await _admin.DeleteSubscriptionAsync(topicName, name, ct);

    public async Task<IReadOnlyList<RuleInfo>> ListRulesAsync(string topicName, string subscriptionName,
        CancellationToken ct = default)
    {
        var results = new List<RuleInfo>();
        await foreach (var rule in _admin.GetRulesAsync(topicName, subscriptionName, ct))
            results.Add(MapRule(rule));
        return results;
    }

    public async Task<RuleInfo> CreateRuleAsync(string topicName, string subscriptionName,
        CreateRuleOptions opts, CancellationToken ct = default)
    {
        RuleFilter filter = opts.FilterType.Equals("CorrelationFilter", StringComparison.OrdinalIgnoreCase)
            ? new CorrelationRuleFilter { CorrelationId = opts.FilterExpression }
            : new SqlRuleFilter(opts.FilterExpression);

        var createOpts = new Azure.Messaging.ServiceBus.Administration.CreateRuleOptions(opts.Name, filter);
        if (opts.ActionExpression != null)
            createOpts.Action = new SqlRuleAction(opts.ActionExpression);

        var created = await _admin.CreateRuleAsync(topicName, subscriptionName, createOpts, ct);
        return MapRule(created.Value);
    }

    public async Task DeleteRuleAsync(string topicName, string subscriptionName, string ruleName,
        CancellationToken ct = default) =>
        await _admin.DeleteRuleAsync(topicName, subscriptionName, ruleName, ct);

    private static SubscriptionInfo MapRuntime(string topicName, SubscriptionRuntimeProperties p) => new(
        topicName, p.SubscriptionName, p.ActiveMessageCount, p.DeadLetterMessageCount,
        TimeSpan.Zero, 10, CoreEntityStatus.Unknown);

    private static SubscriptionInfo MapFull(SubscriptionProperties p, SubscriptionRuntimeProperties r) => new(
        p.TopicName, p.SubscriptionName, r.ActiveMessageCount, r.DeadLetterMessageCount,
        p.LockDuration, p.MaxDeliveryCount, MapEntityStatus(p.Status));

    private static RuleInfo MapRule(RuleProperties r)
    {
        var filter = r.Filter;
        string filterType, filterExpr;
        if (filter is SqlRuleFilter sql) { filterType = "SqlFilter"; filterExpr = sql.SqlExpression; }
        else if (filter is CorrelationRuleFilter cor) { filterType = "CorrelationFilter"; filterExpr = cor.CorrelationId ?? ""; }
        else if (filter is TrueRuleFilter) { filterType = "TrueFilter"; filterExpr = "1=1"; }
        else if (filter is FalseRuleFilter) { filterType = "FalseFilter"; filterExpr = "1=0"; }
        else { filterType = "Unknown"; filterExpr = ""; }
        var actionExpr = r.Action is SqlRuleAction sqlAction ? sqlAction.SqlExpression : null;
        return new RuleInfo(r.Name, filterType, filterExpr, actionExpr);
    }

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
