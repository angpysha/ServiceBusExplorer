#nullable enable
namespace ServiceBusExplorer;

public interface INamespaceService
{
    Task<bool> TestConnectionAsync(ConnectionOptions opts, CancellationToken ct = default);
    Task<string> GetNamespaceNameAsync(CancellationToken ct = default);
}
