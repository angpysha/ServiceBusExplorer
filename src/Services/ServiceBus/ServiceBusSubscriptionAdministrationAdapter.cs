#nullable enable
using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using CoreEntityStatus = ServiceBusExplorer.EntityStatus;
using SBEntityStatus = Azure.Messaging.ServiceBus.Administration.EntityStatus;
using AzureCreateRuleOptions = Azure.Messaging.ServiceBus.Administration.CreateRuleOptions;
using AzureCreateSubscriptionOptions = Azure.Messaging.ServiceBus.Administration.CreateSubscriptionOptions;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Production adapter over <see cref="ServiceBusAdministrationClient"/>.
/// Maps Azure TrueFilter to <see cref="RuleFilterKind.CatchAll"/> (never as SQL 1=1).
/// Versions are taken from response ETags when present.
/// </summary>
public sealed class ServiceBusSubscriptionAdministrationAdapter : ISubscriptionAdministrationAdapter
{
    private readonly ServiceBusAdministrationClient _admin;

    public ServiceBusSubscriptionAdministrationAdapter(
        ServiceBusAdministrationClient admin,
        ILogger<ServiceBusSubscriptionAdministrationAdapter> log)
    {
        _admin = admin;
        _ = log;
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(
        string topicName,
        CancellationToken cancellationToken)
    {
        var runtimeMap = new Dictionary<string, SubscriptionRuntimeProperties>(StringComparer.OrdinalIgnoreCase);
        await foreach (var r in _admin.GetSubscriptionsRuntimePropertiesAsync(topicName, cancellationToken))
            runtimeMap[r.SubscriptionName] = r;

        var results = new List<SubscriptionInfo>();
        await foreach (var p in _admin.GetSubscriptionsAsync(topicName, cancellationToken))
        {
            if (runtimeMap.TryGetValue(p.SubscriptionName, out var runtime))
                results.Add(MapSubscription(p, runtime, version: null));
        }

        return results;
    }

    public async Task<SubscriptionInfo?> GetSubscriptionAsync(
        string topicName,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            var propsResponse = await _admin.GetSubscriptionAsync(topicName, name, cancellationToken);
            var runtime = await _admin.GetSubscriptionRuntimePropertiesAsync(topicName, name, cancellationToken);
            return MapSubscription(propsResponse.Value, runtime.Value, ExtractVersion(propsResponse));
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            return null;
        }
    }

    public async Task<EntityLifecycleResult<SubscriptionInfo>> CreateSubscriptionAsync(
        CreateSubscriptionOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var createOpts = new AzureCreateSubscriptionOptions(options.TopicName, options.Name);
            if (options.LockDuration.HasValue) createOpts.LockDuration = options.LockDuration.Value;
            if (options.MaxDeliveryCount.HasValue) createOpts.MaxDeliveryCount = options.MaxDeliveryCount.Value;
            if (options.ForwardTo != null) createOpts.ForwardTo = options.ForwardTo;

            var created = await _admin.CreateSubscriptionAsync(createOpts, cancellationToken);
            var runtime = await _admin.GetSubscriptionRuntimePropertiesAsync(
                options.TopicName, options.Name, cancellationToken);
            var mapped = MapSubscription(created.Value, runtime.Value, ExtractVersion(created));
            return EntityLifecycleResult<SubscriptionInfo>.Succeeded(
                mapped, mapped.ServiceVersion, "Subscription created.");
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
        {
            return EntityLifecycleResult<SubscriptionInfo>.Failed("A subscription with this name already exists.");
        }
    }

