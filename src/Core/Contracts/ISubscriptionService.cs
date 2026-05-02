#nullable enable
namespace ServiceBusExplorer;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionInfo>> ListAsync(string topicName, CancellationToken ct = default);
    Task<SubscriptionInfo> GetAsync(string topicName, string name, CancellationToken ct = default);
    Task<SubscriptionInfo> CreateAsync(CreateSubscriptionOptions opts, CancellationToken ct = default);
    Task<SubscriptionInfo> UpdateAsync(SubscriptionInfo updated, CancellationToken ct = default);
    Task DeleteAsync(string topicName, string name, CancellationToken ct = default);
    Task<IReadOnlyList<RuleInfo>> ListRulesAsync(string topicName, string subscriptionName,
        CancellationToken ct = default);
    Task<RuleInfo> CreateRuleAsync(string topicName, string subscriptionName,
        CreateRuleOptions opts, CancellationToken ct = default);
    Task DeleteRuleAsync(string topicName, string subscriptionName, string ruleName,
        CancellationToken ct = default);
}
