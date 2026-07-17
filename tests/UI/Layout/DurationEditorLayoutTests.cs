using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ServiceBusExplorer.App.Views.Controls;
using Xunit;

namespace ServiceBusExplorer.UITests.Layout;

public class DurationEditorLayoutTests
{
    [AvaloniaFact]
    public void MinimumWidthScaleMatrix_KeepsCompleteEditorVisible()
    {
        foreach (var scale in new[] { 1.0, 1.5, 2.0 })
            AssertCompleteEditorAtScale(scale);
    }

    [AvaloniaFact]
    public void SharedNumericStyleScaleMatrix_KeepsDigitsAndStepperButtonsVisible()
    {
        foreach (var scale in new[] { 1.0, 1.5, 2.0 })
            AssertCompleteNumericInputAtScale(scale);
    }

    private static void AssertCompleteEditorAtScale(double scale)
    {
        var maximum = new DurationValue(DurationValue.MaximumTotalMilliseconds);
        var editor = new DurationEditor
        {
            Value = maximum.ToTimeSpan(),
            Width = 600
        };
        var window = new Window
        {
            Width = 820,
            Height = 700,
            Content = editor
        };
        window.Show();
        AssertRenderedAtScale(window, scale);

        var primary = Assert.IsType<TextBox>(editor.FindControl<TextBox>("PrimaryInput"));
        Assert.Equal("10675199.02:48:05.477", primary.Text);
        AssertTextFits(primary, primary.Text!);

        var editButton = Assert.IsType<Button>(editor.FindControl<Button>("EditButton"));
        var flyout = Assert.IsAssignableFrom<Flyout>(editButton.Flyout);
        flyout.ShowAt(editButton);
        Dispatcher.UIThread.RunJobs();

        var inputs = new[]
        {
            Assert.IsType<TextBox>(editor.FindControl<TextBox>("DaysInput")),
            Assert.IsType<TextBox>(editor.FindControl<TextBox>("HoursInput")),
            Assert.IsType<TextBox>(editor.FindControl<TextBox>("MinutesInput")),
            Assert.IsType<TextBox>(editor.FindControl<TextBox>("SecondsInput")),
            Assert.IsType<TextBox>(editor.FindControl<TextBox>("MillisecondsInput"))
        };
        var labels = new[]
        {
            Assert.IsType<TextBlock>(editor.FindControl<TextBlock>("DaysLabel")),
            Assert.IsType<TextBlock>(editor.FindControl<TextBlock>("HoursLabel")),
            Assert.IsType<TextBlock>(editor.FindControl<TextBlock>("MinutesLabel")),
            Assert.IsType<TextBlock>(editor.FindControl<TextBlock>("SecondsLabel")),
            Assert.IsType<TextBlock>(editor.FindControl<TextBlock>("MillisecondsLabel"))
        };
        var apply = Assert.IsType<Button>(editor.FindControl<Button>("ApplyButton"));
        var cancel = Assert.IsType<Button>(editor.FindControl<Button>("CancelButton"));

        Assert.Equal(new[] { "10675199", "2", "48", "5", "477" }, inputs.Select(input => input.Text));
        foreach (var input in inputs)
            AssertTextFits(input, input.Text!);

        var popup = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(inputs[0]));
        var visibleControls = labels.Cast<Control>().Concat(inputs).Append(apply).Append(cancel).ToArray();
        foreach (var control in visibleControls)
            AssertInside(control, popup);

        AssertNoOverlap(inputs.Cast<Control>().Append(apply).Append(cancel), popup);
        AssertRenderedAtScale(popup, scale);

        inputs[1].Text = "24";
        Dispatcher.UIThread.RunJobs();
        var error = Assert.IsType<TextBlock>(editor.FindControl<TextBlock>("HoursError"));
        Assert.False(string.IsNullOrWhiteSpace(error.Text));
        AssertInside(error, popup);

