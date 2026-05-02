#nullable enable
namespace ServiceBusExplorer;

public record SubscriptionInfo(
    string TopicName,
    string Name,
    long ActiveMessageCount,
    long DeadLetterCount,
    TimeSpan LockDuration,
    int MaxDeliveryCount,
    EntityStatus Status);

public record CreateSubscriptionOptions(
    string TopicName,
    string Name,
    TimeSpan? LockDuration = null,
    int? MaxDeliveryCount = null,
    string? ForwardTo = null);

public record RuleInfo(
    string Name,
    string FilterType,
    string FilterExpression,
    string? ActionExpression);

public record CreateRuleOptions(
    string Name,
    string FilterExpression,
    string FilterType = "SqlFilter",
    string? ActionExpression = null);
