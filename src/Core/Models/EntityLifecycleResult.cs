#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Result category for queue/topic (and later subscription/rule) lifecycle mutations.
/// </summary>
public enum EntityLifecycleKind
{
    /// <summary>Mutation applied; entity reflects authoritative service state.</summary>
    Succeeded,

    /// <summary>
    /// Caller version did not match the current service version; no overwrite occurred.
    /// <see cref="EntityLifecycleResult{T}.Entity"/> carries the refreshed current value when available.
    /// </summary>
    Conflict,

    /// <summary>Options or field changes are unsupported or out of range; no service mutation.</summary>
    ValidationFailed,

    /// <summary>Service or transport failure; no claim of success.</summary>
    Failed,

    /// <summary>Target entity was not found.</summary>
    NotFound
}

/// <summary>
/// Secret-safe lifecycle mutation outcome with optional authoritative entity and service version.
/// </summary>
public sealed record EntityLifecycleResult<T>(
    EntityLifecycleKind Kind,
    T? Entity,
    string? ServiceVersion,
    string SafeMessage,
    IReadOnlyList<string>? ValidationErrors = null)
{
    public bool IsSuccess => Kind == EntityLifecycleKind.Succeeded;

    public static EntityLifecycleResult<T> Succeeded(
        T entity,
        string? serviceVersion,
        string safeMessage) =>
        new(EntityLifecycleKind.Succeeded, entity, serviceVersion, safeMessage);

    public static EntityLifecycleResult<T> Conflict(
        T? currentEntity,
        string? currentVersion,
        string safeMessage) =>
        new(EntityLifecycleKind.Conflict, currentEntity, currentVersion, safeMessage);

    public static EntityLifecycleResult<T> ValidationFailed(
        string safeMessage,
        params string[] errors) =>
        new(
            EntityLifecycleKind.ValidationFailed,
            default,
            null,
            safeMessage,
            errors.Length == 0 ? null : errors);

    public static EntityLifecycleResult<T> Failed(string safeMessage) =>
        new(EntityLifecycleKind.Failed, default, null, safeMessage);

    public static EntityLifecycleResult<T> NotFound(string safeMessage) =>
        new(EntityLifecycleKind.NotFound, default, null, safeMessage);
}
