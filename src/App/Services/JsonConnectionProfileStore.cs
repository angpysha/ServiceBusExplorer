#nullable enable
namespace ServiceBusExplorer.App;

/// <summary>
/// JSON-backed profile store that applies vault-before-persist ordering without native adapters.
/// </summary>
public sealed class JsonConnectionProfileStore : IConnectionProfileStore
{
    private readonly SettingsService _settingsService;
    private readonly ICredentialVault _credentialVault;
    private readonly object _gate = new();

    public JsonConnectionProfileStore(SettingsService settingsService, ICredentialVault credentialVault)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _credentialVault = credentialVault ?? throw new ArgumentNullException(nameof(credentialVault));
    }

    public Task<IReadOnlyList<ConnectionProfile>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = _settingsService.Load();
        return Task.FromResult<IReadOnlyList<ConnectionProfile>>(settings.ConnectionHistory.ToList());
    }

    public Task UpsertAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var settings = _settingsService.Load();
            UpsertInPlace(settings, profile);
            _settingsService.Save(settings);
        }

        return Task.CompletedTask;
    }

    public async Task<ProfileCredentialMutationResult> SaveCredentialAsync(
        string profileId,
        SensitiveCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(credential);

        var profile = RequireProfile(profileId);
        if (profile.CredentialReference is not null)
        {
            return new ProfileCredentialMutationResult(
                CredentialVaultStatus.Failure,
                "Use replace when a credential reference already exists.",
                profile);
        }

        var reference = CredentialReference.CreateNew();
        var storeResult = await _credentialVault.StoreAsync(reference, credential, cancellationToken)
            .ConfigureAwait(false);

        if (storeResult.Status != CredentialVaultStatus.Available)
        {
            return new ProfileCredentialMutationResult(
                storeResult.Status,
                storeResult.RecoveryGuidance,
                profile);
        }

        var updated = profile with
        {
            CredentialReference = reference,
            SchemaVersion = ConnectionProfile.CurrentSchemaVersion
        };

        await UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
        return new ProfileCredentialMutationResult(
            CredentialVaultStatus.Available,
            "Credential saved for reconnect.",
            updated);
    }

    public async Task<ProfileCredentialMutationResult> ReplaceCredentialAsync(
        string profileId,
        SensitiveCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(credential);

        var profile = RequireProfile(profileId);
        if (profile.CredentialReference is null)
        {
            return new ProfileCredentialMutationResult(
                CredentialVaultStatus.Failure,
                "No credential reference exists to replace.",
                profile);
        }

        var storeResult = await _credentialVault
            .StoreAsync(profile.CredentialReference, credential, cancellationToken)
            .ConfigureAwait(false);

        // Failure/uncertainty keeps the existing reference and does not claim replacement.
        return new ProfileCredentialMutationResult(
            storeResult.Status,
            storeResult.RecoveryGuidance,
            profile);
    }

    public async Task<CredentialVaultRetrieveResult> RetrieveCredentialAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var profile = RequireProfile(profileId);
        if (profile.CredentialReference is null)
        {
            return new CredentialVaultRetrieveResult(
                CredentialVaultStatus.NotFound,
                "This profile has no saved credential reference.",
                null);
        }

        // Preserve the profile/reference regardless of retrieve outcome.
        return await _credentialVault
            .RetrieveAsync(profile.CredentialReference, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CredentialVaultMutationResult> RemoveAsync(
        string profileId,
        bool deleteVaultItem,
        bool allowMetadataOnlyAfterVaultFailure = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var profile = RequireProfile(profileId);

        if (deleteVaultItem && profile.CredentialReference is not null)
        {
            var deleteResult = await _credentialVault
                .DeleteAsync(profile.CredentialReference, cancellationToken)
                .ConfigureAwait(false);

            if (deleteResult.Status != CredentialVaultStatus.Available)
            {
                if (!allowMetadataOnlyAfterVaultFailure)
                {
                    return deleteResult;
                }
            }
        }

        lock (_gate)
        {
            var settings = _settingsService.Load();
            settings.ConnectionHistory.RemoveAll(item => item.Id == profileId);
            _settingsService.Save(settings);
        }

        return new CredentialVaultMutationResult(
            CredentialVaultStatus.Available,
            deleteVaultItem
                ? "Profile and vault item removed."
                : "Profile metadata removed.");
    }

    private ConnectionProfile RequireProfile(string profileId)
    {
        lock (_gate)
        {
            var settings = _settingsService.Load();
            var profile = settings.ConnectionHistory.FirstOrDefault(item => item.Id == profileId);
            if (profile is null)
                throw new InvalidOperationException($"Connection profile '{profileId}' was not found.");

            return profile;
        }
    }

    private static void UpsertInPlace(AppSettings settings, ConnectionProfile profile)
    {
        var existingIndex = settings.ConnectionHistory.FindIndex(item => item.Id == profile.Id);
        if (existingIndex >= 0)
        {
            settings.ConnectionHistory[existingIndex] = profile;
            return;
        }

        var targetIndex = settings.ConnectionHistory.FindIndex(item =>
            string.Equals(item.NamespaceEndpoint, profile.NamespaceEndpoint, StringComparison.OrdinalIgnoreCase) &&
            item.AuthMode == profile.AuthMode &&
            string.Equals(item.EntityPath, profile.EntityPath, StringComparison.Ordinal));

        if (targetIndex >= 0)
        {
            var existing = settings.ConnectionHistory[targetIndex];
            settings.ConnectionHistory[targetIndex] = profile with
            {
                Id = existing.Id,
                CredentialReference = profile.CredentialReference ?? existing.CredentialReference
            };
            return;
        }

        settings.ConnectionHistory.Insert(0, profile);
        if (settings.ConnectionHistory.Count > 10)
            settings.ConnectionHistory = settings.ConnectionHistory.Take(10).ToList();
    }
}
