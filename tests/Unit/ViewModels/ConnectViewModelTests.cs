#nullable enable
using ServiceBusExplorer.App;
using ServiceBusExplorer.ViewModels;
using Xunit;

namespace ServiceBusExplorer.UnitTests.ViewModels;

public sealed class ConnectViewModelTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"sbe-connect-vm-{Guid.NewGuid():N}");

    [Fact]
    public void SaveSasToVault_DefaultsFalse_OnNewViewModel()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.SaveSasToVault);
    }

    [Fact]
    public void ApplyProfile_ResetsSaveSasToVault_EvenWhenProfileHasCredentialReference()
    {
        var viewModel = CreateViewModel();
        viewModel.SaveSasToVault = true;
        var profile = CreateProfile() with
        {
            CredentialReference = CredentialReference.CreateNew(),
            SchemaVersion = ConnectionProfile.CurrentSchemaVersion
        };

        viewModel.ApplyProfile(profile);

        Assert.False(viewModel.SaveSasToVault);
        Assert.Equal(profile.CredentialReference, viewModel.ActiveCredentialReference);
    }

    [Fact]
    public async Task EntraAuthMode_NeverInvokesVault()
    {
        var vault = new CountingCredentialVault();
        var store = CreateProfileStore(vault);
        var viewModel = CreateViewModel(vault, store);
        viewModel.AuthMode = ServiceBusAuthMode.AzureActiveDirectory;
        var profile = CreateProfile() with
        {
            AuthMode = ServiceBusAuthMode.AzureActiveDirectory,
            CredentialReference = CredentialReference.CreateNew(),
            SchemaVersion = ConnectionProfile.CurrentSchemaVersion
        };
        await store.UpsertAsync(profile);

        await viewModel.InitializeAsync();
        viewModel.ApplyProfile(profile);
        await viewModel.ApplyProfileAsync(profile);
        _ = viewModel.BuildConnectionRequest();

        Assert.Equal(1, vault.AvailabilityCallCount);
        Assert.Equal(0, vault.RetrieveCallCount);
        Assert.Equal(0, vault.StoreCallCount);
        Assert.Equal(0, vault.DeleteCallCount);
    }

    [Fact]
    public async Task ApplyProfileAsync_RetrieveFailure_PreservesReferenceAndPromptsForSas()
    {
        var vault = new CountingCredentialVault();
        var store = CreateProfileStore(vault);
        var reference = CredentialReference.CreateNew();
        var profile = CreateProfile() with
        {
            CredentialReference = reference,
            SchemaVersion = ConnectionProfile.CurrentSchemaVersion
        };
        await store.UpsertAsync(profile);

        var viewModel = CreateViewModel(vault, store);
        await viewModel.ApplyProfileAsync(profile);

        Assert.True(viewModel.RequiresManualSas);
        Assert.Equal(reference, viewModel.ActiveCredentialReference);
        Assert.Equal(string.Empty, viewModel.ConnectionString);
        Assert.Contains("enter", viewModel.VaultStatusMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, vault.RetrieveCallCount);
    }

    [Fact]
    public async Task ReplaceCredentialAsync_UncertainOutcome_KeepsExistingReference()
    {
        var vault = new CountingCredentialVault();
        var store = CreateProfileStore(vault);
        var profile = CreateProfile();
        await store.UpsertAsync(profile);
        using var original = new SensitiveCredential(CreateConnectionString());
        var saved = await store.SaveCredentialAsync(profile.Id, original);
        var reference = saved.Profile.CredentialReference!;

        vault.NextStoreStatus = CredentialVaultStatus.Uncertain;
        var viewModel = CreateViewModel(vault, store);
        viewModel.ApplyProfile(saved.Profile);
        viewModel.ConnectionString = CreateConnectionString("replacement-secret");

        await viewModel.ReplaceCredentialAsync();

        var listed = Assert.Single(await store.ListAsync());
        Assert.Equal(reference, listed.CredentialReference);
        Assert.Contains("uncertain", viewModel.StatusMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveProfileAsync_CleanupFailure_RetainsProfile()
    {
        var vault = new CountingCredentialVault();
        var store = CreateProfileStore(vault);
        var profile = CreateProfile();
        await store.UpsertAsync(profile);
        using var credential = new SensitiveCredential(CreateConnectionString());
        var saved = await store.SaveCredentialAsync(profile.Id, credential);

        vault.NextDeleteStatus = CredentialVaultStatus.Locked;
        var viewModel = CreateViewModel(vault, store);
        viewModel.ConnectionHistory.Add(saved.Profile);

        await viewModel.RemoveProfileAsync(profile.Id, deleteVaultItem: true);

        var listed = Assert.Single(await store.ListAsync());
        Assert.Equal(saved.Profile.CredentialReference, listed.CredentialReference);
        Assert.Contains("cleanup failed", viewModel.StatusMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(viewModel.ConnectionHistory);
    }

    [Fact]
    public async Task SaveCredentialAsync_Success_PersistsOpaqueReferenceOnly()
    {
        var path = CreateSettingsPath();
        var vault = new CountingCredentialVault();
        var store = new JsonConnectionProfileStore(new SettingsService(path), vault);
        var profile = CreateProfile();
        await store.UpsertAsync(profile);

        var viewModel = CreateViewModel(vault, store);
        viewModel.ApplyProfile(profile);
        viewModel.ConnectionString = CreateConnectionString();

        await viewModel.SaveCredentialAsync();

        var persisted = File.ReadAllText(path);
        Assert.DoesNotContain("test-only-secret", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SharedAccessKey", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(viewModel.ActiveCredentialReference);
        Assert.True(CredentialReference.IsOpaque(viewModel.ActiveCredentialReference!.Value));
        Assert.Single(vault.Entries);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private ConnectViewModel CreateViewModel(
        ICredentialVault? vault = null,
        IConnectionProfileStore? store = null) =>
        new(vault, store);

    private JsonConnectionProfileStore CreateProfileStore(ICredentialVault vault) =>
        new(new SettingsService(CreateSettingsPath()), vault);

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

    private sealed class CountingCredentialVault : ICredentialVault
    {
        public Dictionary<string, SensitiveCredential> Entries { get; } = new(StringComparer.Ordinal);

        public int AvailabilityCallCount { get; private set; }

        public int RetrieveCallCount { get; private set; }

        public int StoreCallCount { get; private set; }

        public int DeleteCallCount { get; private set; }

        public CredentialVaultStatus NextStoreStatus { get; set; } = CredentialVaultStatus.Available;

        public CredentialVaultStatus NextDeleteStatus { get; set; } = CredentialVaultStatus.Available;

        public Task<CredentialVaultAvailabilityResult> GetAvailabilityAsync(
            CancellationToken cancellationToken = default)
        {
            AvailabilityCallCount++;
            return Task.FromResult(new CredentialVaultAvailabilityResult(
                CredentialVaultStatus.Available,
                "Enter SAS for this connection."));
        }

        public Task<CredentialVaultMutationResult> StoreAsync(
            CredentialReference reference,
            SensitiveCredential credential,
            CancellationToken cancellationToken = default)
        {
            StoreCallCount++;
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
            RetrieveCallCount++;
            if (!Entries.TryGetValue(reference.Value, out _))
            {
                return Task.FromResult(new CredentialVaultRetrieveResult(
                    CredentialVaultStatus.NotFound,
                    "Credential was not found. Enter SAS for this connection.",
                    null));
            }

            return Task.FromResult(new CredentialVaultRetrieveResult(
                CredentialVaultStatus.Available,
                "Retrieved.",
                new SensitiveCredential(Entries[reference.Value].Reveal())));
        }

        public Task<CredentialVaultMutationResult> DeleteAsync(
            CredentialReference reference,
            CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            if (NextDeleteStatus != CredentialVaultStatus.Available)
            {
                return Task.FromResult(new CredentialVaultMutationResult(
                    NextDeleteStatus,
                    "Cleanup failed."));
            }

            Entries.Remove(reference.Value);
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Available,
                "Deleted."));
        }
    }
}
