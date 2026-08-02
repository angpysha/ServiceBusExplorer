using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using ServiceBusExplorer.App.Views.Controls;
using Xunit;

namespace ServiceBusExplorer.UITests.Controls;

public class DurationEditorInteractionTests
{
    [AvaloniaFact]
    public void ProgrammaticBinding_SubMillisecondValues_InitializeAtWholeMillisecondPrecision()
    {
        var cases = new[]
        {
            (Value: TimeSpan.FromTicks(1), ExpectedText: "0.00:00:00"),
            (Value: TimeSpan.MaxValue, ExpectedText: "10675199.02:48:05.477"),
        };

        foreach (var testCase in cases)
        {
            var editor = new DurationEditor
            {
                DataContext = new { Duration = testCase.Value },
            };
            editor.Bind(
                DurationEditor.ValueProperty,
                new Binding("Duration") { Mode = BindingMode.OneWay });
            var window = new Window { Content = editor };

            var exception = Record.Exception(window.Show);

            Assert.Null(exception);
            Assert.Equal(testCase.Value, editor.Value);
            Assert.Equal(testCase.ExpectedText, editor.PrimaryText);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ProgrammaticBinding_NegativeValue_ReportsErrorWithoutEscapingTextChanged()
    {
        var invalid = TimeSpan.FromTicks(-1);
        var editor = ShowEditor(invalid, out var window);
        var primary = Required<TextBox>(editor, "PrimaryInput");

        var exception = Record.Exception(() => primary.Text = "0.00:00:00");

        Assert.Null(exception);
        Assert.Equal(invalid, editor.Value);
        Assert.Contains(
            "non-negative",
            Required<TextBlock>(editor, "PrimaryError").Text,
            StringComparison.OrdinalIgnoreCase);
        window.Close();
    }

    [AvaloniaFact]
    public void Cancel_MaxValueDraft_PreservesExactExternalSentinel()
    {
        var editor = ShowEditor(TimeSpan.MaxValue, out var window);

        Click(Required<Button>(editor, "EditButton"));
        Required<TextBox>(editor, "DaysInput").Text = "1";
        Click(Required<Button>(editor, "CancelButton"));

        Assert.Equal(TimeSpan.MaxValue, editor.Value);
        Assert.Equal("10675199.02:48:05.477", editor.PrimaryText);
        Assert.Empty(editor.FieldErrors);
        window.Close();
    }

    [AvaloniaFact]
    public void Apply_IsTheOnlyActionThatCommitsComponentDrafts()
    {
        var editor = ShowEditor(TimeSpan.FromDays(1), out var window);

        Click(Required<Button>(editor, "EditButton"));
        Required<TextBox>(editor, "DaysInput").Text = "2";
        Required<TextBox>(editor, "HoursInput").Text = "3";
        Required<TextBox>(editor, "MinutesInput").Text = "4";
        Required<TextBox>(editor, "SecondsInput").Text = "5";
        Required<TextBox>(editor, "MillisecondsInput").Text = "6";

        Assert.Equal(TimeSpan.FromDays(1), editor.Value);

        Click(Required<Button>(editor, "ApplyButton"));

        Assert.Equal(
            new TimeSpan(2, 3, 4, 5) + TimeSpan.FromMilliseconds(6),
            editor.Value);
        Assert.False(editor.IsEditorOpen);
        Assert.True(Required<Button>(editor, "EditButton").IsFocused);
        window.Close();
    }

    [AvaloniaFact]
    public void Cancel_RestoresExactOriginalAfterValidAndInvalidDrafts()
    {
        var original = new TimeSpan(12, 3, 4, 5) + TimeSpan.FromMilliseconds(6);
        var editor = ShowEditor(original, out var window);

        Click(Required<Button>(editor, "EditButton"));
        Required<TextBox>(editor, "DaysInput").Text = "20";
        Required<TextBox>(editor, "HoursInput").Text = "24";
        Assert.NotEmpty(editor.FieldErrors);

        Click(Required<Button>(editor, "CancelButton"));

        Assert.Equal(original, editor.Value);
        Assert.Equal("12.03:04:05.006", editor.PrimaryText);
        Assert.Empty(editor.FieldErrors);
        Assert.True(Required<Button>(editor, "EditButton").IsFocused);
        window.Close();
    }

    [AvaloniaFact]
    public void PrimaryDraft_InvalidText_DoesNotMutateValue()
    {
        var original = TimeSpan.FromMinutes(1);
        var editor = ShowEditor(original, out var window);

        Required<TextBox>(editor, "PrimaryInput").Text = "not a duration";

        Assert.Equal(original, editor.Value);
        Assert.Contains("Duration", editor.FieldErrors.Keys);
        window.Close();
    }

    [AvaloniaFact]
    public void KeyboardIncrementAndEscape_ChangeDraftThenRollback()
    {
        var original = TimeSpan.FromDays(1);
        var editor = ShowEditor(original, out var window);
        Click(Required<Button>(editor, "EditButton"));
        var hours = Required<TextBox>(editor, "HoursInput");
        hours.Focus();

        hours.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Up,
            PhysicalKey = PhysicalKey.ArrowUp
        });
        Assert.Equal("1", hours.Text);
        Assert.Equal(original, editor.Value);

        hours.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape,
            PhysicalKey = PhysicalKey.Escape
        });

        Assert.Equal(original, editor.Value);
        Assert.Equal("1.00:00:00", editor.PrimaryText);
        Assert.True(Required<Button>(editor, "EditButton").IsFocused);
        window.Close();
    }

    [AvaloniaFact]
    public void LightDismiss_RollsBackDraftAndRestoresFocus()
    {
        var original = TimeSpan.FromMinutes(1);
        var editor = ShowEditor(original, out var window);
        var editButton = Required<Button>(editor, "EditButton");
        var flyout = editButton.Flyout
            ?? throw new InvalidOperationException("Duration flyout was not found.");
        flyout.ShowAt(editButton);
        Required<TextBox>(editor, "MinutesInput").Text = "10";

        flyout.Hide();

        Assert.Equal(original, editor.Value);
        Assert.Equal("0.00:01:00", editor.PrimaryText);
        Assert.True(editButton.IsFocused);
        window.Close();
    }

    private static DurationEditor ShowEditor(TimeSpan value, out Window window)
    {
        var editor = new DurationEditor { Value = value };
        window = new Window { Content = editor };
        window.Show();
        return editor;
    }

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Required<T>(Control control, string name)
        where T : Control =>
        control.FindControl<T>(name)
        ?? throw new InvalidOperationException($"Required control '{name}' was not found.");
}
