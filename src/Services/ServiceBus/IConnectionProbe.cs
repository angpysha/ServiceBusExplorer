#nullable enable
using Azure.Messaging.ServiceBus.Administration;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Test seam for capability probes without contacting live Azure.
/// </summary>
public interface IConnectionProbe
{
    Task<bool> ProbeNamespaceAdminAsync(
        string fullyQualifiedNamespace,
        CancellationToken cancellationToken = default);

    Task ProbeMessagingAsync(
        string fullyQualifiedNamespace,
        string? entityPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Administration-client-backed probe used by the factory at runtime.
/// </summary>
public sealed class AdministrationClientConnectionProbe : IConnectionProbe
{
    private readonly ServiceBusAdministrationClient _adminClient;

    public AdministrationClientConnectionProbe(ServiceBusAdministrationClient adminClient) =>
        _adminClient = adminClient;

    public async Task<bool> ProbeNamespaceAdminAsync(
        string fullyQualifiedNamespace,
        CancellationToken cancellationToken = default)
    {
        _ = fullyQualifiedNamespace;
        await _adminClient.GetNamespacePropertiesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task ProbeMessagingAsync(
        string fullyQualifiedNamespace,
        string? entityPath,
        CancellationToken cancellationToken = default)
    {
        _ = fullyQualifiedNamespace;
        _ = entityPath;
        return Task.CompletedTask;
    }
}
