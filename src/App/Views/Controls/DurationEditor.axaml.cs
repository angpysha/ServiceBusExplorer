using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Reactive.Linq;

namespace ServiceBusExplorer.App.Views.Controls;

public partial class DurationEditor : UserControl
{
    public static readonly StyledProperty<TimeSpan> ValueProperty =
        AvaloniaProperty.Register<DurationEditor, TimeSpan>(nameof(Value),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<DurationConstraint?> ConstraintProperty =
        AvaloniaProperty.Register<DurationEditor, DurationConstraint?>(nameof(Constraint));

    public TimeSpan Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public DurationConstraint? Constraint
    {
        get => GetValue(ConstraintProperty);
        set => SetValue(ConstraintProperty, value);
    }

    public string PrimaryText => PrimaryInput.Text ?? string.Empty;
    public bool IsEditorOpen { get; private set; }
    public IReadOnlyDictionary<string, string> FieldErrors =>
        _transaction?.FieldErrors ?? EmptyErrors;

    private static readonly IReadOnlyDictionary<string, string> EmptyErrors =
        new Dictionary<string, string>();

    private DurationEditTransaction? _transaction;
    private string? _boundValueError;
    private bool _synchronizing;
    private bool _applied;

    public DurationEditor()
    {
        InitializeComponent();

        this.GetObservable(ValueProperty).Subscribe(_ => SynchronizeFromValue());
        this.GetObservable(ConstraintProperty).Subscribe(_ => Revalidate());
        PrimaryInput.GetObservable(TextBox.TextProperty)
            .Subscribe(_ => UpdatePrimaryDraft());
        DaysInput.GetObservable(TextBox.TextProperty)
            .Merge(HoursInput.GetObservable(TextBox.TextProperty))
            .Merge(MinutesInput.GetObservable(TextBox.TextProperty))
            .Merge(SecondsInput.GetObservable(TextBox.TextProperty))
            .Merge(MillisecondsInput.GetObservable(TextBox.TextProperty))
            .Subscribe(_ => UpdateComponentDrafts());
    }

    private void SynchronizeFromValue()
    {
        if (_synchronizing || IsEditorOpen)
            return;

        _synchronizing = true;
        try
        {
            _transaction = null;
            if (EnsureTransaction())
            {
                SynchronizeDraftControls();
            }
            else
            {
                PrimaryInput.Text = string.Empty;
                ShowErrors();
            }
        }
        finally
        {
            _synchronizing = false;
        }
    }

    private void OnPrimaryTextChanged(object? sender, TextChangedEventArgs e)
        => UpdatePrimaryDraft();

    private void UpdatePrimaryDraft()
    {
        if (_synchronizing)
            return;

        EnsureTransaction();
        _transaction?.UpdatePrimaryDraft(PrimaryInput.Text ?? string.Empty);
        if (_transaction?.Candidate is { } candidate)
        {
            _synchronizing = true;
            SetComponentTexts(candidate);
            _synchronizing = false;
        }

        ShowErrors();
    }

    private void OnFlyoutOpened(object? sender, EventArgs e)
    {
        if (!EnsureTransaction())
            return;

        IsEditorOpen = true;
        _applied = false;
        SynchronizeDraftControls();
        DaysInput.Focus();
    }

    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if (!EnsureTransaction())
            return;

        IsEditorOpen = true;
        _applied = false;
        SynchronizeDraftControls();
    }

    private void OnFlyoutClosed(object? sender, EventArgs e)
    {
        if (!_applied)
            _transaction?.Cancel();

        IsEditorOpen = false;
        _applied = false;
        SynchronizeDraftControls();
        EditButton.Focus();
    }

    private void OnComponentTextChanged(object? sender, TextChangedEventArgs e)
        => UpdateComponentDrafts();

    private void UpdateComponentDrafts()
    {
        if (_synchronizing)
            return;

        EnsureTransaction();
        _transaction?.UpdateComponents(
            DaysInput.Text ?? string.Empty,
            HoursInput.Text ?? string.Empty,
            MinutesInput.Text ?? string.Empty,
            SecondsInput.Text ?? string.Empty,
            MillisecondsInput.Text ?? string.Empty);
        if (_transaction?.Candidate is { } candidate)
        {
            _synchronizing = true;
            PrimaryInput.Text = candidate.ToString();
            _synchronizing = false;
        }

        ShowErrors();
    }

