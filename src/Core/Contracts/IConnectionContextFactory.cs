#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Creates async-disposable live connection contexts from validated connection requests.
/// </summary>
public interface IConnectionContextFactory
{
    Task<ConnectionCreateResult> CreateAsync(
        ConnectionRequest request,
        CancellationToken cancellationToken = default);
}
