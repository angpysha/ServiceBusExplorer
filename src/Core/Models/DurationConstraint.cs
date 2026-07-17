#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Validates a shared duration against the inclusive limits of one named Azure property.
/// </summary>
public sealed record DurationConstraint(
    string PropertyName,
    DurationValue? Minimum = null,
    DurationValue? Maximum = null)
{
    /// <summary>
    /// Gets the Azure Service Bus constraint for a queue or subscription lock duration.
    /// </summary>
    public static DurationConstraint LockDuration { get; } = new(
        nameof(LockDuration),
        DurationValue.Create(0, 0, 0, 5, 0),
        DurationValue.Create(0, 0, 5, 0, 0));

    /// <summary>
    /// Gets the Azure Service Bus constraint for an entity's default message time to live.
    /// </summary>
    public static DurationConstraint DefaultMessageTimeToLive { get; } = new(
        nameof(DefaultMessageTimeToLive),
        DurationValue.Create(0, 0, 0, 1, 0));

    /// <summary>
    /// Gets the Azure Service Bus constraint for automatic deletion after an idle interval.
    /// </summary>
    public static DurationConstraint AutoDeleteOnIdle { get; } = new(
        nameof(AutoDeleteOnIdle),
        DurationValue.Create(0, 0, 5, 0, 0));

    public string? Validate(DurationValue value)
    {
        if (Minimum is { } minimum && value.TotalMilliseconds < minimum.TotalMilliseconds)
        {
            return $"{PropertyName} must be at least {minimum}.";
        }

        if (Maximum is { } maximum && value.TotalMilliseconds > maximum.TotalMilliseconds)
        {
            return $"{PropertyName} must be no more than {maximum}.";
        }

        return null;
    }
}
