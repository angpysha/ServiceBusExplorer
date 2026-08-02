#nullable enable
using Xunit;

namespace ServiceBusExplorer.PlatformVaultTests;

public sealed class CredentialVaultConformanceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"sbe-vault-conformance-{Guid.NewGuid():N}");

    [Fact]
    public async Task Harness_AcceptsMemoryVaultWithoutCreatingFallbackFiles()
    {
        Directory.CreateDirectory(_directory);
        var vault = new MemoryCredentialVault();

        await CredentialVaultConformance.AssertConformsAsync(
            vault,
            _directory,
            TestContext.Current.CancellationToken);

        Assert.Empty(Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Harness_AcceptsTypedFailuresWithoutCreatingFallbackFiles()
    {
        Directory.CreateDirectory(_directory);
        var vault = new MemoryCredentialVault
        {
            AvailabilityStatus = CredentialVaultStatus.Available,
            NextStoreStatus = CredentialVaultStatus.PermissionDenied,
            NextRetrieveStatus = CredentialVaultStatus.Locked,
            NextDeleteStatus = CredentialVaultStatus.Unavailable
        };

        await CredentialVaultConformance.AssertTypedFailuresDoNotWriteFilesAsync(
            vault,
            _directory,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Harness_RejectsVaultThatWritesCredentialFallbackFile()
    {
        Directory.CreateDirectory(_directory);
        var vault = new FileWritingForbiddenVault(_directory);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            CredentialVaultConformance.AssertConformsAsync(
                vault,
                _directory,
                TestContext.Current.CancellationToken));

        Assert.Contains("forbids fallback credential files", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Harness_AllowsProviderMissingWithoutRoundTrip()
    {
        Directory.CreateDirectory(_directory);
        var vault = new MemoryCredentialVault
        {
            AvailabilityStatus = CredentialVaultStatus.ProviderMissing
        };

        await CredentialVaultConformance.AssertConformsAsync(
            vault,
            _directory,
            TestContext.Current.CancellationToken);

        Assert.Empty(vault.Entries);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class MemoryCredentialVault : ICredentialVault
    {
        public Dictionary<string, string> Entries { get; } = new(StringComparer.Ordinal);
        public CredentialVaultStatus AvailabilityStatus { get; set; } = CredentialVaultStatus.Available;
        public CredentialVaultStatus NextStoreStatus { get; set; } = CredentialVaultStatus.Available;
        public CredentialVaultStatus NextRetrieveStatus { get; set; } = CredentialVaultStatus.Available;
        public CredentialVaultStatus NextDeleteStatus { get; set; } = CredentialVaultStatus.Available;

        public Task<CredentialVaultAvailabilityResult> GetAvailabilityAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CredentialVaultAvailabilityResult(
                AvailabilityStatus,
                AvailabilityStatus == CredentialVaultStatus.Available
                    ? "Native vault is available for conformance."
                    : "Native vault provider is missing or unavailable."));

        public Task<CredentialVaultMutationResult> StoreAsync(
            CredentialReference reference,
            SensitiveCredential credential,
            CancellationToken cancellationToken = default)
        {
            if (NextStoreStatus != CredentialVaultStatus.Available)
            {
                return Task.FromResult(new CredentialVaultMutationResult(
                    NextStoreStatus,
                    "Store was denied by the native vault."));
            }

            Entries[reference.Value] = credential.Reveal();
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Available,
                "Stored in the designated vault."));
        }

        public Task<CredentialVaultRetrieveResult> RetrieveAsync(
            CredentialReference reference,
            CancellationToken cancellationToken = default)
        {
            if (NextRetrieveStatus != CredentialVaultStatus.Available)
            {
                return Task.FromResult(new CredentialVaultRetrieveResult(
                    NextRetrieveStatus,
                    "Retrieve could not use the native vault.",
                    null));
            }

            if (!Entries.TryGetValue(reference.Value, out var secret))
            {
                return Task.FromResult(new CredentialVaultRetrieveResult(
                    CredentialVaultStatus.NotFound,
                    "Credential was not found in the designated vault.",
                    null));
            }

            return Task.FromResult(new CredentialVaultRetrieveResult(
                CredentialVaultStatus.Available,
                "Retrieved from the designated vault.",
                new SensitiveCredential(secret)));
        }

        public Task<CredentialVaultMutationResult> DeleteAsync(
            CredentialReference reference,
            CancellationToken cancellationToken = default)
        {
            if (NextDeleteStatus != CredentialVaultStatus.Available)
            {
                return Task.FromResult(new CredentialVaultMutationResult(
                    NextDeleteStatus,
                    "Delete could not use the native vault."));
            }

            Entries.Remove(reference.Value);
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Available,
                "Deleted from the designated vault."));
        }
    }

    /// <summary>
    /// Deliberately violates the contract by writing credentials.dat — the harness must fail.
    /// </summary>
    private sealed class FileWritingForbiddenVault : ICredentialVault
    {
        private readonly string _directory;

        public FileWritingForbiddenVault(string directory) => _directory = directory;

        public Task<CredentialVaultAvailabilityResult> GetAvailabilityAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CredentialVaultAvailabilityResult(
                CredentialVaultStatus.Available,
                "Available."));

        public Task<CredentialVaultMutationResult> StoreAsync(
            CredentialReference reference,
            SensitiveCredential credential,
            CancellationToken cancellationToken = default)
        {
            File.WriteAllText(
                Path.Combine(_directory, "credentials.dat"),
                credential.Reveal());
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Available,
                "Stored."));
        }

        public Task<CredentialVaultRetrieveResult> RetrieveAsync(
            CredentialReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CredentialVaultRetrieveResult(
                CredentialVaultStatus.NotFound,
                "Missing.",
                null));

        public Task<CredentialVaultMutationResult> DeleteAsync(
            CredentialReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Available,
                "Deleted."));
    }
}
