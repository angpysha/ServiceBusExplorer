#nullable enable
using ServiceBusExplorer.App.Services.Credentials;
using Xunit;

namespace ServiceBusExplorer.PlatformVaultTests;

public sealed class WindowsCredentialVaultSmokeTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"sbe-windows-vault-{Guid.NewGuid():N}");

    [Fact]
    public async Task WindowsVault_Conforms_OrReportsUnsupportedOffWindows()
    {
        Directory.CreateDirectory(_directory);
        var vault = new WindowsCredentialVault();

        if (!OperatingSystem.IsWindows())
        {
            var availability = await vault.GetAvailabilityAsync(TestContext.Current.CancellationToken);
            Assert.Equal(CredentialVaultStatus.Unsupported, availability.Status);
            CredentialVaultConformance.AssertNoForbiddenFiles(_directory);
            return;
        }

        await CredentialVaultConformance.AssertConformsAsync(
            vault,
            _directory,
            TestContext.Current.CancellationToken);

        var reference = CredentialReference.CreateNew();
        const string secret =
            "Endpoint=sb://windows-smoke.example/;SharedAccessKeyName=test;SharedAccessKey=windows-restart-secret";

        using (var credential = new SensitiveCredential(secret))
        {
            var stored = await vault.StoreAsync(reference, credential, TestContext.Current.CancellationToken);
            Assert.Equal(CredentialVaultStatus.Available, stored.Status);
        }

        var restarted = new WindowsCredentialVault();
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
