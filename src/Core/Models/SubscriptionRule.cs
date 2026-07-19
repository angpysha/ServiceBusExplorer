#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Typed filter kind for a subscription rule. Catch-all is explicit and must not be
/// silently represented as a SQL expression such as <c>1=1</c>.
/// </summary>
public enum RuleFilterKind
{
    Sql,
    Correlation,
    CatchAll
}

/// <summary>
/// Named subscription rule with typed filter, optional action, and opaque service version.
/// </summary>
public sealed record SubscriptionRule(
    string Name,
    RuleFilterKind FilterKind,
    string? FilterExpression,
    string? ActionExpression,
    string ServiceVersion)
{
    /// <summary>True when this rule is an explicit catch-all (True filter).</summary>
    public bool IsCatchAll => FilterKind == RuleFilterKind.CatchAll;

    /// <summary>Short display label for UI lists.</summary>
    public string FilterDisplay =>
        FilterKind switch
        {
            RuleFilterKind.CatchAll => "Catch-all",
            RuleFilterKind.Correlation => $"Correlation: {FilterExpression}",
            RuleFilterKind.Sql => FilterExpression ?? string.Empty,
            _ => FilterExpression ?? string.Empty
        };
}

/// <summary>Create options for a typed subscription rule.</summary>
public sealed record CreateSubscriptionRuleOptions(
    string Name,
    RuleFilterKind FilterKind,
    string? FilterExpression = null,
    string? ActionExpression = null);
