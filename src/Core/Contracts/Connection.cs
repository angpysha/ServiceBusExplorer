#nullable enable
using System.Text.Json.Serialization;

namespace ServiceBusExplorer;

public enum ServiceBusAuthMode { Sas, Windows, AzureActiveDirectory }

public record ConnectionOptions(
    string ConnectionString,
    ServiceBusAuthMode AuthMode = ServiceBusAuthMode.Sas,
    string? TenantId = null,
    string? EntityPath = null);

/// <summary>
/// Contains only the non-secret metadata retained for a previous connection.
/// Optional <see cref="CredentialReference"/> is introduced by the native-vault milestone.
/// </summary>
public sealed record ConnectionProfile(
    string Label,
    string NamespaceEndpoint,
    ServiceBusAuthMode AuthMode,
    string? TenantId,
    string? EntityPath)
{
    public const int MinimumSupportedSchemaVersion = 1;
    public const int CurrentSchemaVersion = 2;

    public string Id { get; init; } = CreateProfileId();

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CredentialReference? CredentialReference { get; init; }

    public static string CreateProfileId() => Guid.NewGuid().ToString("N");
}
