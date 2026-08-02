#nullable enable
using System.Globalization;

namespace ServiceBusExplorer;

/// <summary>
/// Holds duration drafts separately from the bound value until a valid Apply operation.
/// </summary>
public sealed class DurationEditTransaction
{
    private readonly DurationConstraint? _constraint;
    private readonly Dictionary<string, string> _fieldErrors = new(StringComparer.Ordinal);

    public DurationEditTransaction(
        DurationValue original,
        DurationConstraint? constraint = null)
    {
        Original = original;
        _constraint = constraint;
        Restore(original);
    }

    public DurationValue Original { get; }
    public string PrimaryDraft { get; private set; } = string.Empty;
    public string DaysDraft { get; private set; } = string.Empty;
    public string HoursDraft { get; private set; } = string.Empty;
    public string MinutesDraft { get; private set; } = string.Empty;
    public string SecondsDraft { get; private set; } = string.Empty;
    public string MillisecondsDraft { get; private set; } = string.Empty;
    public IReadOnlyDictionary<string, string> FieldErrors => _fieldErrors;
    public string? ContextError { get; private set; }
    public DurationValue? Candidate { get; private set; }

    public void UpdatePrimaryDraft(string draft)
    {
        PrimaryDraft = draft;
        _fieldErrors.Clear();
        ContextError = null;

        if (!DurationValue.TryParse(draft, out var value))
        {
            Candidate = null;
            _fieldErrors["Duration"] =
                "Use D.HH:MM:SS or D.HH:MM:SS.fff with whole non-negative components.";
            return;
        }

        Candidate = value;
        SetComponentDrafts(value);
        ContextError = _constraint?.Validate(value);
    }

    public void UpdateComponents(
        string days,
        string hours,
        string minutes,
        string seconds,
        string milliseconds)
    {
        DaysDraft = days;
        HoursDraft = hours;
        MinutesDraft = minutes;
        SecondsDraft = seconds;
        MillisecondsDraft = milliseconds;
        _fieldErrors.Clear();
        ContextError = null;

        var parsedDays = ParseWholeNumber(days, "Days", long.MaxValue);
        var parsedHours = ParseWholeNumber(hours, "Hours", int.MaxValue);
        var parsedMinutes = ParseWholeNumber(minutes, "Minutes", int.MaxValue);
        var parsedSeconds = ParseWholeNumber(seconds, "Seconds", int.MaxValue);
        var parsedMilliseconds = ParseWholeNumber(
            milliseconds,
            "Milliseconds",
            int.MaxValue);

        if (_fieldErrors.Count > 0)
        {
            Candidate = null;
            return;
        }

        if (!DurationValue.TryCreate(
                parsedDays,
                (int)parsedHours,
                (int)parsedMinutes,
                (int)parsedSeconds,
                (int)parsedMilliseconds,
                out var value,
                out var componentErrors))
        {
            foreach (var (field, error) in componentErrors)
                _fieldErrors[field] = error;
            Candidate = null;
            return;
        }

        Candidate = value;
        PrimaryDraft = value.ToString();
        ContextError = _constraint?.Validate(value);
    }

    public bool TryCommit(out DurationValue value)
    {
        if (Candidate is { } candidate &&
            _fieldErrors.Count == 0 &&
            ContextError is null)
        {
            value = candidate;
            return true;
        }

        value = Original;
        return false;
    }

    public void Cancel() => Restore(Original);

    private void Restore(DurationValue value)
    {
        Candidate = value;
        PrimaryDraft = value.ToString();
        SetComponentDrafts(value);
        _fieldErrors.Clear();
        ContextError = _constraint?.Validate(value);
    }

    private void SetComponentDrafts(DurationValue value)
    {
        DaysDraft = value.Days.ToString(CultureInfo.InvariantCulture);
        HoursDraft = value.Hours.ToString(CultureInfo.InvariantCulture);
        MinutesDraft = value.Minutes.ToString(CultureInfo.InvariantCulture);
        SecondsDraft = value.Seconds.ToString(CultureInfo.InvariantCulture);
        MillisecondsDraft = value.Milliseconds.ToString(CultureInfo.InvariantCulture);
    }

    private long ParseWholeNumber(string text, string field, long maximum)
    {
        if (string.IsNullOrEmpty(text) ||
            text.Any(character => character is < '0' or > '9') ||
            !long.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value) ||
            value > maximum)
        {
            _fieldErrors[field] = $"{field} must be a non-negative whole number.";
            return 0;
        }

        return value;
    }
}
