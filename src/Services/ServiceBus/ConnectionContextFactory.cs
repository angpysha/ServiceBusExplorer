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
                client = new ServiceBusClient(connectionString);
                if (request.Scope == ConnectionScope.Namespace)
                {
                    adminClient = new ServiceBusAdministrationClient(connectionString);
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

        if (!TryNormalizeNamespace(request.NamespaceEndpoint, out _))
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
        TryNormalizeNamespace(endpoint, out var normalized)
            ? normalized
            : endpoint.Trim();

    private static bool TryNormalizeNamespace(string endpoint, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(endpoint))
            return false;

        var trimmed = endpoint.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (!string.IsNullOrEmpty(uri.UserInfo))
                return false;

            var host = uri.Host;
            if (!host.EndsWith(".servicebus.windows.net", StringComparison.OrdinalIgnoreCase))
                return false;

            normalized = host;
            return true;
        }

        if (trimmed.StartsWith("sb://", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["sb://".Length..];

        trimmed = trimmed.TrimEnd('/');
        if (!trimmed.EndsWith(".servicebus.windows.net", StringComparison.OrdinalIgnoreCase))
            return false;

        normalized = trimmed;
        return true;
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
