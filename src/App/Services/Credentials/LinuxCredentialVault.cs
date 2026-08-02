#nullable enable
using System.Runtime.InteropServices;
using System.Text;

namespace ServiceBusExplorer.App.Services.Credentials;

/// <summary>
/// First-party Linux freedesktop Secret Service adapter via libsecret.
/// Reports provider absence; no file or in-memory production fallback.
/// </summary>
public sealed class LinuxCredentialVault : ICredentialVault
{
    internal const string SchemaName = "org.servicebusexplorer.SasCredential";
    internal const string AttributeKey = "credential_reference";

    public Task<CredentialVaultAvailabilityResult> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsLinux())
        {
            return Task.FromResult(new CredentialVaultAvailabilityResult(
                CredentialVaultStatus.Unsupported,
                "Linux Secret Service is only available on Linux."));
        }

        if (!LinuxSecretNative.TryEnsureLoaded(out var loadError))
        {
            return Task.FromResult(new CredentialVaultAvailabilityResult(
                CredentialVaultStatus.ProviderMissing,
                loadError));
        }

        return Task.FromResult(new CredentialVaultAvailabilityResult(
            CredentialVaultStatus.Available,
            "Linux Secret Service (libsecret) is available."));
    }

    public Task<CredentialVaultMutationResult> StoreAsync(
        CredentialReference reference,
        SensitiveCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(credential);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsLinux())
        {
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Unsupported,
                "Linux Secret Service is only available on Linux."));
        }

        if (!LinuxSecretNative.TryEnsureLoaded(out var loadError))
        {
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.ProviderMissing,
                loadError));
        }

        var secret = credential.Reveal();
        var label = "ServiceBusExplorer SAS " + reference.Value[..8];
        var ok = LinuxSecretNative.secret_password_store_sync(
            LinuxSecretNative.GetSchema(),
            LinuxSecretNative.SecretCollectionDefault,
            label,
            secret,
            IntPtr.Zero,
            out var error,
            AttributeKey,
            reference.Value,
            IntPtr.Zero);

        if (!ok)
        {
            var guidance = LinuxSecretNative.ConsumeError(error, "store");
            return Task.FromResult(new CredentialVaultMutationResult(
                MapStoreError(guidance.status),
                guidance.message));
        }

        return Task.FromResult(new CredentialVaultMutationResult(
            CredentialVaultStatus.Available,
            "Credential stored in the Linux Secret Service."));
    }

    public Task<CredentialVaultRetrieveResult> RetrieveAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsLinux())
        {
            return Task.FromResult(new CredentialVaultRetrieveResult(
                CredentialVaultStatus.Unsupported,
                "Linux Secret Service is only available on Linux.",
                null));
        }

        if (!LinuxSecretNative.TryEnsureLoaded(out var loadError))
        {
            return Task.FromResult(new CredentialVaultRetrieveResult(
                CredentialVaultStatus.ProviderMissing,
                loadError,
                null));
        }

        var pointer = LinuxSecretNative.secret_password_lookup_sync(
            LinuxSecretNative.GetSchema(),
            IntPtr.Zero,
            out var error,
            AttributeKey,
            reference.Value,
            IntPtr.Zero);

        if (error != IntPtr.Zero)
        {
            var guidance = LinuxSecretNative.ConsumeError(error, "retrieve");
            return Task.FromResult(new CredentialVaultRetrieveResult(
                guidance.status,
                guidance.message,
                null));
        }

        if (pointer == IntPtr.Zero)
        {
            return Task.FromResult(new CredentialVaultRetrieveResult(
                CredentialVaultStatus.NotFound,
                "No Secret Service item exists for this credential reference.",
                null));
        }

        try
        {
            var secret = Marshal.PtrToStringUTF8(pointer);
            if (string.IsNullOrEmpty(secret))
            {
                return Task.FromResult(new CredentialVaultRetrieveResult(
                    CredentialVaultStatus.Failure,
                    "Secret Service returned an empty credential.",
                    null));
            }

            return Task.FromResult(new CredentialVaultRetrieveResult(
                CredentialVaultStatus.Available,
                "Credential retrieved from the Linux Secret Service.",
                new SensitiveCredential(secret)));
        }
        finally
        {
            LinuxSecretNative.secret_password_free(pointer);
        }
    }

    public Task<CredentialVaultMutationResult> DeleteAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsLinux())
        {
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Unsupported,
                "Linux Secret Service is only available on Linux."));
        }

        if (!LinuxSecretNative.TryEnsureLoaded(out var loadError))
        {
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.ProviderMissing,
                loadError));
        }

        var ok = LinuxSecretNative.secret_password_clear_sync(
            LinuxSecretNative.GetSchema(),
            IntPtr.Zero,
            out var error,
            AttributeKey,
            reference.Value,
            IntPtr.Zero);

        if (!ok)
        {
            var guidance = LinuxSecretNative.ConsumeError(error, "delete");
            // libsecret returns false with no error when nothing matched.
            if (error == IntPtr.Zero)
            {
                return Task.FromResult(new CredentialVaultMutationResult(
                    CredentialVaultStatus.NotFound,
                    "No Secret Service item exists for this credential reference."));
            }

            return Task.FromResult(new CredentialVaultMutationResult(
                guidance.status,
                guidance.message));
        }

        return Task.FromResult(new CredentialVaultMutationResult(
            CredentialVaultStatus.Available,
            "Credential deleted from the Linux Secret Service."));
    }

    private static CredentialVaultStatus MapStoreError(CredentialVaultStatus status) => status;
}

internal static class LinuxSecretNative
{
    internal const string SecretCollectionDefault = "default";

