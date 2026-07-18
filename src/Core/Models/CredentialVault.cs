#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Closed set of native-vault availability and operation outcomes.
/// </summary>
public enum CredentialVaultStatus
{
    Available,
    Unavailable,
    Locked,
    PermissionDenied,
    ProviderMissing,
    Unsupported,
    NotFound,
    Uncertain,
    Failure,
    Cancelled
}

/// <summary>
/// Transient non-serializable SAS credential wrapper. Never persists or prints the secret.
/// </summary>
public sealed class SensitiveCredential : IDisposable
{
    private string? _value;

    public SensitiveCredential(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A sensitive credential value is required.", nameof(value));

        _value = value;
    }

    public string Reveal() =>
        _value ?? throw new ObjectDisposedException(nameof(SensitiveCredential));

    public override string ToString() => "[redacted]";

    public void Dispose() => _value = null;
}

/// <summary>
/// Availability probe result for the designated native vault.
/// </summary>
public sealed record CredentialVaultAvailabilityResult(
    CredentialVaultStatus Status,
    string RecoveryGuidance);

/// <summary>
/// Store or delete outcome without secret material.
/// </summary>
public sealed record CredentialVaultMutationResult(
    CredentialVaultStatus Status,
    string RecoveryGuidance);

/// <summary>
/// Retrieve outcome. A secret wrapper is present only on success.
/// </summary>
public sealed record CredentialVaultRetrieveResult(
    CredentialVaultStatus Status,
    string RecoveryGuidance,
    SensitiveCredential? Credential);
