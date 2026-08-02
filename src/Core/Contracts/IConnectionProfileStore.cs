#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Persists secret-free connection profiles with versioned allowlisted serialization.
/// </summary>
public interface IConnectionProfileStore
{
    Task<IReadOnlyList<ConnectionProfile>> ListAsync(
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default);

    Task<ProfileCredentialMutationResult> SaveCredentialAsync(
        string profileId,
        SensitiveCredential credential,
        CancellationToken cancellationToken = default);

    Task<ProfileCredentialMutationResult> ReplaceCredentialAsync(
        string profileId,
        SensitiveCredential credential,
        CancellationToken cancellationToken = default);

    Task<CredentialVaultRetrieveResult> RetrieveCredentialAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task<CredentialVaultMutationResult> RemoveAsync(
        string profileId,
        bool deleteVaultItem,
        bool allowMetadataOnlyAfterVaultFailure = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Mutation outcome that also returns the resulting profile snapshot.
/// </summary>
public sealed record ProfileCredentialMutationResult(
    CredentialVaultStatus Status,
    string RecoveryGuidance,
    ConnectionProfile Profile);
