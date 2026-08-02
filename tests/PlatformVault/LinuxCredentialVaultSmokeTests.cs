#nullable enable
using ServiceBusExplorer.App.Services.Credentials;
using Xunit;

namespace ServiceBusExplorer.PlatformVaultTests;

public sealed class LinuxCredentialVaultSmokeTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"sbe-linux-vault-{Guid.NewGuid():N}");

    [Fact]
    public async Task LinuxVault_Conforms_OrReportsProviderMissingOrUnsupported()
    {
        Directory.CreateDirectory(_directory);
        var vault = new LinuxCredentialVault();

        if (!OperatingSystem.IsLinux())
        {
            var availability = await vault.GetAvailabilityAsync(TestContext.Current.CancellationToken);
            Assert.Equal(CredentialVaultStatus.Unsupported, availability.Status);
            CredentialVaultConformance.AssertNoForbiddenFiles(_directory);
            return;
        }

        var probe = await vault.GetAvailabilityAsync(TestContext.Current.CancellationToken);
        if (probe.Status is CredentialVaultStatus.ProviderMissing or CredentialVaultStatus.Unavailable)
        {
            Assert.Contains("libsecret", probe.RecoveryGuidance, StringComparison.OrdinalIgnoreCase);
            CredentialVaultConformance.AssertNoForbiddenFiles(_directory);
            return;
        }

        await CredentialVaultConformance.AssertConformsAsync(
            vault,
            _directory,
            TestContext.Current.CancellationToken);

        var reference = CredentialReference.CreateNew();
        const string secret =
            "Endpoint=sb://linux-smoke.example/;SharedAccessKeyName=test;SharedAccessKey=linux-restart-secret";

        using (var credential = new SensitiveCredential(secret))
        {
            var stored = await vault.StoreAsync(reference, credential, TestContext.Current.CancellationToken);
            Assert.Equal(CredentialVaultStatus.Available, stored.Status);
        }

        var restarted = new LinuxCredentialVault();
        var retrieved = await restarted.RetrieveAsync(reference, TestContext.Current.CancellationToken);
        Assert.Equal(secret, retrieved.Credential!.Reveal());
        retrieved.Credential.Dispose();

        var deleted = await restarted.DeleteAsync(reference, TestContext.Current.CancellationToken);
        Assert.Equal(CredentialVaultStatus.Available, deleted.Status);
        CredentialVaultConformance.AssertNoForbiddenFiles(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
