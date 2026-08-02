using System.Xml.Linq;
using Xunit;

namespace ServiceBusExplorer.UITests.ViewResolution;

public class SendMessageViewTemplateTests
{
    public static TheoryData<string, string, string, string, string, string, string> EditableFields => new()
    {
        { "TextBox", "Text", "Body", "Body", "True", "Body *", "Required. UTF-8 text" },
        { "TextBox", "Text", "ContentType", "Content type", "False", "Content type (optional)", "Optional. MIME type" },
        { "TextBox", "Text", "MessageId", "Message ID", "False", "Message ID (optional)", "Optional. Application-defined identifier" },
        { "TextBox", "Text", "CorrelationId", "Correlation ID", "False", "Correlation ID (optional)", "Optional. Associates related messages" },
        { "TextBox", "Text", "SessionId", "Session ID", "False", "Session ID (conditionally required)", "Conditionally required for session-enabled" },
        { "TextBox", "Text", "To", "To", "False", "To (optional)", "Optional logical destination metadata" },
        { "TextBox", "Text", "PropertiesJson", "Application properties", "False", "Application properties (optional JSON)", "Optional. JSON object" },
        { "NumericUpDown", "Value", "SendCount", "Message count", "True", "Message count *", "Required. Number of identical" },
        { "CheckBox", "IsChecked", "UseScheduledTime", "Schedule message", "False", "Schedule message (optional)", "Optional. Enable to enqueue" },
        { "DurationEditor", "Value", "ScheduleDelay", "Schedule delay", "{Binding UseScheduledTime}", "Schedule delay * (when scheduling)", "Required when scheduling. Delay from now" }
    };

    [Fact]
    public void App_RegistersSendMessageViewModelTemplate()
    {
        var xaml = File.ReadAllText(GetRepositoryFile("src", "App", "App.axaml"));

        Assert.Contains("DataType=\"{x:Type vm:SendMessageViewModel}\"", xaml);
        Assert.Contains("<queues:SendMessageView", xaml);
    }

    [Fact]
    public void SendView_ExposesActualDestinationAndOutcome()
    {
        var xaml = File.ReadAllText(
            GetRepositoryFile("src", "App", "Views", "Queues", "SendMessageView.axaml"));

        Assert.Contains("DestinationDescription", xaml);
        Assert.Contains("AutomationProperties.Name=\"Actual publish destination\"", xaml);
        Assert.Contains("Outcome", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Assertive\"", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
    }

    [Theory]
    [MemberData(nameof(EditableFields))]
    public void SendView_ExplainsEveryEditableFieldAndRequiredState(
        string elementName,
        string bindingAttribute,
        string binding,
        string automationName,
        string requiredState,
        string visibleLabel,
        string guidanceFragment)
    {
        var document = XDocument.Load(
            GetRepositoryFile("src", "App", "Views", "Queues", "SendMessageView.axaml"));
        var input = document
            .Descendants()
            .Single(
                element =>
                    element.Name.LocalName == elementName
                    && element.Attribute(bindingAttribute)?.Value == $"{{Binding {binding}}}");

        Assert.Equal(automationName, input.Attribute("AutomationProperties.Name")?.Value);
        Assert.Equal(requiredState, input.Attribute("AutomationProperties.IsRequiredForForm")?.Value);
        Assert.Contains(guidanceFragment, input.Attribute("AutomationProperties.HelpText")?.Value);
        Assert.Contains(
            document.Descendants(),
            element =>
                (element.Attribute("Text")?.Value == visibleLabel
                 || element.Attribute("Content")?.Value == visibleLabel));
        Assert.Contains(
            document.Descendants(),
            element =>
                element.Name.LocalName == "TextBlock"
                && element.Attribute("Text")?.Value.Contains(guidanceFragment, StringComparison.Ordinal) == true);
    }

    [Fact]
    public void SendView_TruthfullyListsDeferredMessageProperties()
    {
        var xaml = File.ReadAllText(
            GetRepositoryFile("src", "App", "Views", "Queues", "SendMessageView.axaml"));

        Assert.Contains("Subject/label", xaml);
        Assert.Contains("partition key", xaml);
        Assert.Contains("reply fields", xaml);
        Assert.Contains("message TTL", xaml);
    }

    private static string GetRepositoryFile(params string[] segments)
    {
        var pathSegments = new[]
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".."
        }.Concat(segments).ToArray();

        return Path.GetFullPath(Path.Combine(pathSegments));
    }
}
