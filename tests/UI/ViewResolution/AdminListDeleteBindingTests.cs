using Xunit;

namespace ServiceBusExplorer.UITests.ViewResolution;

/// <summary>
/// Guards against Avalonia runtime crashes from xmlns-prefixed type casts inside
/// deferred ListBox DataTemplates (XamlIlRuntimeHelpers.XamlTypeResolver).
/// Prefer ListBox.Tag = DeleteCommand + Command="{Binding $parent[ListBox].Tag}".
/// </summary>
public class AdminListDeleteBindingTests
{
    public static TheoryData<string> AdminListViews => new()
    {
        Path.Combine("Queues", "QueueListView.axaml"),
        Path.Combine("Topics", "TopicListView.axaml"),
        Path.Combine("Subscriptions", "RuleListView.axaml"),
    };

    [Theory]
    [MemberData(nameof(AdminListViews))]
    public void AdminListView_DoesNotUseVmCastOnParentDataContext(string relativeViewPath)
    {
        var xaml = File.ReadAllText(GetRepositoryFile("src", "App", "Views", relativeViewPath));

        // Deferred DataTemplates crash on xmlns-prefixed casts in binding paths.
        Assert.DoesNotContain("((vm:", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DataContext).DeleteCommand}", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"{Binding DeleteCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding $parent[ListBox].Tag}\"", xaml, StringComparison.Ordinal);
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