    public async Task<EntityLifecycleResult<SubscriptionInfo>> UpdateSubscriptionAsync(
        SubscriptionInfo updated,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        Response<SubscriptionProperties> existing;
        try
        {
            existing = await _admin.GetSubscriptionAsync(updated.TopicName, updated.Name, cancellationToken);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            return EntityLifecycleResult<SubscriptionInfo>.Failed("Subscription was not found.");
        }

        var currentVersion = ExtractVersion(existing);
        if (!VersionsMatch(expectedVersion, currentVersion))
        {
            var runtime = await _admin.GetSubscriptionRuntimePropertiesAsync(
                updated.TopicName, updated.Name, cancellationToken);
            var authoritative = MapSubscription(existing.Value, runtime.Value, currentVersion);
            return EntityLifecycleResult<SubscriptionInfo>.Conflict(
                authoritative,
                authoritative.ServiceVersion,
                "Subscription was changed by another client; refreshed authoritative state.");
        }

        var props = existing.Value;
        props.LockDuration = updated.LockDuration;
        props.MaxDeliveryCount = updated.MaxDeliveryCount;
        props.DefaultMessageTimeToLive = updated.DefaultMessageTimeToLive == default
            ? props.DefaultMessageTimeToLive
            : updated.DefaultMessageTimeToLive;
        props.AutoDeleteOnIdle = updated.AutoDeleteOnIdle == default
            ? props.AutoDeleteOnIdle
            : updated.AutoDeleteOnIdle;
        props.EnableBatchedOperations = updated.EnableBatchedOperations;
        props.DeadLetteringOnMessageExpiration = updated.EnableDeadLetteringOnMessageExpiration;
        props.EnableDeadLetteringOnFilterEvaluationExceptions =
            updated.EnableDeadLetteringOnFilterEvaluationExceptions;
        if (updated.ForwardTo != null) props.ForwardTo = updated.ForwardTo;
        if (updated.ForwardDeadLetteredMessagesTo != null)
            props.ForwardDeadLetteredMessagesTo = updated.ForwardDeadLetteredMessagesTo;
        if (updated.UserMetadata != null) props.UserMetadata = updated.UserMetadata;
        props.Status = MapStatus(updated.Status);

        try
        {
            var result = await _admin.UpdateSubscriptionAsync(props, cancellationToken);
            var runtime = await _admin.GetSubscriptionRuntimePropertiesAsync(
                updated.TopicName, updated.Name, cancellationToken);
            var mapped = MapSubscription(result.Value, runtime.Value, ExtractVersion(result));
            return EntityLifecycleResult<SubscriptionInfo>.Succeeded(mapped, mapped.ServiceVersion, "Subscription updated.");
        }
        catch (RequestFailedException ex) when (IsPreconditionFailed(ex))
        {
            var refreshed = await GetSubscriptionAsync(updated.TopicName, updated.Name, cancellationToken);
            if (refreshed is null)
                return EntityLifecycleResult<SubscriptionInfo>.Failed(
                    "Subscription was not found after conflict.");
            return EntityLifecycleResult<SubscriptionInfo>.Conflict(
                refreshed,
                refreshed.ServiceVersion,
                "Subscription was changed by another client; refreshed authoritative state.");
        }
    }

    public async Task<EntityLifecycleResult<SubscriptionInfo?>> DeleteSubscriptionAsync(
        string topicName,
        string name,
        string? expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(expectedVersion))
        {
            var current = await GetSubscriptionAsync(topicName, name, cancellationToken);
            if (current is null)
                return EntityLifecycleResult<SubscriptionInfo?>.Failed("Subscription was not found.");
            if (!VersionsMatch(expectedVersion, current.ServiceVersion))
            {
                return EntityLifecycleResult<SubscriptionInfo?>.Conflict(
                    current,
                    current.ServiceVersion,
                    "Subscription was changed by another client; delete was not applied.");
            }
        }

