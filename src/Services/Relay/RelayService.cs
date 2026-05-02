using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.Services;

public class RelayService : IRelayService
{
    private readonly ILogger<RelayService> _log;

    public RelayService(ILogger<RelayService> log)
    {
        _log = log;
    }

    public Task<IReadOnlyList<RelayInfo>> ListAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Relay management requires Azure Resource Manager — not available via connection string");
        return Task.FromResult<IReadOnlyList<RelayInfo>>(Array.Empty<RelayInfo>());
    }

    public Task<RelayInfo> GetAsync(string name, CancellationToken ct = default)
    {
        throw new NotSupportedException(
            "Relay management requires Azure Resource Manager. Full CRUD support is planned for Phase 4.");
    }
}
