#nullable enable
using ServiceBusExplorer.App.Services.Credentials;
using Xunit;

namespace ServiceBusExplorer.PlatformVaultTests;

public sealed class MacOsCredentialVaultSmokeTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"sbe-macos-vault-{Guid.NewGuid():N}");

    [Fact]
    public async Task MacOsVault_Conforms_AndSurvivesNewInstanceRestart()
    {
        if (!OperatingSystem.IsMacOS())
        {
            // Windows/Linux hosts cover their own adapters; this smoke is macOS-only.
            return;
        }

        Directory.CreateDirectory(_directory);
        var vault = new MacOsCredentialVault();

        await CredentialVaultConformance.AssertConformsAsync(
            vault,
            _directory,
            TestContext.Current.CancellationToken);

        var reference = CredentialReference.CreateNew();
        const string secret =
            "Endpoint=sb://macos-smoke.example/;SharedAccessKeyName=test;SharedAccessKey=macos-restart-secret";

        using (var credential = new SensitiveCredential(secret))
        {
            var stored = await vault.StoreAsync(
                reference,
                credential,
                TestContext.Current.CancellationToken);
            Assert.Equal(CredentialVaultStatus.Available, stored.Status);
        }

        // New instance simulates process restart against the same login keychain.
        var restarted = new MacOsCredentialVault();
        var retrieved = await restarted.RetrieveAsync(
            reference,
            TestContext.Current.CancellationToken);
        Assert.Equal(CredentialVaultStatus.Available, retrieved.Status);
        Assert.Equal(secret, retrieved.Credential!.Reveal());
        retrieved.Credential.Dispose();

        var deleted = await restarted.DeleteAsync(
            reference,
            TestContext.Current.CancellationToken);
        Assert.Equal(CredentialVaultStatus.Available, deleted.Status);

        var missing = await restarted.RetrieveAsync(
            reference,
            TestContext.Current.CancellationToken);
        Assert.Equal(CredentialVaultStatus.NotFound, missing.Status);
        Assert.Null(missing.Credential);

        CredentialVaultConformance.AssertNoForbiddenFiles(_directory);
    }

    [Fact]
    public async Task MacOsVault_MissingItem_ReturnsNotFoundWithoutFiles()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        Directory.CreateDirectory(_directory);
        var vault = new MacOsCredentialVault();
        var reference = CredentialReference.CreateNew();

        var retrieved = await vault.RetrieveAsync(
            reference,
            TestContext.Current.CancellationToken);

        Assert.Equal(CredentialVaultStatus.NotFound, retrieved.Status);
        Assert.Null(retrieved.Credential);
        CredentialVaultConformance.AssertNoForbiddenFiles(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
