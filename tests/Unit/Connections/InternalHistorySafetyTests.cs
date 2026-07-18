using System.Text.Json;
using ServiceBusExplorer.App;
using ServiceBusExplorer.ViewModels;
using Xunit;

namespace ServiceBusExplorer.UnitTests.Connections;

public sealed class InternalHistorySafetyTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"sbe-history-{Guid.NewGuid():N}");

    [Fact]
    public void Load_LegacyRawHistory_RemovesSecretAndRewritesSafeSchema()
    {
        var path = CreateSettingsPath();
        File.WriteAllText(path, CreateLegacySettingsJson());

        var settings = new SettingsService(path).Load();

        Assert.Empty(settings.ConnectionHistory);
        var persisted = File.ReadAllText(path);
        Assert.DoesNotContain("test-only-secret", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SharedAccessKey", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Fact]
    public void RecordConnection_PersistsOnlyAllowlistedMetadata()
    {
        var path = CreateSettingsPath();
        var service = new SettingsService(path);

        var settings = service.RecordConnection(new ConnectionOptions(
            CreateConnectionString(),
            ServiceBusAuthMode.Sas,
            TenantId: "tenant-id",
            EntityPath: "orders"));

        var profile = Assert.Single(settings.ConnectionHistory);
        Assert.Equal("example", profile.Label);
        Assert.Equal("sb://example.servicebus.windows.net/", profile.NamespaceEndpoint);
        Assert.Equal(ServiceBusAuthMode.Sas, profile.AuthMode);
        Assert.Equal("tenant-id", profile.TenantId);
        Assert.Equal("orders", profile.EntityPath);

        var persisted = File.ReadAllText(path);
        Assert.DoesNotContain("test-only-secret", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SharedAccessKey", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credentialReference", persisted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConnectionProfile_AllowsOptionalOpaqueReferenceButNoSecretMembers()
    {
        var properties = typeof(ConnectionProfile).GetProperties();
        var propertyNames = properties.Select(property => property.Name).ToArray();

        Assert.Contains("CredentialReference", propertyNames);
        Assert.Equal(typeof(CredentialReference), properties.Single(p => p.Name == "CredentialReference").PropertyType);
        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyProfile_RestoresMetadataButRequiresFullSasReentry()
    {
        var viewModel = new ConnectViewModel
        {
            ConnectionString = CreateConnectionString()
        };
        var profile = new ConnectionProfile(
            "example",
            "sb://example.servicebus.windows.net/",
            ServiceBusAuthMode.Sas,
            "tenant-id",
            "orders");

        viewModel.ApplyProfile(profile);

        Assert.Equal(string.Empty, viewModel.ConnectionString);
        Assert.Equal(ServiceBusAuthMode.Sas, viewModel.AuthMode);
        Assert.Equal("tenant-id", viewModel.TenantId);
        Assert.Equal("orders", viewModel.EntityPath);
    }

    [Fact]
    public void SerializedSettings_ContainsOnlyVersionedProfileShape()
    {
        var settings = new AppSettings
        {
            ConnectionHistory =
            [
                new ConnectionProfile(
                    "example",
                    "sb://example.servicebus.windows.net/",
                    ServiceBusAuthMode.Sas,
                    null,
                    null)
            ]
        };

        var json = JsonSerializer.Serialize(settings);

        Assert.Contains("SchemaVersion", json);
        Assert.DoesNotContain("CredentialReference", json);
        Assert.DoesNotContain("ConnectionString", json);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string CreateSettingsPath()
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, "settings.json");
    }

    private static string CreateConnectionString()
    {
        const string keyField = "SharedAccess" + "Key";
        return $"Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=test;{keyField}=test-only-secret";
    }

    private static string CreateLegacySettingsJson() =>
        $$"""
        {
          "connectionHistory": [
            "{{CreateConnectionString()}}"
          ],
          "defaultPeekCount": 20,
          "theme": "Light"
        }
        """;
}
