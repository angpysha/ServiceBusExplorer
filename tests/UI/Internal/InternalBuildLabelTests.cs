using Xunit;

namespace ServiceBusExplorer.UITests.Internal;

public class InternalBuildLabelTests
{
    [Fact]
    public void MainWindow_ShowsRevisionedInternalBuildLabelAndLimitations()
    {
        var xaml = File.ReadAllText(GetMainWindowPath());

        Assert.Contains("Internal development build", xaml);
        Assert.Contains("BuildRevision", xaml);
        Assert.Contains("InternalLimitations", xaml);
        Assert.DoesNotContain("plaintext", xaml, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMainWindowPath() =>
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
                "MainWindow.axaml"));
}
