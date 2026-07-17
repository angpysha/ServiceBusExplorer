using System.Xml.Linq;
using Xunit;

namespace ServiceBusExplorer.UITests.Inventory;

public class VisibleDurationFieldInventoryTests
{
    public static TheoryData<string, string, string> VisibleFields => new()
    {
        { "Queues/QueueDetailView.axaml", "LockDuration", "LockDuration" },
        { "Queues/QueueDetailView.axaml", "DefaultMessageTimeToLive", "DefaultMessageTimeToLive" },
        { "Queues/QueueDetailView.axaml", "AutoDeleteOnIdle", "AutoDeleteOnIdle" },
        { "Topics/TopicDetailView.axaml", "DefaultMessageTimeToLive", "DefaultMessageTimeToLive" },
        { "Topics/TopicDetailView.axaml", "AutoDeleteOnIdle", "AutoDeleteOnIdle" },
        { "Subscriptions/SubscriptionDetailView.axaml", "LockDuration", "LockDuration" },
        { "Subscriptions/SubscriptionDetailView.axaml", "DefaultMessageTimeToLive", "DefaultMessageTimeToLive" },
        { "Subscriptions/SubscriptionDetailView.axaml", "AutoDeleteOnIdle", "AutoDeleteOnIdle" },
        { "Queues/SendMessageView.axaml", "ScheduleDelay", "ScheduledEnqueueDelay" }
    };

    public static TheoryData<string, string, int, string, string, string, string, string> NumericFields => new()
    {
        { "Queues/QueueDetailView.axaml", "MaxDeliveryCount", 1, "1", "2000", "1", "Max delivery count", "Max delivery count" },
        { "Queues/QueueDetailView.axaml", "PeekCount", 2, "1", "1000", "1", "Messages to peek", "Messages to peek" },
        { "Subscriptions/SubscriptionDetailView.axaml", "MaxDeliveryCount", 1, "1", "2000", "1", "Max delivery count", "Max delivery count" },
        { "Subscriptions/SubscriptionDetailView.axaml", "PeekCount", 2, "1", "1000", "1", "Messages to peek", "Messages to peek" },
        { "Topics/TopicDetailView.axaml", "MaxSizeInMegabytes", 1, "1", "81920", "1", "Maximum topic size in megabytes", "Maximum topic size (MB)" },
        { "Queues/SendMessageView.axaml", "SendCount", 1, "1", "1000", "1", "Message count", "Message count" }
    };

    [Theory]
    [MemberData(nameof(VisibleFields))]
    public void VisibleDurationField_UsesDurationEditorAndNamedConstraint(
        string relativePath,
        string binding,
        string constraint)
    {
        var document = XDocument.Load(GetViewFile(relativePath));
        var editor = document
            .Descendants()
            .Single(
                element =>
                    element.Name.LocalName == "DurationEditor"
                    && element.Attribute("Value")?.Value == $"{{Binding {binding}}}");

        Assert.Equal(
            $"{{x:Static domain:DurationConstraint.{constraint}}}",
            editor.Attribute("Constraint")?.Value);
    }

