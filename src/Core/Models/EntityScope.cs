#nullable enable
namespace ServiceBusExplorer;

public enum ScopedEntityKind
{
    None,
    Unspecified,
    Queue,
    Topic,
    Subscription,
}

public enum BrowseSurface
{
    Queues,
    Topics,
}

/// <summary>
/// Parses entity paths and resolves which browse surfaces are permitted.
/// </summary>
public static class EntityScopeHelper
{
    private const string SubscriptionSegment = "/Subscriptions/";

    public static ScopedEntityKind ParseKind(string? entityPath, ScopedEntityKind? declaredKind = null)
    {
        if (declaredKind is ScopedEntityKind.Queue or ScopedEntityKind.Topic or ScopedEntityKind.Subscription)
            return declaredKind.Value;

        if (string.IsNullOrWhiteSpace(entityPath))
            return ScopedEntityKind.None;

        if (TryParseSubscription(entityPath, out _, out _))
            return ScopedEntityKind.Subscription;

        return ScopedEntityKind.Unspecified;
    }

    public static bool TryParseSubscription(
        string entityPath,
        out string topicName,
        out string subscriptionName)
    {
        topicName = string.Empty;
        subscriptionName = string.Empty;

        var index = entityPath.IndexOf(SubscriptionSegment, StringComparison.OrdinalIgnoreCase);
        if (index <= 0)
            return false;

        topicName = entityPath[..index];
        subscriptionName = entityPath[(index + SubscriptionSegment.Length)..];
        return !string.IsNullOrWhiteSpace(topicName) && !string.IsNullOrWhiteSpace(subscriptionName);
    }

    public static string GetRootEntityName(string? entityPath)
    {
        if (string.IsNullOrWhiteSpace(entityPath))
            return string.Empty;

        return TryParseSubscription(entityPath, out var topicName, out _)
            ? topicName
            : entityPath;
    }

    public static bool PermitsSurface(ScopedEntityKind kind, BrowseSurface surface)
    {
        return kind switch
        {
            ScopedEntityKind.Queue => surface == BrowseSurface.Queues,
            ScopedEntityKind.Topic => surface == BrowseSurface.Topics,
            ScopedEntityKind.Subscription => surface == BrowseSurface.Topics,
            ScopedEntityKind.Unspecified => false,
            _ => false,
        };
    }
}