        try
        {
            await _admin.DeleteSubscriptionAsync(topicName, name, cancellationToken);
            return EntityLifecycleResult<SubscriptionInfo?>.Succeeded(null, null, "Subscription deleted.");
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            return EntityLifecycleResult<SubscriptionInfo?>.Failed("Subscription was not found.");
        }
        catch (RequestFailedException ex) when (IsPreconditionFailed(ex))
        {
            var refreshed = await GetSubscriptionAsync(topicName, name, cancellationToken);
            return EntityLifecycleResult<SubscriptionInfo?>.Conflict(
                refreshed,
                refreshed?.ServiceVersion,
                "Subscription was changed by another client; delete was not applied.");
        }
    }

    public async Task<IReadOnlyList<SubscriptionRule>> ListRulesAsync(
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken)
    {
        var results = new List<SubscriptionRule>();
        await foreach (var rule in _admin.GetRulesAsync(topicName, subscriptionName, cancellationToken))
            results.Add(MapRule(rule, version: null));
        return results;
    }

    public async Task<EntityLifecycleResult<SubscriptionRule>> CreateRuleAsync(
        string topicName,
        string subscriptionName,
        CreateSubscriptionRuleOptions options,
        CancellationToken cancellationToken)
    {
        var filter = MapFilter(options.FilterKind, options.FilterExpression);
        var createOpts = new AzureCreateRuleOptions(options.Name, filter);
        if (options.ActionExpression != null)
            createOpts.Action = new SqlRuleAction(options.ActionExpression);

        try
        {
            var created = await _admin.CreateRuleAsync(topicName, subscriptionName, createOpts, cancellationToken);
            var mapped = MapRule(created.Value, ExtractVersion(created));
            return EntityLifecycleResult<SubscriptionRule>.Succeeded(
                mapped, mapped.ServiceVersion, "Rule created.");
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
        {
            return EntityLifecycleResult<SubscriptionRule>.Failed("A rule with this name already exists.");
        }
    }

    public async Task<EntityLifecycleResult<SubscriptionRule>> UpdateRuleAsync(
        string topicName,
        string subscriptionName,
        SubscriptionRule updated,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        Response<RuleProperties> existing;
        try
        {
            existing = await _admin.GetRuleAsync(topicName, subscriptionName, updated.Name, cancellationToken);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            return EntityLifecycleResult<SubscriptionRule>.Failed("Rule was not found.");
        }

        var currentVersion = ExtractVersion(existing);
        if (!VersionsMatch(expectedVersion, currentVersion))
        {
            var conflictEntity = MapRule(existing.Value, currentVersion);
            return EntityLifecycleResult<SubscriptionRule>.Conflict(
                conflictEntity,
                conflictEntity.ServiceVersion,
                "Rule was changed by another client; refreshed authoritative state.");
        }

        var filter = MapFilter(updated.FilterKind, updated.FilterExpression);

        try
        {
            await _admin.DeleteRuleAsync(topicName, subscriptionName, updated.Name, cancellationToken);
            var createOpts = new AzureCreateRuleOptions(updated.Name, filter);
            if (updated.ActionExpression != null)
                createOpts.Action = new SqlRuleAction(updated.ActionExpression);
            var created = await _admin.CreateRuleAsync(topicName, subscriptionName, createOpts, cancellationToken);
            var mapped = MapRule(created.Value, ExtractVersion(created));
            return EntityLifecycleResult<SubscriptionRule>.Succeeded(
                mapped, mapped.ServiceVersion, "Rule updated.");
        }
        catch (RequestFailedException ex) when (IsPreconditionFailed(ex))
        {
            try
            {
                var refreshed = await _admin.GetRuleAsync(
                    topicName, subscriptionName, updated.Name, cancellationToken);
                var conflictEntity = MapRule(refreshed.Value, ExtractVersion(refreshed));
                return EntityLifecycleResult<SubscriptionRule>.Conflict(
                    conflictEntity,
                    conflictEntity.ServiceVersion,
                    "Rule was changed by another client; refreshed authoritative state.");
            }
            catch (ServiceBusException)
            {
                return EntityLifecycleResult<SubscriptionRule>.Failed(
                    "Rule was not found after conflict.");
            }
        }
    }

    public async Task<EntityLifecycleResult<SubscriptionRule?>> DeleteRuleAsync(
        string topicName,
        string subscriptionName,
        string ruleName,
        string? expectedVersion,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(expectedVersion))
        {
            try
            {
                var existing = await _admin.GetRuleAsync(
                    topicName, subscriptionName, ruleName, cancellationToken);
                var currentVersion = ExtractVersion(existing);
                if (!VersionsMatch(expectedVersion, currentVersion))
                {
                    return EntityLifecycleResult<SubscriptionRule?>.Conflict(
                        MapRule(existing.Value, currentVersion),
                        currentVersion,
                        "Rule was changed by another client; delete was not applied.");
                }
            }
            catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
            {
                return EntityLifecycleResult<SubscriptionRule?>.Failed("Rule was not found.");
            }
        }

        try
        {
            await _admin.DeleteRuleAsync(topicName, subscriptionName, ruleName, cancellationToken);
            return EntityLifecycleResult<SubscriptionRule?>.Succeeded(null, null, "Rule deleted.");
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            return EntityLifecycleResult<SubscriptionRule?>.Failed("Rule was not found.");
        }
    }

    private static RuleFilter MapFilter(RuleFilterKind kind, string? expression) =>
        kind switch
        {
            RuleFilterKind.CatchAll => new TrueRuleFilter(),
            RuleFilterKind.Correlation => new CorrelationRuleFilter
            {
                CorrelationId = expression ?? string.Empty
            },
            RuleFilterKind.Sql => new SqlRuleFilter(
                string.IsNullOrWhiteSpace(expression) ? "1=1" : expression),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown rule filter kind.")
        };

    private static SubscriptionRule MapRule(RuleProperties rule, string? version)
    {
        RuleFilterKind kind;
        string? expression;
        switch (rule.Filter)
        {
            case TrueRuleFilter:
                kind = RuleFilterKind.CatchAll;
                expression = null;
                break;
            case FalseRuleFilter:
                kind = RuleFilterKind.Sql;
                expression = "1=0";
                break;
            case CorrelationRuleFilter correlation:
                kind = RuleFilterKind.Correlation;
                expression = correlation.CorrelationId ?? string.Empty;
                break;
            case SqlRuleFilter sql:
                kind = RuleFilterKind.Sql;
                expression = sql.SqlExpression;
                break;
            default:
                kind = RuleFilterKind.Sql;
                expression = string.Empty;
                break;
        }

        var action = rule.Action is SqlRuleAction sqlAction ? sqlAction.SqlExpression : null;
        return new SubscriptionRule(
            rule.Name,
            kind,
            expression,
            action,
            version ?? string.Empty);
    }

    private static SubscriptionInfo MapSubscription(
        SubscriptionProperties p,
        SubscriptionRuntimeProperties r,
        string? version) =>
        new(
            TopicName: p.TopicName,
            Name: p.SubscriptionName,
            ActiveMessageCount: r.ActiveMessageCount,
            DeadLetterCount: r.DeadLetterMessageCount,
            LockDuration: p.LockDuration,
            MaxDeliveryCount: p.MaxDeliveryCount,
            Status: MapEntityStatus(p.Status),
            DefaultMessageTimeToLive: p.DefaultMessageTimeToLive,
            AutoDeleteOnIdle: p.AutoDeleteOnIdle,
            EnableBatchedOperations: p.EnableBatchedOperations,
            ForwardTo: string.IsNullOrEmpty(p.ForwardTo) ? null : p.ForwardTo,
            ForwardDeadLetteredMessagesTo: string.IsNullOrEmpty(p.ForwardDeadLetteredMessagesTo)
                ? null
                : p.ForwardDeadLetteredMessagesTo,
            UserMetadata: string.IsNullOrEmpty(p.UserMetadata) ? null : p.UserMetadata,
            EnableDeadLetteringOnMessageExpiration: p.DeadLetteringOnMessageExpiration,
            EnableDeadLetteringOnFilterEvaluationExceptions: p.EnableDeadLetteringOnFilterEvaluationExceptions,
            RequiresSession: p.RequiresSession,
            ServiceVersion: version ?? string.Empty);

    private static string? ExtractVersion<T>(Response<T> response)
    {
        var etag = response.GetRawResponse().Headers.ETag;
        return etag?.ToString();
    }

    private static bool VersionsMatch(string expected, string? current) =>
        string.Equals(expected ?? string.Empty, current ?? string.Empty, StringComparison.Ordinal);

    private static bool IsPreconditionFailed(RequestFailedException ex) =>
        ex.Status == 412;

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
