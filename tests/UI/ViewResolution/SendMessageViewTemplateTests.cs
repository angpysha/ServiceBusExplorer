using Xunit;

namespace ServiceBusExplorer.UITests.ViewResolution;

public class SendMessageViewTemplateTests
{
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
