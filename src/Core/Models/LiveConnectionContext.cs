#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Async-disposable runtime connection context. Azure client lifetime is owned by a Services dispose callback.
/// </summary>
public sealed class LiveConnectionContext : IAsyncDisposable
{
    private readonly Func<ValueTask>? _disposeAsync;
    private int _disposed;

    internal LiveConnectionContext(
        string namespaceEndpoint,
        ConnectionScope scope,
        CapabilitySet capabilities,
        string? profileId = null,
        string? entityPath = null,
        Func<ValueTask>? disposeAsync = null)
    {
        NamespaceEndpoint = namespaceEndpoint;
        Scope = scope;
        Capabilities = capabilities;
        ProfileId = profileId;
        EntityPath = entityPath;
        _disposeAsync = disposeAsync;
        State = ConnectionState.Connected;
    }

    /// <summary>
    /// Creates a connected context. Intended for Services factory use only.
    /// </summary>
    public static LiveConnectionContext Create(
        string namespaceEndpoint,
        ConnectionScope scope,
        CapabilitySet capabilities,
        string? profileId,
        string? entityPath,
        Func<ValueTask>? disposeAsync) =>
        new(namespaceEndpoint, scope, capabilities, profileId, entityPath, disposeAsync);

    public string NamespaceEndpoint { get; }

    public string? ProfileId { get; }

    public ConnectionScope Scope { get; }

    public string? EntityPath { get; }

    public CapabilitySet Capabilities { get; }

    public ConnectionState State { get; private set; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        State = ConnectionState.Cancelling;
        if (_disposeAsync is not null)
            await _disposeAsync().ConfigureAwait(false);

        State = ConnectionState.Disconnected;
    }
}
