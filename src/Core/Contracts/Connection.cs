#nullable enable
using System.Text.Json.Serialization;

namespace ServiceBusExplorer;

public enum ServiceBusAuthMode { Sas, Windows, AzureActiveDirectory }

public enum ConnectionScope
{
    Namespace,
    Entity,
}

public enum EntraInteractionMode
{
    Default,
    InteractiveBrowser,
}

public enum ConnectionState
{
    Connecting,
    Connected,
    Cancelling,
    Disconnected,
    Faulted,
}

public enum ConnectionFailureCategory
{
    Validation,
    Authentication,
    Authorization,
    Cancellation,
    Throttling,
    ServiceUnavailable,
    Unknown,
}

public record ConnectionOptions(
    string ConnectionString,
    ServiceBusAuthMode AuthMode = ServiceBusAuthMode.Sas,
    string? TenantId = null,
    string? EntityPath = null);

/// <summary>
/// Ephemeral input used to establish one live connection context.
/// </summary>
public sealed record ConnectionRequest
{
    public required string NamespaceEndpoint { get; init; }

    public required ServiceBusAuthMode AuthMode { get; init; }

    public required ConnectionScope Scope { get; init; }

    public string? EntityPath { get; init; }

    public string? TenantId { get; init; }

    public EntraInteractionMode? EntraInteraction { get; init; }

    public SensitiveCredential? SasCredential { get; init; }

    public CredentialReference? CredentialReference { get; init; }

    public string? ProfileId { get; init; }
}

/// <summary>
/// Typed factory outcome without secret material.
/// </summary>
public sealed record ConnectionCreateResult
{
    public bool Success { get; init; }

    public LiveConnectionContext? Context { get; init; }

    public ConnectionFailureCategory? FailureCategory { get; init; }

    public string? FailureMessage { get; init; }

    public bool RequiresManualSas { get; init; }

    public CredentialReference? PreservedCredentialReference { get; init; }

    /// <summary>
    /// Services-layer attachment (for example <c>ConnectionServiceHandles</c>) for composition.
    /// </summary>
    public object? ServiceHandles { get; init; }

    public static ConnectionCreateResult Succeeded(
        LiveConnectionContext context,
        object? serviceHandles = null) =>
        new() { Success = true, Context = context, ServiceHandles = serviceHandles };

    public static ConnectionCreateResult Failed(
        ConnectionFailureCategory category,
        string message) =>
        new()
        {
            Success = false,
            FailureCategory = category,
            FailureMessage = message,
        };

    public static ConnectionCreateResult ManualSasRequired(
        CredentialReference reference,
        string message) =>
        new()
        {
            Success = false,
            FailureCategory = ConnectionFailureCategory.Authentication,
            FailureMessage = message,
            RequiresManualSas = true,
            PreservedCredentialReference = reference,
        };
}

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
