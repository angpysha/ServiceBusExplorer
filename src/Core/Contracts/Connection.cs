#nullable enable
namespace ServiceBusExplorer;

public enum ServiceBusAuthMode { Sas, Windows, AzureActiveDirectory }

public record ConnectionOptions(
    string ConnectionString,
    ServiceBusAuthMode AuthMode = ServiceBusAuthMode.Sas,
    string? TenantId = null,
    string? EntityPath = null);

/// <summary>
/// Contains only the non-secret metadata retained for a previous connection.
/// </summary>
public sealed record ConnectionProfile(
    string Label,
    string NamespaceEndpoint,
    ServiceBusAuthMode AuthMode,
    string? TenantId,
    string? EntityPath)
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
}
