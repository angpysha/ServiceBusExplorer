#nullable enable
using System.Text.Json;
using Xunit;

namespace ServiceBusExplorer.PlatformVaultTests;

/// <summary>
/// Reusable no-file-fallback conformance suite for any <see cref="ICredentialVault"/> candidate.
/// T009–T011 smoke tests must invoke <see cref="AssertConformsAsync"/> against the real OS adapter.
/// </summary>
public static class CredentialVaultConformance
{
    private static readonly string[] ForbiddenFileNameFragments =
    [
        "credentials.dat",
        "credentialcache",
        "sbexplorer-secret",
        "saved-sas",
        ".dpapi"
    ];

    public static async Task AssertConformsAsync(
        ICredentialVault vault,
        string monitoredDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentException.ThrowIfNullOrWhiteSpace(monitoredDirectory);

        Directory.CreateDirectory(monitoredDirectory);
        AssertNoForbiddenFiles(monitoredDirectory);

        var availability = await vault.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        Assert.False(string.IsNullOrWhiteSpace(availability.RecoveryGuidance));
        Assert.DoesNotContain("SharedAccessKey", availability.RecoveryGuidance, StringComparison.OrdinalIgnoreCase);

        if (availability.Status is CredentialVaultStatus.Unsupported
            or CredentialVaultStatus.ProviderMissing
            or CredentialVaultStatus.Unavailable)
        {
            AssertNoForbiddenFiles(monitoredDirectory);
            return;
        }

        Assert.Equal(CredentialVaultStatus.Available, availability.Status);

        var reference = CredentialReference.CreateNew();
        const string originalSecret = "Endpoint=sb://conformance.example/;SharedAccessKeyName=test;SharedAccessKey=conformance-secret-1";
        const string replacementSecret = "Endpoint=sb://conformance.example/;SharedAccessKeyName=test;SharedAccessKey=conformance-secret-2";

        using (var original = new SensitiveCredential(originalSecret))
        {
            var stored = await vault.StoreAsync(reference, original, cancellationToken).ConfigureAwait(false);
            Assert.Equal(CredentialVaultStatus.Available, stored.Status);
            AssertSecretSafeGuidance(stored.RecoveryGuidance);
        }

        AssertNoForbiddenFiles(monitoredDirectory);

        var retrieved = await vault.RetrieveAsync(reference, cancellationToken).ConfigureAwait(false);
        Assert.Equal(CredentialVaultStatus.Available, retrieved.Status);
        Assert.NotNull(retrieved.Credential);
        Assert.Equal(originalSecret, retrieved.Credential!.Reveal());
        Assert.Equal("[redacted]", retrieved.Credential.ToString());
        Assert.DoesNotContain(originalSecret, JsonSerializer.Serialize(new { Hint = retrieved.Credential.ToString() }));
        retrieved.Credential.Dispose();

        using (var replacement = new SensitiveCredential(replacementSecret))
        {
            var replaced = await vault.StoreAsync(reference, replacement, cancellationToken).ConfigureAwait(false);
            Assert.Equal(CredentialVaultStatus.Available, replaced.Status);
        }

        var afterReplace = await vault.RetrieveAsync(reference, cancellationToken).ConfigureAwait(false);
        Assert.Equal(replacementSecret, afterReplace.Credential!.Reveal());
        afterReplace.Credential.Dispose();

        var deleted = await vault.DeleteAsync(reference, cancellationToken).ConfigureAwait(false);
        Assert.Equal(CredentialVaultStatus.Available, deleted.Status);

        var missing = await vault.RetrieveAsync(reference, cancellationToken).ConfigureAwait(false);
        Assert.Equal(CredentialVaultStatus.NotFound, missing.Status);
        Assert.Null(missing.Credential);
        AssertSecretSafeGuidance(missing.RecoveryGuidance);

        AssertNoForbiddenFiles(monitoredDirectory);
    }

    public static async Task AssertTypedFailuresDoNotWriteFilesAsync(
        ICredentialVault failingVault,
        string monitoredDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failingVault);
        Directory.CreateDirectory(monitoredDirectory);

        var reference = CredentialReference.CreateNew();
        using var credential = new SensitiveCredential(
            "Endpoint=sb://conformance.example/;SharedAccessKeyName=test;SharedAccessKey=failure-secret");

        var store = await failingVault.StoreAsync(reference, credential, cancellationToken).ConfigureAwait(false);
        Assert.NotEqual(CredentialVaultStatus.Available, store.Status);
        AssertSecretSafeGuidance(store.RecoveryGuidance);

        var retrieve = await failingVault.RetrieveAsync(reference, cancellationToken).ConfigureAwait(false);
        Assert.NotEqual(CredentialVaultStatus.Available, retrieve.Status);
        Assert.Null(retrieve.Credential);

        var delete = await failingVault.DeleteAsync(reference, cancellationToken).ConfigureAwait(false);
        Assert.NotEqual(CredentialVaultStatus.Available, delete.Status);

        AssertNoForbiddenFiles(monitoredDirectory);
    }

    public static void AssertNoForbiddenFiles(string monitoredDirectory)
    {
        if (!Directory.Exists(monitoredDirectory))
            return;

        var offenders = Directory
            .EnumerateFiles(monitoredDirectory, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return ForbiddenFileNameFragments.Any(fragment =>
                    name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Credential vault conformance forbids fallback credential files: " +
            string.Join(", ", offenders));
    }

    private static void AssertSecretSafeGuidance(string guidance)
    {
        Assert.False(string.IsNullOrWhiteSpace(guidance));
        Assert.DoesNotContain("SharedAccessKey", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("conformance-secret", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failure-secret", guidance, StringComparison.OrdinalIgnoreCase);
    }
}