        flyout.Hide();
        window.Close();
    }

    private static void AssertTextFits(TextBox textBox, string text)
    {
        var conservativeTextWidth = text.Length * textBox.FontSize * 0.55;
        Assert.True(
            textBox.Bounds.Width >= conservativeTextWidth,
            $"'{text}' needs {conservativeTextWidth:F1} DIPs but has {textBox.Bounds.Width:F1}.");
    }

    private static void AssertCompleteNumericInputAtScale(double scale)
    {
        var app = new ServiceBusExplorer.App.App();
        app.Initialize();
        var numericStyle = app.Styles
            .OfType<Style>()
            .Single(style => style.Selector?.ToString() == "NumericUpDown");
        app.Styles.Remove(numericStyle);
        var input = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 81920,
            Increment = 1,
            Value = 81920
        };
        var window = new Window
        {
            Width = 820,
            Height = 200,
            Content = input
        };
        window.Styles.Add(numericStyle);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AssertRenderedAtScale(window, scale);

        Assert.True(input.Bounds.Width >= 144, $"Numeric input is only {input.Bounds.Width:F1} DIPs wide.");
        var textBox = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        AssertTextFits(textBox, "81920");

        var buttons = input.GetVisualDescendants().OfType<Button>().Cast<Control>().ToArray();
        Assert.Equal(2, buttons.Length);
        var visibleControls = buttons.Prepend(textBox).ToArray();
        foreach (var control in visibleControls)
            AssertInside(control, window);
        var buttonBounds = buttons
            .Select(
                button =>
                    new Rect(
                        button.TranslatePoint(default, window)
                            ?? throw new InvalidOperationException($"{button.Name} has no window position."),
                        button.Bounds.Size))
            .ToArray();
        Assert.False(buttonBounds[0].Intersects(buttonBounds[1]), "The increase and decrease buttons overlap.");
        var textWidthWithoutSpinner = textBox.Bounds.Width - buttons.Max(button => button.Bounds.Width);
        var requiredTextWidth = "81920".Length * textBox.FontSize * 0.55;
        Assert.True(
            textWidthWithoutSpinner >= requiredTextWidth,
            $"Digits have {textWidthWithoutSpinner:F1} DIPs before the spinner but need {requiredTextWidth:F1}.");

        window.Close();
    }

    private static void AssertInside(Control control, TopLevel topLevel)
    {
        var origin = control.TranslatePoint(default, topLevel);
        Assert.True(origin.HasValue, $"{control.Name} could not be located in its top level.");

        var bounds = new Rect(origin.Value, control.Bounds.Size);
        var viewport = new Rect(topLevel.ClientSize);
        Assert.True(
            viewport.Contains(bounds),
            $"{control.Name} bounds {bounds} exceed viewport {viewport}.");
    }

    private static void AssertNoOverlap(
        IEnumerable<Control> controls,
        TopLevel topLevel)
    {
        var rectangles = controls
            .Select(
                control =>
                {
                    var origin = control.TranslatePoint(default, topLevel)
                        ?? throw new InvalidOperationException($"{control.Name} has no top-level position.");
                    return (control.Name, Bounds: new Rect(origin, control.Bounds.Size));
                })
            .ToArray();

        for (var first = 0; first < rectangles.Length; first++)
        {
            for (var second = first + 1; second < rectangles.Length; second++)
            {
                Assert.False(
                    rectangles[first].Bounds.Intersects(rectangles[second].Bounds),
                    $"{rectangles[first].Name} overlaps {rectangles[second].Name}.");
            }
        }
    }

    private static void AssertRenderedAtScale(Control control, double scale)
    {
        var pixelSize = new PixelSize(
            (int)Math.Ceiling(control.Bounds.Width * scale),
            (int)Math.Ceiling(control.Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(
            pixelSize,
            new Vector(96 * scale, 96 * scale));

        bitmap.Render(control);

        Assert.Equal(pixelSize, bitmap.PixelSize);
    }
}
