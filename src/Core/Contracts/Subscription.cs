#nullable enable
namespace ServiceBusExplorer;

public record SubscriptionInfo(
    string TopicName,
    string Name,
    long ActiveMessageCount,
    long DeadLetterCount,
    TimeSpan LockDuration,
    int MaxDeliveryCount,
    EntityStatus Status,
    // Extended fields for editing
    TimeSpan DefaultMessageTimeToLive = default,
    TimeSpan AutoDeleteOnIdle = default,
    bool EnableBatchedOperations = true,
    string? ForwardTo = null,
    string? ForwardDeadLetteredMessagesTo = null,
    string? UserMetadata = null,
    bool EnableDeadLetteringOnMessageExpiration = false,
    bool EnableDeadLetteringOnFilterEvaluationExceptions = true);

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
