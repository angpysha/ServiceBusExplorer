#nullable enable
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ServiceBusExplorer.App;
using Xunit;

namespace ServiceBusExplorer.UnitTests.Connections;

public sealed class ConnectionProfileAndVaultContractTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"sbe-vault-{Guid.NewGuid():N}");

    [Fact]
    public void CredentialReference_CreateNew_IsOpaqueCsprngValue()
    {
        var first = CredentialReference.CreateNew();
        var second = CredentialReference.CreateNew();

        Assert.NotEqual(first, second);
        Assert.True(CredentialReference.IsOpaque(first.Value));
        Assert.DoesNotContain("SharedAccessKey", first.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("servicebus", first.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SensitiveCredential_RedactsToStringAndOmitsSecretFromJson()
    {
        using var credential = new SensitiveCredential(CreateConnectionString());

        Assert.Equal("[redacted]", credential.ToString());
        var json = JsonSerializer.Serialize(new { Credential = credential });
        Assert.DoesNotContain("test-only-secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SharedAccessKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CreateConnectionString(), credential.Reveal());
    }

    [Fact]
    public async Task FakeVault_StoreRetrieveReplaceDelete_Succeeds()
    {
        var vault = new FakeCredentialVault();
        var reference = CredentialReference.CreateNew();
        using var original = new SensitiveCredential(CreateConnectionString());
        using var replacement = new SensitiveCredential(CreateConnectionString("replacement-secret"));

        var store = await vault.StoreAsync(reference, original);
        Assert.Equal(CredentialVaultStatus.Available, store.Status);

        var retrieved = await vault.RetrieveAsync(reference);
        Assert.Equal(CredentialVaultStatus.Available, retrieved.Status);
        Assert.NotNull(retrieved.Credential);
        Assert.Equal(CreateConnectionString(), retrieved.Credential!.Reveal());
        retrieved.Credential.Dispose();

        var replaced = await vault.StoreAsync(reference, replacement);
        Assert.Equal(CredentialVaultStatus.Available, replaced.Status);

        var afterReplace = await vault.RetrieveAsync(reference);
        Assert.Equal(CreateConnectionString("replacement-secret"), afterReplace.Credential!.Reveal());
        afterReplace.Credential.Dispose();

        var deleted = await vault.DeleteAsync(reference);
        Assert.Equal(CredentialVaultStatus.Available, deleted.Status);

        var missing = await vault.RetrieveAsync(reference);
        Assert.Equal(CredentialVaultStatus.NotFound, missing.Status);
        Assert.Null(missing.Credential);
    }

    [Fact]
    public async Task ProfileStore_SaveCredential_PersistsReferenceOnlyAfterVaultSuccess()
    {
        var path = CreateSettingsPath();
        var vault = new FakeCredentialVault();
        var store = new JsonConnectionProfileStore(new SettingsService(path), vault);
        var profile = CreateProfile();

        await store.UpsertAsync(profile);
        using var credential = new SensitiveCredential(CreateConnectionString());

        var saved = await store.SaveCredentialAsync(profile.Id, credential);

        Assert.Equal(CredentialVaultStatus.Available, saved.Status);
        Assert.NotNull(saved.Profile.CredentialReference);
        Assert.DoesNotContain("test-only-secret", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(saved.Profile.CredentialReference!.Value, File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Single(vault.Entries);
    }

    [Fact]
    public async Task ProfileStore_SaveCredential_VaultFailure_LeavesProfileWithoutReference()
    {
        var path = CreateSettingsPath();
        var vault = new FakeCredentialVault { NextStoreStatus = CredentialVaultStatus.Failure };
        var store = new JsonConnectionProfileStore(new SettingsService(path), vault);
        var profile = CreateProfile();
        await store.UpsertAsync(profile);
        using var credential = new SensitiveCredential(CreateConnectionString());

        var saved = await store.SaveCredentialAsync(profile.Id, credential);

        Assert.Equal(CredentialVaultStatus.Failure, saved.Status);
        var listed = await store.ListAsync();
        Assert.Null(Assert.Single(listed).CredentialReference);
        Assert.DoesNotContain("credentialReference", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vault.Entries);
    }

    [Fact]
    public async Task ProfileStore_ReplaceCredential_FailureKeepsExistingReference()
    {
        var path = CreateSettingsPath();
        var vault = new FakeCredentialVault();
        var store = new JsonConnectionProfileStore(new SettingsService(path), vault);
        var profile = CreateProfile();
        await store.UpsertAsync(profile);
        using var original = new SensitiveCredential(CreateConnectionString());
        var saved = await store.SaveCredentialAsync(profile.Id, original);
        var reference = saved.Profile.CredentialReference!;

        vault.NextStoreStatus = CredentialVaultStatus.Uncertain;
        using var replacement = new SensitiveCredential(CreateConnectionString("replacement-secret"));
        var replaced = await store.ReplaceCredentialAsync(profile.Id, replacement);

        Assert.Equal(CredentialVaultStatus.Uncertain, replaced.Status);
        var listed = Assert.Single(await store.ListAsync());
        Assert.Equal(reference, listed.CredentialReference);
        Assert.Equal(CreateConnectionString(), vault.Entries[reference.Value].Reveal());
    }

    [Fact]
    public async Task ProfileStore_RemoveWithCleanup_VaultFailure_RetainsProfileAndReference()
    {
        var path = CreateSettingsPath();
        var vault = new FakeCredentialVault();
        var store = new JsonConnectionProfileStore(new SettingsService(path), vault);
        var profile = CreateProfile();
        await store.UpsertAsync(profile);
        using var credential = new SensitiveCredential(CreateConnectionString());
        var saved = await store.SaveCredentialAsync(profile.Id, credential);

        vault.NextDeleteStatus = CredentialVaultStatus.Locked;
        var removed = await store.RemoveAsync(
            profile.Id,
            deleteVaultItem: true,
            allowMetadataOnlyAfterVaultFailure: false);

        Assert.Equal(CredentialVaultStatus.Locked, removed.Status);
        var listed = Assert.Single(await store.ListAsync());
        Assert.Equal(saved.Profile.CredentialReference, listed.CredentialReference);
        Assert.Single(vault.Entries);
    }

    [Fact]
    public async Task ProfileStore_RetrieveCredential_NotFound_PreservesReference()
    {
        var path = CreateSettingsPath();
        var vault = new FakeCredentialVault();
        var store = new JsonConnectionProfileStore(new SettingsService(path), vault);
        var reference = CredentialReference.CreateNew();
        var profile = CreateProfile() with
        {
            CredentialReference = reference,
            SchemaVersion = ConnectionProfile.CurrentSchemaVersion
        };
        await store.UpsertAsync(profile);

        var retrieved = await store.RetrieveCredentialAsync(profile.Id);

        Assert.Equal(CredentialVaultStatus.NotFound, retrieved.Status);
        Assert.Null(retrieved.Credential);
        Assert.Equal(reference, Assert.Single(await store.ListAsync()).CredentialReference);
    }

    [Fact]
    public void SettingsService_RoundTripsOptionalOpaqueCredentialReference()
    {
        var path = CreateSettingsPath();
        var reference = CredentialReference.CreateNew();
        var settings = new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            ConnectionHistory =
            [
                CreateProfile() with
                {
                    CredentialReference = reference,
                    SchemaVersion = ConnectionProfile.CurrentSchemaVersion
                }
            ]
        };

        new SettingsService(path).Save(settings);
        var loaded = new SettingsService(path).Load();

        var profile = Assert.Single(loaded.ConnectionHistory);
        Assert.Equal(reference, profile.CredentialReference);
        Assert.DoesNotContain("test-only-secret", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
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

    private static ConnectionProfile CreateProfile() =>
        new(
            "example",
            "sb://example.servicebus.windows.net/",
            ServiceBusAuthMode.Sas,
            null,
            null)
        {
            Id = ConnectionProfile.CreateProfileId(),
            SchemaVersion = ConnectionProfile.CurrentSchemaVersion
        };

    private static string CreateConnectionString(string secret = "test-only-secret")
    {
        const string keyField = "SharedAccess" + "Key";
        return $"Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=test;{keyField}={secret}";
    }

    private sealed class FakeCredentialVault : ICredentialVault
    {
        public Dictionary<string, SensitiveCredential> Entries { get; } = new(StringComparer.Ordinal);
        public CredentialVaultStatus NextStoreStatus { get; set; } = CredentialVaultStatus.Available;
        public CredentialVaultStatus NextDeleteStatus { get; set; } = CredentialVaultStatus.Available;
        public CredentialVaultStatus AvailabilityStatus { get; set; } = CredentialVaultStatus.Available;

        public Task<CredentialVaultAvailabilityResult> GetAvailabilityAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CredentialVaultAvailabilityResult(
                AvailabilityStatus,
                AvailabilityStatus == CredentialVaultStatus.Available
                    ? "Native vault is available."
                    : "Native vault is unavailable."));

        public Task<CredentialVaultMutationResult> StoreAsync(
            CredentialReference reference,
            SensitiveCredential credential,
            CancellationToken cancellationToken = default)
        {
            if (NextStoreStatus != CredentialVaultStatus.Available)
            {
                return Task.FromResult(new CredentialVaultMutationResult(
                    NextStoreStatus,
                    "Store failed."));
            }

            Entries[reference.Value] = new SensitiveCredential(credential.Reveal());
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Available,
                "Stored."));
        }

        public Task<CredentialVaultRetrieveResult> RetrieveAsync(
            CredentialReference reference,
            CancellationToken cancellationToken = default)
        {
            if (!Entries.TryGetValue(reference.Value, out var stored))
            {
                return Task.FromResult(new CredentialVaultRetrieveResult(
                    CredentialVaultStatus.NotFound,
                    "Credential was not found.",
                    null));
            }

            return Task.FromResult(new CredentialVaultRetrieveResult(
                CredentialVaultStatus.Available,
                "Retrieved.",
                new SensitiveCredential(stored.Reveal())));
        }

        public Task<CredentialVaultMutationResult> DeleteAsync(
            CredentialReference reference,
            CancellationToken cancellationToken = default)
        {
            if (NextDeleteStatus != CredentialVaultStatus.Available)
            {
                return Task.FromResult(new CredentialVaultMutationResult(
                    NextDeleteStatus,
                    "Delete failed."));
            }

            Entries.Remove(reference.Value);
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Available,
                "Deleted."));
        }
    }
}