    [Fact]
    public void ModernViews_ContainExactlyTheInventoriedFieldsAndNoRetiredControl()
    {
        var viewRoot = GetViewFile();
        var xaml = Directory
            .EnumerateFiles(viewRoot, "*.axaml", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.DoesNotContain(xaml, text => text.Contains("TimeSpanControl", StringComparison.Ordinal));
        Assert.Equal(
            VisibleFields.Count,
            xaml.Sum(text => CountOccurrences(text, "<ctrl:DurationEditor")));
        Assert.Equal(
            8,
            xaml.Sum(text => CountOccurrences(text, "<NumericUpDown")));

        var durationEditorCode = File.ReadAllText(
            Path.Combine(viewRoot, "Controls", "DurationEditor.axaml.cs"));
        Assert.DoesNotContain("class TimeSpanControl", durationEditorCode, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(NumericFields))]
    public void TrueNumericField_UsesSharedPatternAndExplicitSemantics(
        string relativePath,
        string binding,
        int expectedCount,
        string minimum,
        string maximum,
        string increment,
        string automationName,
        string visibleLabel)
    {
        var document = XDocument.Load(GetViewFile(relativePath));
        var inputs = document
            .Descendants()
            .Where(
                element =>
                    element.Name.LocalName == "NumericUpDown"
                    && element.Attribute("Value")?.Value == $"{{Binding {binding}}}")
            .ToArray();

        Assert.Equal(expectedCount, inputs.Length);
        Assert.Equal(
            expectedCount,
            document
                .Descendants()
                .Count(
                    element =>
                        element.Name.LocalName == "TextBlock"
                        && element.Attribute("Text")?.Value == visibleLabel));
        foreach (var input in inputs)
        {
            Assert.Equal(minimum, input.Attribute("Minimum")?.Value);
            Assert.Equal(maximum, input.Attribute("Maximum")?.Value);
            Assert.Equal(increment, input.Attribute("Increment")?.Value);
            Assert.Equal(automationName, input.Attribute("AutomationProperties.Name")?.Value);
            Assert.False(string.IsNullOrWhiteSpace(input.Attribute("AutomationProperties.HelpText")?.Value));
        }
    }

    [Fact]
    public void SharedNumericStyle_PreservesDigitsAndVisibleStepperControls()
    {
        var document = XDocument.Load(GetAppFile());
        var style = document
            .Descendants()
            .Single(
                element =>
                    element.Name.LocalName == "Style"
                    && element.Attribute("Selector")?.Value == "NumericUpDown");
        var setters = style
            .Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => element.Attribute("Property")!.Value,
                element => element.Attribute("Value")!.Value);

        Assert.Equal("144", setters["MinWidth"]);
        Assert.Equal("True", setters["ShowButtonSpinner"]);
        Assert.Equal("True", setters["AllowSpin"]);
        Assert.Equal("0", setters["FormatString"]);
    }

    [Fact]
    public void PlusActions_AreNamedActionsRatherThanUnclassifiedSteppers()
    {
        var buttons = Directory
            .EnumerateFiles(GetViewFile(), "*.axaml", SearchOption.AllDirectories)
            .Select(XDocument.Load)
            .SelectMany(document => document.Descendants())
            .Where(
                element =>
                    element.Name.LocalName == "Button"
                    && element.Attribute("Content")?.Value.StartsWith("+", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(buttons);
        foreach (var button in buttons)
        {
            Assert.False(string.IsNullOrWhiteSpace(button.Attribute("AutomationProperties.Name")?.Value));
            Assert.False(string.IsNullOrWhiteSpace(button.Attribute("AutomationProperties.HelpText")?.Value));
        }
    }

    [Fact]
    public void NamedConstraints_MatchAzureServiceBusLimits()
    {
        Assert.Equal("LockDuration", DurationConstraint.LockDuration.PropertyName);
        Assert.Equal(TimeSpan.FromSeconds(5), DurationConstraint.LockDuration.Minimum?.ToTimeSpan());
        Assert.Equal(TimeSpan.FromMinutes(5), DurationConstraint.LockDuration.Maximum?.ToTimeSpan());

        Assert.Equal(
            TimeSpan.FromSeconds(1),
            DurationConstraint.DefaultMessageTimeToLive.Minimum?.ToTimeSpan());
        Assert.Null(DurationConstraint.DefaultMessageTimeToLive.Maximum);

        Assert.Equal(
            TimeSpan.FromMinutes(5),
            DurationConstraint.AutoDeleteOnIdle.Minimum?.ToTimeSpan());
        Assert.Null(DurationConstraint.AutoDeleteOnIdle.Maximum);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string GetViewFile(string relativePath = "") =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "App",
                "Views",
                relativePath));

    private static string GetAppFile() =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "App",
                "App.axaml"));
}