    private void OnComponentKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelAndClose();
            e.Handled = true;
            return;
        }

        if (sender is not TextBox input || e.Key is not (Key.Up or Key.Down))
            return;

        var (minimum, maximum) = input.Name switch
        {
            nameof(DaysInput) => (0L, 10675199L),
            nameof(HoursInput) => (0L, 23L),
            nameof(MinutesInput) => (0L, 59L),
            nameof(SecondsInput) => (0L, 59L),
            nameof(MillisecondsInput) => (0L, 999L),
            _ => (0L, 0L)
        };
        _ = long.TryParse(input.Text, out var current);
        var delta = e.Key == Key.Up ? 1 : -1;
        input.Text = Math.Clamp(current + delta, minimum, maximum).ToString();
        e.Handled = true;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        CancelAndClose();
        e.Handled = true;
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        EnsureTransaction();
        _transaction?.UpdateComponents(
            DaysInput.Text ?? string.Empty,
            HoursInput.Text ?? string.Empty,
            MinutesInput.Text ?? string.Empty,
            SecondsInput.Text ?? string.Empty,
            MillisecondsInput.Text ?? string.Empty);

        if (_transaction is null || !_transaction.TryCommit(out var committed))
        {
            ShowErrors();
            return;
        }

        _applied = true;
        Value = committed.ToTimeSpan();
        EditButton.Flyout?.Hide();
        IsEditorOpen = false;
        EditButton.Focus();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => CancelAndClose();

    private void CancelAndClose()
    {
        _transaction?.Cancel();
        _applied = false;
        EditButton.Flyout?.Hide();
        IsEditorOpen = false;
        SynchronizeDraftControls();
        EditButton.Focus();
    }

    private bool EnsureTransaction()
    {
        if (_transaction is not null)
            return true;

        if (Value < TimeSpan.Zero)
        {
            _boundValueError = "Duration must be non-negative.";
            ShowErrors();
            return false;
        }

        // Azure represents unlimited entity durations as TimeSpan.MaxValue, whose final
        // 5,807 ticks exceed whole-millisecond precision. Truncate only at this App
        // boundary; DurationValue remains strict and the exact bound value is preserved
        // unless the user explicitly applies an edit.
        var wholeMillisecondTicks =
            Value.Ticks - Value.Ticks % TimeSpan.TicksPerMillisecond;
        _transaction = new DurationEditTransaction(
            DurationValue.FromTimeSpan(TimeSpan.FromTicks(wholeMillisecondTicks)),
            Constraint);
        _boundValueError = null;
        return true;
    }

    private void Revalidate()
    {
        _transaction = null;
        if (EnsureTransaction())
            SynchronizeDraftControls();
    }

    private void SynchronizeDraftControls()
    {
        if (_transaction is null)
            return;

        _synchronizing = true;
        PrimaryInput.Text = _transaction.PrimaryDraft;
        DaysInput.Text = _transaction.DaysDraft;
        HoursInput.Text = _transaction.HoursDraft;
        MinutesInput.Text = _transaction.MinutesDraft;
        SecondsInput.Text = _transaction.SecondsDraft;
        MillisecondsInput.Text = _transaction.MillisecondsDraft;
        _synchronizing = false;
        ShowErrors();
    }

    private void SetComponentTexts(DurationValue value)
    {
        DaysInput.Text = value.Days.ToString();
        HoursInput.Text = value.Hours.ToString();
        MinutesInput.Text = value.Minutes.ToString();
        SecondsInput.Text = value.Seconds.ToString();
        MillisecondsInput.Text = value.Milliseconds.ToString();
    }

    private void ShowErrors()
    {
        SetFieldError("Days", DaysInput, DaysError, "Non-negative whole number of days.");
        SetFieldError("Hours", HoursInput, HoursError, "Whole number from 0 through 23.");
        SetFieldError("Minutes", MinutesInput, MinutesError, "Whole number from 0 through 59.");
        SetFieldError("Seconds", SecondsInput, SecondsError, "Whole number from 0 through 59.");
        SetFieldError(
            "Milliseconds",
            MillisecondsInput,
            MillisecondsError,
            "Whole number from 0 through 999.");
        PrimaryError.Text = _boundValueError ?? GetFieldError("Duration");
        ContextError.Text = _transaction?.ContextError;
    }

    private void SetFieldError(
        string field,
        Control input,
        TextBlock errorText,
        string defaultHelp)
    {
        var error = GetFieldError(field);
        errorText.Text = error;
        AutomationProperties.SetHelpText(
            input,
            string.IsNullOrEmpty(error) ? defaultHelp : error);
    }

    private string? GetFieldError(string field) =>
        _transaction?.FieldErrors.TryGetValue(field, out var error) == true
            ? error
            : null;
}
