#nullable enable
namespace ServiceBusExplorer.Services;

/// <summary>
/// Azure administration seam for subscription and rule CRUD. Enables in-memory fakes in contract tests.
/// </summary>
public interface ISubscriptionAdministrationAdapter
{
    Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(
        string topicName,
        CancellationToken cancellationToken);

    Task<SubscriptionInfo?> GetSubscriptionAsync(
        string topicName,
        string name,
        CancellationToken cancellationToken);

    Task<EntityLifecycleResult<SubscriptionInfo>> CreateSubscriptionAsync(
        CreateSubscriptionOptions options,
        CancellationToken cancellationToken);

    Task<EntityLifecycleResult<SubscriptionInfo>> UpdateSubscriptionAsync(
        SubscriptionInfo updated,
        string expectedVersion,
        CancellationToken cancellationToken);

    Task<EntityLifecycleResult<SubscriptionInfo?>> DeleteSubscriptionAsync(
        string topicName,
        string name,
        string? expectedVersion,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionRule>> ListRulesAsync(
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken);

    Task<EntityLifecycleResult<SubscriptionRule>> CreateRuleAsync(
        string topicName,
        string subscriptionName,
        CreateSubscriptionRuleOptions options,
        CancellationToken cancellationToken);

    Task<EntityLifecycleResult<SubscriptionRule>> UpdateRuleAsync(
        string topicName,
        string subscriptionName,
        SubscriptionRule updated,
        string expectedVersion,
        CancellationToken cancellationToken);

    Task<EntityLifecycleResult<SubscriptionRule?>> DeleteRuleAsync(
        string topicName,
        string subscriptionName,
        string ruleName,
        string? expectedVersion,
        CancellationToken cancellationToken);
}
