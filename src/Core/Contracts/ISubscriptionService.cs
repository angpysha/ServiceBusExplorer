#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Subscription and rule administration with version-aware conflict outcomes.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionInfo>> ListAsync(string topicName, CancellationToken ct = default);

    Task<SubscriptionInfo> GetAsync(string topicName, string name, CancellationToken ct = default);

    Task<EntityLifecycleResult<SubscriptionInfo>> CreateAsync(
        CreateSubscriptionOptions opts,
        CancellationToken ct = default);

    /// <summary>
    /// Updates a subscription when <see cref="SubscriptionInfo.ServiceVersion"/> matches the service.
    /// On mismatch returns <see cref="EntityLifecycleKind.Conflict"/> with authoritative entity.
    /// </summary>
    Task<EntityLifecycleResult<SubscriptionInfo>> UpdateAsync(
        SubscriptionInfo updated,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a subscription. When <paramref name="expectedVersion"/> is provided and stale,
    /// returns conflict without deleting.
    /// </summary>
    Task<EntityLifecycleResult<SubscriptionInfo?>> DeleteAsync(
        string topicName,
        string name,
        string? expectedVersion = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<SubscriptionRule>> ListRulesAsync(
        string topicName,
        string subscriptionName,
        CancellationToken ct = default);

    Task<EntityLifecycleResult<SubscriptionRule>> CreateRuleAsync(
        string topicName,
        string subscriptionName,
        CreateSubscriptionRuleOptions opts,
        CancellationToken ct = default);

    Task<EntityLifecycleResult<SubscriptionRule>> UpdateRuleAsync(
        string topicName,
        string subscriptionName,
        SubscriptionRule updated,
        CancellationToken ct = default);

    Task<EntityLifecycleResult<SubscriptionRule?>> DeleteRuleAsync(
        string topicName,
        string subscriptionName,
        string ruleName,
        string? expectedVersion = null,
        CancellationToken ct = default);
}
