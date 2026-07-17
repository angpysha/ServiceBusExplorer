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
        { "Subscriptions/SubscriptionDetailView.axaml", "AutoDeleteOnIdle", "AutoDeleteOnIdle" }
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
}
