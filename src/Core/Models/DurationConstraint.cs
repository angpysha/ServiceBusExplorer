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
