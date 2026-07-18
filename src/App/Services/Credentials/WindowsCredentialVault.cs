#nullable enable
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ServiceBusExplorer.App.Services.Credentials;

/// <summary>
/// First-party current-user Windows Credential Manager generic-credential adapter.
/// No file or DPAPI fallback.
/// </summary>
public sealed class WindowsCredentialVault : ICredentialVault
{
    internal const string TargetPrefix = "ServiceBusExplorer/SasCredential/";

    public Task<CredentialVaultAvailabilityResult> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new CredentialVaultAvailabilityResult(
                CredentialVaultStatus.Unsupported,
                "Windows Credential Manager is only available on Windows."));
        }

        var probeTarget = TargetPrefix + CredentialReference.CreateNew().Value;
        var readOk = WindowsCredentialNative.CredRead(
            probeTarget,
            WindowsCredentialNative.CredTypeGeneric,
            0,
            out var credentialPtr);
        var error = Marshal.GetLastWin32Error();

        try
        {
            if (readOk || error == WindowsCredentialNative.ErrorNotFound)
            {
                return Task.FromResult(new CredentialVaultAvailabilityResult(
                    CredentialVaultStatus.Available,
                    "Windows Credential Manager is available."));
            }

            return Task.FromResult(new CredentialVaultAvailabilityResult(
                MapWin32(error),
                MapGuidance(error, "availability")));
        }
        finally
        {
            if (credentialPtr != IntPtr.Zero)
                WindowsCredentialNative.CredFree(credentialPtr);
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

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Unsupported,
                "Windows Credential Manager is only available on Windows."));
        }

        var target = TargetPrefix + reference.Value;
        var secretBytes = Encoding.UTF8.GetBytes(credential.Reveal());
        var blobHandle = GCHandle.Alloc(secretBytes, GCHandleType.Pinned);
        try
        {
            var native = new WindowsCredentialNative.Credential
            {
                Type = WindowsCredentialNative.CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = blobHandle.AddrOfPinnedObject(),
                Persist = WindowsCredentialNative.CredPersistLocalMachine,
                UserName = reference.Value
            };

            if (!WindowsCredentialNative.CredWrite(ref native, 0))
            {
                var error = Marshal.GetLastWin32Error();
                return Task.FromResult(new CredentialVaultMutationResult(
                    MapWin32(error),
                    MapGuidance(error, "store")));
            }

            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Available,
                "Credential stored in Windows Credential Manager."));
        }
        finally
        {
            if (blobHandle.IsAllocated)
                blobHandle.Free();
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    public Task<CredentialVaultRetrieveResult> RetrieveAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new CredentialVaultRetrieveResult(
                CredentialVaultStatus.Unsupported,
                "Windows Credential Manager is only available on Windows.",
                null));
        }

        var target = TargetPrefix + reference.Value;
        if (!WindowsCredentialNative.CredRead(
                target,
                WindowsCredentialNative.CredTypeGeneric,
                0,
                out var credentialPtr))
        {
            var error = Marshal.GetLastWin32Error();
            return Task.FromResult(new CredentialVaultRetrieveResult(
                error == WindowsCredentialNative.ErrorNotFound
                    ? CredentialVaultStatus.NotFound
                    : MapWin32(error),
                MapGuidance(error, "retrieve"),
                null));
        }

        try
        {
            var native = Marshal.PtrToStructure<WindowsCredentialNative.Credential>(credentialPtr);
            if (native.CredentialBlob == IntPtr.Zero || native.CredentialBlobSize == 0)
            {
                return Task.FromResult(new CredentialVaultRetrieveResult(
                    CredentialVaultStatus.Failure,
                    "Windows Credential Manager returned an empty credential blob.",
                    null));
            }

            var buffer = new byte[native.CredentialBlobSize];
            Marshal.Copy(native.CredentialBlob, buffer, 0, buffer.Length);
            try
            {
                var secret = Encoding.UTF8.GetString(buffer);
                return Task.FromResult(new CredentialVaultRetrieveResult(
                    CredentialVaultStatus.Available,
                    "Credential retrieved from Windows Credential Manager.",
                    new SensitiveCredential(secret)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }
        finally
        {
            WindowsCredentialNative.CredFree(credentialPtr);
        }
    }

    public Task<CredentialVaultMutationResult> DeleteAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Unsupported,
                "Windows Credential Manager is only available on Windows."));
        }

        var target = TargetPrefix + reference.Value;
        if (WindowsCredentialNative.CredDelete(target, WindowsCredentialNative.CredTypeGeneric, 0))
        {
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Available,
                "Credential deleted from Windows Credential Manager."));
        }

        var error = Marshal.GetLastWin32Error();
        return Task.FromResult(new CredentialVaultMutationResult(
            error == WindowsCredentialNative.ErrorNotFound
                ? CredentialVaultStatus.NotFound
                : MapWin32(error),
            MapGuidance(error, "delete")));
    }

    private static CredentialVaultStatus MapWin32(int error) =>
        error switch
        {
            WindowsCredentialNative.ErrorNotFound => CredentialVaultStatus.NotFound,
            WindowsCredentialNative.ErrorAccessDenied => CredentialVaultStatus.PermissionDenied,
            WindowsCredentialNative.ErrorCancelled => CredentialVaultStatus.Cancelled,
            WindowsCredentialNative.ErrorNoSuchLogonSession => CredentialVaultStatus.Unavailable,
            _ => CredentialVaultStatus.Failure
        };

    private static string MapGuidance(int error, string operation) =>
        error switch
        {
            WindowsCredentialNative.ErrorNotFound =>
                "No Windows Credential Manager item exists for this credential reference.",
            WindowsCredentialNative.ErrorAccessDenied =>
                "Windows Credential Manager permission was denied. Enter SAS for this connection.",
            WindowsCredentialNative.ErrorCancelled =>
                "The Windows credential prompt was cancelled. Enter SAS or retry.",
            WindowsCredentialNative.ErrorNoSuchLogonSession =>
                "Windows Credential Manager is unavailable for this session. Enter SAS.",
            _ => $"The Windows Credential Manager {operation} could not be completed. Enter SAS or retry."
        };
}

internal static class WindowsCredentialNative
{
    internal const int CredTypeGeneric = 1;
    internal const int CredPersistLocalMachine = 2;
    internal const int ErrorNotFound = 1168;
    internal const int ErrorAccessDenied = 5;
    internal const int ErrorCancelled = 1223;
    internal const int ErrorNoSuchLogonSession = 1312;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Credential
    {
        public uint Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CredRead(
        string target,
        int type,
        int reservedFlag,
        out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern void CredFree(IntPtr credential);
}
