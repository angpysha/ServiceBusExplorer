#nullable enable
using System.Globalization;

namespace ServiceBusExplorer;

/// <summary>
/// Represents a non-negative, whole-millisecond duration across the full TimeSpan range.
/// </summary>
public readonly record struct DurationValue
{
    private const long MillisecondsPerSecond = 1000;
    private const long MillisecondsPerMinute = 60 * MillisecondsPerSecond;
    private const long MillisecondsPerHour = 60 * MillisecondsPerMinute;
    private const long MillisecondsPerDay = 24 * MillisecondsPerHour;

    public const long MaximumTotalMilliseconds = 922337203685477;

    public DurationValue(long totalMilliseconds)
    {
        if (totalMilliseconds is < 0 or > MaximumTotalMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(totalMilliseconds));

        TotalMilliseconds = totalMilliseconds;
    }

    public long TotalMilliseconds { get; }
    public long Days => TotalMilliseconds / MillisecondsPerDay;
    public int Hours => (int)(TotalMilliseconds / MillisecondsPerHour % 24);
    public int Minutes => (int)(TotalMilliseconds / MillisecondsPerMinute % 60);
    public int Seconds => (int)(TotalMilliseconds / MillisecondsPerSecond % 60);
    public int Milliseconds => (int)(TotalMilliseconds % MillisecondsPerSecond);

    public static DurationValue Create(
        long days,
        int hours,
        int minutes,
        int seconds,
        int milliseconds)
    {
        if (TryCreate(
                days,
                hours,
                minutes,
                seconds,
                milliseconds,
                out var value,
                out var errors))
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(
            nameof(days),
            string.Join(" ", errors.Values));
    }

    public static bool TryCreate(
        long days,
        int hours,
        int minutes,
        int seconds,
        int milliseconds,
        out DurationValue value,
        out IReadOnlyDictionary<string, string> errors)
    {
        var validationErrors = new Dictionary<string, string>(StringComparer.Ordinal);
        ValidateComponent(validationErrors, "Days", days, 0, 10675199);
        ValidateComponent(validationErrors, "Hours", hours, 0, 23);
        ValidateComponent(validationErrors, "Minutes", minutes, 0, 59);
        ValidateComponent(validationErrors, "Seconds", seconds, 0, 59);
        ValidateComponent(validationErrors, "Milliseconds", milliseconds, 0, 999);

        long totalMilliseconds = 0;
        if (validationErrors.Count == 0)
        {
            try
            {
                totalMilliseconds = checked(
                    days * MillisecondsPerDay +
                    hours * MillisecondsPerHour +
                    minutes * MillisecondsPerMinute +
                    seconds * MillisecondsPerSecond +
                    milliseconds);
                if (totalMilliseconds > MaximumTotalMilliseconds)
                    validationErrors["Days"] = "The composed duration exceeds the shared duration range.";
            }
            catch (OverflowException)
            {
                validationErrors["Days"] = "The composed duration exceeds the shared duration range.";
            }
        }

        errors = validationErrors;
        value = validationErrors.Count == 0
            ? new DurationValue(totalMilliseconds)
            : default;
        return validationErrors.Count == 0;
    }

    public static DurationValue Parse(string text)
    {
        if (!TryParse(text, out var value))
            throw new FormatException("Duration must use D.HH:MM:SS or D.HH:MM:SS.fff.");

        return value;
    }

    public static bool TryParse(string? text, out DurationValue value)
    {
        value = default;
        if (string.IsNullOrEmpty(text))
            return false;

        var sections = text.Split('.');
        if (sections.Length is < 2 or > 3 ||
            !IsAsciiDigits(sections[0]) ||
            !long.TryParse(
                sections[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var days))
        {
            return false;
        }

        var time = sections[1].Split(':');
        if (time.Length != 3 ||
            time.Any(component => component.Length != 2 || !IsAsciiDigits(component)) ||
            !int.TryParse(time[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(time[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(time[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var milliseconds = 0;
        if (sections.Length == 3 &&
            (sections[2].Length != 3 ||
             !IsAsciiDigits(sections[2]) ||
             !int.TryParse(
                 sections[2],
                 NumberStyles.None,
                 CultureInfo.InvariantCulture,
                 out milliseconds) ||
             milliseconds == 0))
        {
            return false;
        }

        return TryCreate(
            days,
            hours,
            minutes,
            seconds,
            milliseconds,
            out value,
            out _);
    }

    public static DurationValue FromTimeSpan(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(value));
        if (value.Ticks % TimeSpan.TicksPerMillisecond != 0)
            throw new ArgumentException("Duration must be aligned to whole milliseconds.", nameof(value));

        return new DurationValue(value.Ticks / TimeSpan.TicksPerMillisecond);
    }

    public TimeSpan ToTimeSpan() =>
        new(checked(TotalMilliseconds * TimeSpan.TicksPerMillisecond));

    public override string ToString()
    {
        var baseText = string.Create(
            CultureInfo.InvariantCulture,
            $"{Days}.{Hours:00}:{Minutes:00}:{Seconds:00}");
        return Milliseconds == 0
            ? baseText
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{baseText}.{Milliseconds:000}");
    }

    private static void ValidateComponent(
        IDictionary<string, string> errors,
        string field,
        long value,
        long minimum,
        long maximum)
    {
        if (value < minimum || value > maximum)
            errors[field] = $"{field} must be between {minimum} and {maximum}.";
    }

    private static bool IsAsciiDigits(string value) =>
        value.Length > 0 && value.All(character => character is >= '0' and <= '9');
}
