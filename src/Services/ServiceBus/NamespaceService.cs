using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

public class NamespaceService : INamespaceService
{
    private readonly ServiceBusAdministrationClient _admin;
    private readonly ILogger<NamespaceService> _log;

    public NamespaceService(ServiceBusAdministrationClient admin, ILogger<NamespaceService> log)
    {
        _admin = admin;
        _log = log;
    }

    public async Task<bool> TestConnectionAsync(ConnectionOptions opts, CancellationToken ct = default)
    {
        try
        {
            var client = new ServiceBusAdministrationClient(opts.ConnectionString);
            await client.GetNamespacePropertiesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Connection test failed");
            return false;
        }
    }

    public async Task<string> GetNamespaceNameAsync(CancellationToken ct = default)
    {
        var props = await _admin.GetNamespacePropertiesAsync(ct);
        return props.Value.Name;
    }
}