    private static readonly object Gate = new();
    private static IntPtr _schema;
    private static bool _loadAttempted;
    private static bool _loaded;
    private static string _loadError = "libsecret is not available.";

    [StructLayout(LayoutKind.Sequential)]
    private struct SecretSchemaAttribute
    {
        public IntPtr Name;
        public int Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecretSchema
    {
        public IntPtr Name;
        public int Flags;
        public SecretSchemaAttribute Attr1;
        public SecretSchemaAttribute Attr2;
        public SecretSchemaAttribute Attr3;
        public SecretSchemaAttribute Attr4;
        public SecretSchemaAttribute Attr5;
        public SecretSchemaAttribute Attr6;
        public SecretSchemaAttribute Attr7;
        public SecretSchemaAttribute Attr8;
        public SecretSchemaAttribute Attr9;
        public SecretSchemaAttribute Attr10;
        public SecretSchemaAttribute Attr11;
        public SecretSchemaAttribute Attr12;
        public SecretSchemaAttribute Attr13;
        public SecretSchemaAttribute Attr14;
        public SecretSchemaAttribute Attr15;
        public SecretSchemaAttribute Attr16;
        public SecretSchemaAttribute Attr17;
        public SecretSchemaAttribute Attr18;
        public SecretSchemaAttribute Attr19;
        public SecretSchemaAttribute Attr20;
        public SecretSchemaAttribute Attr21;
        public SecretSchemaAttribute Attr22;
        public SecretSchemaAttribute Attr23;
        public SecretSchemaAttribute Attr24;
        public SecretSchemaAttribute Attr25;
        public SecretSchemaAttribute Attr26;
        public SecretSchemaAttribute Attr27;
        public SecretSchemaAttribute Attr28;
        public SecretSchemaAttribute Attr29;
        public SecretSchemaAttribute Attr30;
        public SecretSchemaAttribute Attr31;
        public SecretSchemaAttribute Attr32;
        public int Reserved;
        public IntPtr Reserved1;
        public IntPtr Reserved2;
        public IntPtr Reserved3;
        public IntPtr Reserved4;
        public IntPtr Reserved5;
        public IntPtr Reserved6;
        public IntPtr Reserved7;
    }

    internal static bool TryEnsureLoaded(out string error)
    {
        lock (Gate)
        {
            if (_loadAttempted)
            {
                error = _loadError;
                return _loaded;
            }

            _loadAttempted = true;
            try
            {
                NativeLibrary.Load("libsecret-1.so.0");
                _schema = CreateSchema();
                _loaded = true;
                _loadError = string.Empty;
            }
            catch (Exception)
            {
                _loaded = false;
                _loadError =
                    "libsecret (libsecret-1.so.0) or a Secret Service provider is missing. Install libsecret and enable a Secret Service, or enter SAS.";
            }

            error = _loadError;
            return _loaded;
        }
    }

    internal static IntPtr GetSchema()
    {
        if (!TryEnsureLoaded(out _))
            throw new InvalidOperationException(_loadError);

        return _schema;
    }

    private static IntPtr CreateSchema()
    {
        var schema = new SecretSchema
        {
            Name = Marshal.StringToHGlobalAnsi(LinuxCredentialVault.SchemaName),
            Flags = 0,
            Attr1 = new SecretSchemaAttribute
            {
                Name = Marshal.StringToHGlobalAnsi(LinuxCredentialVault.AttributeKey),
                Type = 0 // SECRET_SCHEMA_ATTRIBUTE_STRING
            }
        };

        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<SecretSchema>());
        Marshal.StructureToPtr(schema, ptr, false);
        return ptr;
    }

    internal static (CredentialVaultStatus status, string message) ConsumeError(
        IntPtr error,
        string operation)
    {
        if (error == IntPtr.Zero)
        {
            return (CredentialVaultStatus.Failure,
                $"The Linux Secret Service {operation} could not be completed. Enter SAS or retry.");
        }

        try
        {
            // GError: domain(quark int), code(int), message(char*)
            var code = Marshal.ReadInt32(error, IntPtr.Size);
            var messagePtr = Marshal.ReadIntPtr(error, IntPtr.Size * 2);
            var nativeMessage = messagePtr == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUTF8(messagePtr);

            var status = code switch
            {
                // Common libsecret / DBus cancellation and locked states are mapped conservatively.
                _ when Contains(nativeMessage, "cancel") => CredentialVaultStatus.Cancelled,
                _ when Contains(nativeMessage, "locked") => CredentialVaultStatus.Locked,
                _ when Contains(nativeMessage, "denied") => CredentialVaultStatus.PermissionDenied,
                _ when Contains(nativeMessage, "not found") => CredentialVaultStatus.NotFound,
                _ => CredentialVaultStatus.Failure
            };

            return (status,
                $"The Linux Secret Service {operation} could not be completed. Enter SAS or retry.");
        }
        finally
        {
            g_error_free(error);
        }
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null &&
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    [DllImport("libsecret-1.so.0", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool secret_password_store_sync(
        IntPtr schema,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string collection,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string password,
        IntPtr cancellable,
        out IntPtr error,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attributeName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attributeValue,
        IntPtr end);

    [DllImport("libsecret-1.so.0", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr secret_password_lookup_sync(
        IntPtr schema,
        IntPtr cancellable,
        out IntPtr error,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attributeName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attributeValue,
        IntPtr end);

    [DllImport("libsecret-1.so.0", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool secret_password_clear_sync(
        IntPtr schema,
        IntPtr cancellable,
        out IntPtr error,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attributeName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string attributeValue,
        IntPtr end);

    [DllImport("libsecret-1.so.0", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void secret_password_free(IntPtr password);

    [DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_error_free(IntPtr error);
}
