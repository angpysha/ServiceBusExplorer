#nullable enable
using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Validates connection requests and constructs async-disposable live contexts.
/// </summary>
public sealed class ConnectionContextFactory : IConnectionContextFactory
{
    private const int EmulatorAdministrationPort = 5300;

    private readonly ICredentialVault _vault;
    private readonly IConnectionProbe? _probeOverride;
    private readonly Func<ConnectionRequest, TokenCredential>? _tokenCredentialFactory;

    public ConnectionContextFactory(
        ICredentialVault vault,
        IConnectionProbe? probeOverride = null,
        Func<ConnectionRequest, TokenCredential>? tokenCredentialFactory = null)
    {
        _vault = vault;
        _probeOverride = probeOverride;
        _tokenCredentialFactory = tokenCredentialFactory;
    }

    public async Task<ConnectionCreateResult> CreateAsync(
        ConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validationError = ValidateRequest(request);
        if (validationError is not null)
            return ConnectionCreateResult.Failed(ConnectionFailureCategory.Validation, validationError);

        SensitiveCredential? resolvedCredential = null;
        var ownsResolvedCredential = false;

        try
        {
            if (request.AuthMode == ServiceBusAuthMode.Sas)
            {
                var resolution = await ResolveSasCredentialAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                if (!resolution.Success)
                    return resolution.Result!;

                resolvedCredential = resolution.Credential;
                ownsResolvedCredential = resolution.OwnsCredential;
            }
            else if (request.AuthMode == ServiceBusAuthMode.AzureActiveDirectory &&
                     request.CredentialReference is not null)
            {
                return ConnectionCreateResult.Failed(
                    ConnectionFailureCategory.Validation,
                    "Entra connections must not use the credential vault.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var fullyQualifiedNamespace = NormalizeNamespace(request.NamespaceEndpoint);
            ServiceBusClient client;
            ServiceBusAdministrationClient? adminClient = null;
            IConnectionProbe probe;

            if (request.AuthMode == ServiceBusAuthMode.Sas)
            {
                var connectionString = resolvedCredential!.Reveal();
                // Namespace-scoped clients must not carry EntityPath: Azure SDK rejects
                // CreateReceiver/CreateSender for any other entity (e.g. subscriptions).
                var messagingConnectionString = PrepareSasConnectionString(
                    connectionString,
                    request.Scope,
                    forAdministration: false);
                client = new ServiceBusClient(messagingConnectionString);
                if (request.Scope == ConnectionScope.Namespace)
                {
                    var administrationConnectionString = PrepareSasConnectionString(
                        connectionString,
                        request.Scope,
                        forAdministration: true);
                    adminClient = new ServiceBusAdministrationClient(administrationConnectionString);
                    probe = _probeOverride ?? new AdministrationClientConnectionProbe(adminClient);
                }
                else
                {
                    probe = _probeOverride ?? new NoOpConnectionProbe();
                }
            }
            else if (request.AuthMode == ServiceBusAuthMode.AzureActiveDirectory)
            {
                var credential = _tokenCredentialFactory?.Invoke(request)
                    ?? CreateEntraCredential(request);
                client = new ServiceBusClient(fullyQualifiedNamespace, credential);
                if (request.Scope == ConnectionScope.Namespace)
                {
                    adminClient = new ServiceBusAdministrationClient(fullyQualifiedNamespace, credential);
                    probe = _probeOverride ?? new AdministrationClientConnectionProbe(adminClient);
                }
                else
                {
                    probe = _probeOverride ?? new NoOpConnectionProbe();
                }
            }
            else
            {
                return ConnectionCreateResult.Failed(
                    ConnectionFailureCategory.Validation,
                    "Only SAS and Entra authentication modes are supported.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var adminProbeSucceeded = false;
            if (request.Scope == ConnectionScope.Namespace)
            {
                try
                {
                    adminProbeSucceeded = await probe
                        .ProbeNamespaceAdminAsync(fullyQualifiedNamespace, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await DisposeClientsAsync(client, adminClient).ConfigureAwait(false);
                    var category = ServiceBusFailureTranslator.Classify(ex);
                    return ConnectionCreateResult.Failed(
                        category,
                        ServiceBusFailureTranslator.ToSafeMessage(ex));
                }
            }

            await probe
                .ProbeMessagingAsync(fullyQualifiedNamespace, request.EntityPath, cancellationToken)
                .ConfigureAwait(false);

            var capabilities = request.Scope == ConnectionScope.Namespace
                ? CapabilitySet.ForNamespaceScope(adminProbeSucceeded)
                : CapabilitySet.ForEntityScope();

            var handles = new ConnectionServiceHandles(client, adminClient);
            var context = LiveConnectionContext.Create(
                fullyQualifiedNamespace,
                request.Scope,
                capabilities,
                request.ProfileId,
                request.EntityPath,
                async () =>
                {
                    await DisposeClientsAsync(client, adminClient).ConfigureAwait(false);
                    if (ownsResolvedCredential)
                        resolvedCredential?.Dispose();
                });

            return ConnectionCreateResult.Succeeded(context, handles);
        }
        catch (OperationCanceledException)
        {
            if (ownsResolvedCredential)
                resolvedCredential?.Dispose();

            throw;
        }
        catch (Exception ex)
        {
            if (ownsResolvedCredential)
                resolvedCredential?.Dispose();

            return ConnectionCreateResult.Failed(
                ServiceBusFailureTranslator.Classify(ex),
                ServiceBusFailureTranslator.ToSafeMessage(ex));
        }
    }

    private async Task<(bool Success, ConnectionCreateResult? Result, SensitiveCredential? Credential, bool OwnsCredential)>
        ResolveSasCredentialAsync(
            ConnectionRequest request,
            CancellationToken cancellationToken)
    {
        if (request.SasCredential is not null)
            return (true, null, request.SasCredential, false);

        if (request.CredentialReference is null)
        {
            return (
                false,
                ConnectionCreateResult.Failed(
                    ConnectionFailureCategory.Validation,
                    "A SAS connection string is required."),
                null,
                false);
        }

        var retrieve = await _vault.RetrieveAsync(request.CredentialReference, cancellationToken)
            .ConfigureAwait(false);
        if (retrieve.Status == CredentialVaultStatus.Available && retrieve.Credential is not null)
            return (true, null, retrieve.Credential, true);

        return (
            false,
            ConnectionCreateResult.ManualSasRequired(
                request.CredentialReference,
                retrieve.RecoveryGuidance),
            null,
            false);
    }

    private static string? ValidateRequest(ConnectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NamespaceEndpoint))
            return "A Service Bus namespace endpoint is required.";

        var allowDevelopmentEmulatorHost =
            request.AuthMode == ServiceBusAuthMode.Sas &&
            HasDevelopmentEmulatorFlag(request.SasCredential);

        if (!TryNormalizeNamespace(request.NamespaceEndpoint, allowDevelopmentEmulatorHost, out _))
            return "The namespace endpoint must be a canonical Service Bus host without credentials.";

        if (request.AuthMode is not (ServiceBusAuthMode.Sas or ServiceBusAuthMode.AzureActiveDirectory))
            return "Only SAS and Entra authentication modes are supported.";

        if (request.Scope == ConnectionScope.Entity && string.IsNullOrWhiteSpace(request.EntityPath))
            return "Entity scope requires an entity path.";

        if (request.Scope == ConnectionScope.Namespace && !string.IsNullOrWhiteSpace(request.EntityPath))
            return "Namespace scope must not include an entity path.";

        if (request.AuthMode == ServiceBusAuthMode.AzureActiveDirectory &&
            request.EntraInteraction is null)
            return "Entra authentication requires an interaction mode.";

        if (request.AuthMode == ServiceBusAuthMode.Sas && request.EntraInteraction is not null)
            return "Entra interaction mode is only valid for Entra authentication.";

        return null;
    }

    private static string NormalizeNamespace(string endpoint) =>
        TryNormalizeNamespace(endpoint, allowDevelopmentEmulatorHost: true, out var normalized)
            ? normalized
            : endpoint.Trim();

    private static bool TryNormalizeNamespace(
        string endpoint,
        bool allowDevelopmentEmulatorHost,
        out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(endpoint))
            return false;

        var trimmed = endpoint.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (!string.IsNullOrEmpty(uri.UserInfo))
                return false;

            if (!IsAllowedNamespaceHost(uri.Host, allowDevelopmentEmulatorHost))
                return false;

            normalized = uri.Host;
            return true;
        }

        if (trimmed.StartsWith("sb://", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["sb://".Length..];

        trimmed = trimmed.TrimEnd('/');
        var host = trimmed;
        var portSeparator = trimmed.IndexOf(':');
        if (portSeparator >= 0)
            host = trimmed[..portSeparator];

        if (!IsAllowedNamespaceHost(host, allowDevelopmentEmulatorHost))
            return false;

        normalized = host;
        return true;
    }

    private static bool IsAllowedNamespaceHost(string host, bool allowDevelopmentEmulatorHost) =>
        host.EndsWith(".servicebus.windows.net", StringComparison.OrdinalIgnoreCase) ||
        IsWellKnownDevelopmentEmulatorHost(host) ||
        (allowDevelopmentEmulatorHost && !string.IsNullOrWhiteSpace(host));

    private static bool IsWellKnownDevelopmentEmulatorHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase);

    private static bool HasDevelopmentEmulatorFlag(SensitiveCredential? credential) =>
        credential is not null &&
        IsDevelopmentEmulatorConnectionString(credential.Reveal());

    private static bool IsDevelopmentEmulatorConnectionString(string connectionString)
    {
        foreach (var part in connectionString.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var segments = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (segments.Length == 2 &&
                segments[0].Equals("UseDevelopmentEmulator", StringComparison.OrdinalIgnoreCase) &&
                segments[1].Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rewrites emulator ports and strips <c>EntityPath</c> for namespace-scoped SAS clients.
    /// </summary>
    private static string PrepareSasConnectionString(
        string connectionString,
        ConnectionScope scope,
        bool forAdministration)
    {
        var prepared = forAdministration
            ? ResolveAdministrationConnectionString(connectionString)
            : ResolveMessagingConnectionString(connectionString);

        return scope == ConnectionScope.Namespace
            ? StripEntityPath(prepared)
            : prepared;
    }

    /// <summary>
    /// Messaging uses the emulator AMQP endpoint (no admin HTTP port).
    /// </summary>
    private static string ResolveMessagingConnectionString(string connectionString) =>
        IsDevelopmentEmulatorConnectionString(connectionString)
            ? RewriteEndpointPort(connectionString, port: null)
            : connectionString;

    /// <summary>
    /// Administration uses the emulator HTTP endpoint (default port 5300).
    /// </summary>
    private static string ResolveAdministrationConnectionString(string connectionString) =>
        IsDevelopmentEmulatorConnectionString(connectionString)
            ? RewriteEndpointPort(connectionString, EmulatorAdministrationPort)
            : connectionString;

    /// <summary>
    /// Removes <c>EntityPath</c> so a namespace-scoped client can target any queue/topic/subscription.
    /// </summary>
    internal static string StripEntityPath(string connectionString)
    {
        var parts = connectionString.Split(';');
        var kept = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator > 0)
            {
                var key = trimmed[..separator].Trim();
                if (key.Equals("EntityPath", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            kept.Add(trimmed);
        }

        return string.Join(';', kept);
    }

    private static string RewriteEndpointPort(string connectionString, int? port)
    {
        var parts = connectionString.Split(';');
        for (var i = 0; i < parts.Length; i++)
        {
            var trimmed = parts[i].Trim();
            if (trimmed.Length == 0)
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = trimmed[..separator].Trim();
            if (!key.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
                continue;

            var endpoint = trimmed[(separator + 1)..].Trim();
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                string.IsNullOrWhiteSpace(uri.Host))
            {
                break;
            }

            var rewritten = port is null
                ? $"sb://{uri.Host}/"
                : $"sb://{uri.Host}:{port.Value}/";
            parts[i] = $"Endpoint={rewritten}";
            break;
        }

        return string.Join(';', parts);
    }

    private static TokenCredential CreateEntraCredential(ConnectionRequest request) =>
        request.EntraInteraction switch
        {
            EntraInteractionMode.InteractiveBrowser => string.IsNullOrWhiteSpace(request.TenantId)
                ? new InteractiveBrowserCredential()
                : new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
                {
                    TenantId = request.TenantId,
                }),
            EntraInteractionMode.Default or null => string.IsNullOrWhiteSpace(request.TenantId)
                ? new DefaultAzureCredential()
                : new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    TenantId = request.TenantId,
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.EntraInteraction, null),
        };

    private static ValueTask DisposeClientsAsync(
        ServiceBusClient client,
        ServiceBusAdministrationClient? adminClient)
    {
        _ = adminClient;
        return client.DisposeAsync();
    }

    private sealed class NoOpConnectionProbe : IConnectionProbe
    {
        public Task<bool> ProbeNamespaceAdminAsync(
            string fullyQualifiedNamespace,
            CancellationToken cancellationToken = default)
        {
            _ = fullyQualifiedNamespace;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }

        public Task ProbeMessagingAsync(
            string fullyQualifiedNamespace,
            string? entityPath,
            CancellationToken cancellationToken = default)
        {
            _ = fullyQualifiedNamespace;
            _ = entityPath;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
