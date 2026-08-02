#nullable enable
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Subscription and rule lifecycle administration with version-aware conflict outcomes.
/// </summary>
public sealed class SubscriptionService : ISubscriptionService
{
    private readonly ISubscriptionAdministrationAdapter _admin;
    private readonly ILogger<SubscriptionService> _log;

    public SubscriptionService(
        ISubscriptionAdministrationAdapter admin,
        ILogger<SubscriptionService> log)
    {
        _admin = admin;
        _log = log;
    }

    public Task<IReadOnlyList<SubscriptionInfo>> ListAsync(
        string topicName,
        CancellationToken ct = default) =>
        _admin.ListSubscriptionsAsync(topicName, ct);

    public async Task<SubscriptionInfo> GetAsync(
        string topicName,
        string name,
        CancellationToken ct = default)
    {
        var found = await _admin.GetSubscriptionAsync(topicName, name, ct);
        if (found is null)
            throw new InvalidOperationException($"Subscription '{topicName}/{name}' was not found.");
        return found;
    }

    public async Task<EntityLifecycleResult<SubscriptionInfo>> CreateAsync(
        CreateSubscriptionOptions opts,
        CancellationToken ct = default)
    {
        var result = await _admin.CreateSubscriptionAsync(opts, ct);
        if (result.IsSuccess)
            _log.LogInformation("Created subscription {Topic}/{Name}", opts.TopicName, opts.Name);
        return result;
    }

    public Task<EntityLifecycleResult<SubscriptionInfo>> UpdateAsync(
        SubscriptionInfo updated,
        CancellationToken ct = default) =>
        _admin.UpdateSubscriptionAsync(updated, updated.ServiceVersion, ct);

    public async Task<EntityLifecycleResult<SubscriptionInfo?>> DeleteAsync(
        string topicName,
        string name,
        string? expectedVersion = null,
        CancellationToken ct = default)
    {
        var result = await _admin.DeleteSubscriptionAsync(topicName, name, expectedVersion, ct);
        if (result.IsSuccess)
            _log.LogInformation("Deleted subscription {Topic}/{Name}", topicName, name);
        return result;
    }

    public Task<IReadOnlyList<SubscriptionRule>> ListRulesAsync(
        string topicName,
        string subscriptionName,
        CancellationToken ct = default) =>
        _admin.ListRulesAsync(topicName, subscriptionName, ct);

    public Task<EntityLifecycleResult<SubscriptionRule>> CreateRuleAsync(
        string topicName,
        string subscriptionName,
        CreateSubscriptionRuleOptions opts,
        CancellationToken ct = default) =>
        _admin.CreateRuleAsync(topicName, subscriptionName, opts, ct);

    public Task<EntityLifecycleResult<SubscriptionRule>> UpdateRuleAsync(
        string topicName,
        string subscriptionName,
        SubscriptionRule updated,
        CancellationToken ct = default) =>
        _admin.UpdateRuleAsync(topicName, subscriptionName, updated, updated.ServiceVersion, ct);

    public Task<EntityLifecycleResult<SubscriptionRule?>> DeleteRuleAsync(
        string topicName,
        string subscriptionName,
        string ruleName,
        string? expectedVersion = null,
        CancellationToken ct = default) =>
        _admin.DeleteRuleAsync(topicName, subscriptionName, ruleName, expectedVersion, ct);
}
