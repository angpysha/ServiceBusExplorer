#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Framework-neutral asynchronous port for the designated native credential vault.
/// Production composition registers a platform adapter only after the native-vault gate.
/// </summary>
public interface ICredentialVault
{
    Task<CredentialVaultAvailabilityResult> GetAvailabilityAsync(
        CancellationToken cancellationToken = default);

    Task<CredentialVaultMutationResult> StoreAsync(
        CredentialReference reference,
        SensitiveCredential credential,
        CancellationToken cancellationToken = default);

    Task<CredentialVaultRetrieveResult> RetrieveAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default);

    Task<CredentialVaultMutationResult> DeleteAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default);
}
