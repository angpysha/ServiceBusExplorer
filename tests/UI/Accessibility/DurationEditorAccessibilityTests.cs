using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ServiceBusExplorer.App.Views.Controls;
using Xunit;

namespace ServiceBusExplorer.UITests.Accessibility;

public class DurationEditorAccessibilityTests
{
    [AvaloniaFact]
    public void Editor_ExposesLogicalNamesHelpAndFocusOrder()
    {
        var editor = new DurationEditor();
        var window = new Window { Content = editor };
        window.Show();

        var orderedControls = new Control[]
        {
            Required<TextBox>(editor, "PrimaryInput"),
            Required<Button>(editor, "EditButton"),
            Required<TextBox>(editor, "DaysInput"),
            Required<TextBox>(editor, "HoursInput"),
            Required<TextBox>(editor, "MinutesInput"),
            Required<TextBox>(editor, "SecondsInput"),
            Required<TextBox>(editor, "MillisecondsInput"),
            Required<Button>(editor, "ApplyButton"),
            Required<Button>(editor, "CancelButton")
        };

        Assert.Equal(
            new[]
            {
                "Duration value",
                "Edit duration",
                "Days",
                "Hours",
                "Minutes",
                "Seconds",
                "Milliseconds",
                "Apply duration",
                "Cancel duration"
            },
            orderedControls.Select(AutomationProperties.GetName));
        Assert.Contains(
            "D.HH:MM:SS",
            AutomationProperties.GetHelpText(orderedControls[0]));
        Assert.Equal(
            orderedControls.Select(control => control.TabIndex).Order(),
            orderedControls.Select(control => control.TabIndex));

        window.Close();
    }

    [AvaloniaFact]
    public void StructuredEditor_UsesPersistentFullComponentLabels()
    {
        var editor = new DurationEditor();
        var window = new Window { Content = editor };
        window.Show();

        foreach (var label in new[] { "Days", "Hours", "Minutes", "Seconds", "Milliseconds" })
            Assert.Equal(label, Required<TextBlock>(editor, $"{label}Label").Text);

        window.Close();
    }

    private static T Required<T>(Control control, string name)
        where T : Control =>
        control.FindControl<T>(name)
        ?? throw new InvalidOperationException($"Required control '{name}' was not found.");
}
