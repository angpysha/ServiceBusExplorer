#nullable enable
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ServiceBusExplorer.App.Services.Credentials;

/// <summary>
/// First-party macOS login Keychain Services generic-password adapter.
/// No file, DPAPI, or in-memory production fallback.
/// </summary>
public sealed class MacOsCredentialVault : ICredentialVault
{
    internal const string ServiceName = "ServiceBusExplorer.SasCredential";

    public Task<CredentialVaultAvailabilityResult> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsMacOS())
        {
            return Task.FromResult(new CredentialVaultAvailabilityResult(
                CredentialVaultStatus.Unsupported,
                "macOS Keychain Services are only available on macOS."));
        }

        // A missing probe item proves the default keychain answers without writing secrets.
        var probe = CredentialReference.CreateNew();
        var status = FindItem(probe.Value, out _, out var passwordData, out var itemRef);
        try
        {
            if (status is MacOsKeychainNative.ErrSecSuccess or MacOsKeychainNative.ErrSecItemNotFound)
            {
                return Task.FromResult(new CredentialVaultAvailabilityResult(
                    CredentialVaultStatus.Available,
                    "macOS Keychain Services are available."));
            }

            return Task.FromResult(new CredentialVaultAvailabilityResult(
                MapStatus(status),
                MapGuidance(status, "availability")));
        }
        finally
        {
            if (passwordData != IntPtr.Zero)
                MacOsKeychainNative.SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
            if (itemRef != IntPtr.Zero)
                MacOsKeychainNative.CFRelease(itemRef);
        }
    }

    public Task<CredentialVaultMutationResult> StoreAsync(
        CredentialReference reference,
        SensitiveCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(credential);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsMacOS())
        {
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Unsupported,
                "macOS Keychain Services are only available on macOS."));
        }

        var passwordBytes = Encoding.UTF8.GetBytes(credential.Reveal());
        try
        {
            var addStatus = AddItem(reference.Value, passwordBytes);
            if (addStatus == MacOsKeychainNative.ErrSecSuccess)
            {
                return Task.FromResult(new CredentialVaultMutationResult(
                    CredentialVaultStatus.Available,
                    "Credential stored in the macOS Keychain."));
            }

            if (addStatus == MacOsKeychainNative.ErrSecDuplicateItem)
            {
                var findStatus = FindItem(
                    reference.Value,
                    out _,
                    out _,
                    out var itemRef);
                try
                {
                    if (findStatus != MacOsKeychainNative.ErrSecSuccess || itemRef == IntPtr.Zero)
                    {
                        return Task.FromResult(new CredentialVaultMutationResult(
                            CredentialVaultStatus.Uncertain,
                            "A Keychain item conflict was reported but the existing item could not be opened for replacement."));
                    }

                    var updateStatus = MacOsKeychainNative.SecKeychainItemModifyAttributesAndData(
                        itemRef,
                        IntPtr.Zero,
                        (uint)passwordBytes.Length,
                        passwordBytes);

                    if (updateStatus == MacOsKeychainNative.ErrSecSuccess)
                    {
                        return Task.FromResult(new CredentialVaultMutationResult(
                            CredentialVaultStatus.Available,
                            "Credential replaced in the macOS Keychain."));
                    }

                    return Task.FromResult(new CredentialVaultMutationResult(
                        MapStatus(updateStatus),
                        MapGuidance(updateStatus, "replace")));
                }
                finally
                {
                    if (itemRef != IntPtr.Zero)
                        MacOsKeychainNative.CFRelease(itemRef);
                }
            }

            return Task.FromResult(new CredentialVaultMutationResult(
                MapStatus(addStatus),
                MapGuidance(addStatus, "store")));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    public Task<CredentialVaultRetrieveResult> RetrieveAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsMacOS())
        {
            return Task.FromResult(new CredentialVaultRetrieveResult(
                CredentialVaultStatus.Unsupported,
                "macOS Keychain Services are only available on macOS.",
                null));
        }

        var status = FindItem(
            reference.Value,
            out var passwordLength,
            out var passwordData,
            out var itemRef);

        try
        {
            if (status == MacOsKeychainNative.ErrSecItemNotFound)
            {
                return Task.FromResult(new CredentialVaultRetrieveResult(
                    CredentialVaultStatus.NotFound,
                    "No Keychain item exists for this credential reference.",
                    null));
            }

            if (status != MacOsKeychainNative.ErrSecSuccess || passwordData == IntPtr.Zero)
            {
                return Task.FromResult(new CredentialVaultRetrieveResult(
                    MapStatus(status),
                    MapGuidance(status, "retrieve"),
                    null));
            }

            var buffer = new byte[passwordLength];
            Marshal.Copy(passwordData, buffer, 0, (int)passwordLength);
            try
            {
                var secret = Encoding.UTF8.GetString(buffer);
                return Task.FromResult(new CredentialVaultRetrieveResult(
                    CredentialVaultStatus.Available,
                    "Credential retrieved from the macOS Keychain.",
                    new SensitiveCredential(secret)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }
        finally
        {
            if (passwordData != IntPtr.Zero)
                MacOsKeychainNative.SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
            if (itemRef != IntPtr.Zero)
                MacOsKeychainNative.CFRelease(itemRef);
        }
    }

    public Task<CredentialVaultMutationResult> DeleteAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsMacOS())
        {
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Unsupported,
                "macOS Keychain Services are only available on macOS."));
        }

        var status = FindItem(reference.Value, out _, out var passwordData, out var itemRef);
        try
        {
            if (passwordData != IntPtr.Zero)
                MacOsKeychainNative.SecKeychainItemFreeContent(IntPtr.Zero, passwordData);

            if (status == MacOsKeychainNative.ErrSecItemNotFound)
            {
                return Task.FromResult(new CredentialVaultMutationResult(
                    CredentialVaultStatus.NotFound,
                    "No Keychain item exists for this credential reference."));
            }

            if (status != MacOsKeychainNative.ErrSecSuccess || itemRef == IntPtr.Zero)
            {
                return Task.FromResult(new CredentialVaultMutationResult(
                    MapStatus(status),
                    MapGuidance(status, "delete")));
            }

            var deleteStatus = MacOsKeychainNative.SecKeychainItemDelete(itemRef);
            if (deleteStatus == MacOsKeychainNative.ErrSecSuccess)
            {
                return Task.FromResult(new CredentialVaultMutationResult(
                    CredentialVaultStatus.Available,
                    "Credential deleted from the macOS Keychain."));
            }

            return Task.FromResult(new CredentialVaultMutationResult(
                MapStatus(deleteStatus),
                MapGuidance(deleteStatus, "delete")));
        }
        finally
        {
            if (itemRef != IntPtr.Zero)
                MacOsKeychainNative.CFRelease(itemRef);
        }
    }

    private static int AddItem(string account, byte[] passwordBytes)
    {
        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(account);
        var status = MacOsKeychainNative.SecKeychainAddGenericPassword(
            IntPtr.Zero,
            (uint)serviceBytes.Length,
            serviceBytes,
            (uint)accountBytes.Length,
            accountBytes,
            (uint)passwordBytes.Length,
            passwordBytes,
            out var itemRef);

        if (itemRef != IntPtr.Zero)
            MacOsKeychainNative.CFRelease(itemRef);

        return status;
    }

    private static int FindItem(
        string account,
        out uint passwordLength,
        out IntPtr passwordData,
        out IntPtr itemRef)
    {
        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(account);
        return MacOsKeychainNative.SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)serviceBytes.Length,
            serviceBytes,
            (uint)accountBytes.Length,
            accountBytes,
            out passwordLength,
            out passwordData,
            out itemRef);
    }

    private static CredentialVaultStatus MapStatus(int status) =>
        status switch
        {
            MacOsKeychainNative.ErrSecSuccess => CredentialVaultStatus.Available,
            MacOsKeychainNative.ErrSecItemNotFound => CredentialVaultStatus.NotFound,
            MacOsKeychainNative.ErrSecAuthFailed => CredentialVaultStatus.PermissionDenied,
            MacOsKeychainNative.ErrSecUserCanceled => CredentialVaultStatus.Cancelled,
            MacOsKeychainNative.ErrSecInteractionNotAllowed => CredentialVaultStatus.Locked,
            MacOsKeychainNative.ErrSecNotAvailable => CredentialVaultStatus.Unavailable,
            _ => CredentialVaultStatus.Failure
        };

    private static string MapGuidance(int status, string operation) =>
        status switch
        {
            MacOsKeychainNative.ErrSecItemNotFound =>
                "No Keychain item exists for this credential reference.",
            MacOsKeychainNative.ErrSecAuthFailed =>
                "Keychain permission was denied. Unlock the keychain or enter SAS for this connection.",
            MacOsKeychainNative.ErrSecUserCanceled =>
                "The Keychain prompt was cancelled. Enter SAS for this connection or retry.",
            MacOsKeychainNative.ErrSecInteractionNotAllowed =>
                "The Keychain is locked or interaction is not allowed. Unlock it or enter SAS.",
            MacOsKeychainNative.ErrSecNotAvailable =>
                "The macOS Keychain is unavailable. Enter SAS for this connection.",
            _ => $"The Keychain {operation} could not be completed. Enter SAS or retry."
        };
}

internal static class MacOsKeychainNative
{
    internal const int ErrSecSuccess = 0;
    internal const int ErrSecItemNotFound = -25300;
    internal const int ErrSecAuthFailed = -25293;
    internal const int ErrSecUserCanceled = -128;
    internal const int ErrSecInteractionNotAllowed = -25308;
    internal const int ErrSecNotAvailable = -25291;
    internal const int ErrSecDuplicateItem = -25299;

    private const string SecurityFramework =
        "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationFramework =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(SecurityFramework)]
    internal static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        uint passwordLength,
        byte[] passwordData,
        out IntPtr itemRef);

    [DllImport(SecurityFramework)]
    internal static extern int SecKeychainFindGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        out uint passwordLength,
        out IntPtr passwordData,
        out IntPtr itemRef);

    [DllImport(SecurityFramework)]
    internal static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(SecurityFramework)]
    internal static extern int SecKeychainItemModifyAttributesAndData(
        IntPtr itemRef,
        IntPtr attrList,
        uint length,
        byte[] data);

    [DllImport(SecurityFramework)]
    internal static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(CoreFoundationFramework)]
    internal static extern void CFRelease(IntPtr cf);
}
