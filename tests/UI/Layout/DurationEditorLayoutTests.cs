using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
            AssertInside(control, popup, scale);

        AssertNoOverlap(inputs.Cast<Control>().Append(apply).Append(cancel), popup, scale);

        inputs[1].Text = "24";
        Dispatcher.UIThread.RunJobs();
        var error = Assert.IsType<TextBlock>(editor.FindControl<TextBlock>("HoursError"));
        Assert.False(string.IsNullOrWhiteSpace(error.Text));
        AssertInside(error, popup, scale);

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

    private static void AssertInside(Control control, TopLevel topLevel, double scale)
    {
        var origin = control.TranslatePoint(default, topLevel);
        Assert.True(origin.HasValue, $"{control.Name} could not be located in its top level.");

        var bounds = ToScaledRect(new Rect(origin.Value, control.Bounds.Size), scale);
        var viewport = ToScaledRect(new Rect(topLevel.ClientSize), scale);
        Assert.True(
            viewport.Contains(bounds),
            $"{control.Name} bounds {bounds} exceed viewport {viewport} at {scale:P0}.");
    }

    private static void AssertNoOverlap(
        IEnumerable<Control> controls,
        TopLevel topLevel,
        double scale)
    {
        var rectangles = controls
            .Select(
                control =>
                {
                    var origin = control.TranslatePoint(default, topLevel)
                        ?? throw new InvalidOperationException($"{control.Name} has no top-level position.");
                    return (control.Name, Bounds: ToScaledRect(new Rect(origin, control.Bounds.Size), scale));
                })
            .ToArray();

        for (var first = 0; first < rectangles.Length; first++)
        {
            for (var second = first + 1; second < rectangles.Length; second++)
            {
                Assert.False(
                    rectangles[first].Bounds.Intersects(rectangles[second].Bounds),
                    $"{rectangles[first].Name} overlaps {rectangles[second].Name} at {scale:P0}.");
            }
        }
    }

    private static Rect ToScaledRect(Rect bounds, double scale)
    {
        var left = Math.Floor(bounds.Left * scale);
        var top = Math.Floor(bounds.Top * scale);
        var right = Math.Ceiling(bounds.Right * scale);
        var bottom = Math.Ceiling(bounds.Bottom * scale);
        return new Rect(left, top, right - left, bottom - top);
    }
}
