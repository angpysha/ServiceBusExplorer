#nullable enable
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Azure SDK clients owned by a live connection context for application composition.
/// </summary>
public sealed class ConnectionServiceHandles
{
    public ConnectionServiceHandles(
        ServiceBusClient client,
        ServiceBusAdministrationClient? adminClient)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        AdminClient = adminClient;
    }

    public ServiceBusClient Client { get; }

    public ServiceBusAdministrationClient? AdminClient { get; }
}
