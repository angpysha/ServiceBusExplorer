#nullable enable
namespace ServiceBusExplorer;

public interface IRelayService
{
    Task<IReadOnlyList<RelayInfo>> ListAsync(CancellationToken ct = default);
    Task<RelayInfo> GetAsync(string name, CancellationToken ct = default);
}
