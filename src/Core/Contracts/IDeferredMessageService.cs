#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Request to retrieve a deferred message by explicit source and sequence number.
/// </summary>
public sealed record DeferredRetrievalRequest(
    EntityAddress Address,
    MessageSource Source,
    long SequenceNumber,
    CapabilitySet Capabilities);

/// <summary>
/// Typed deferred retrieval outcome. Confirmed successes are never silently retried.
/// </summary>
public enum DeferredRetrievalResultKind
{
    Succeeded,
    RejectedUnauthorized,
    RejectedUnsupportedSource,
    NotFound,
    Failed
}

/// <summary>
/// Result of a deferred message retrieval attempt.
/// </summary>
public sealed record DeferredRetrievalOutcome(
    DeferredRetrievalResultKind Result,
    ObservedMessage? Message,
    string SafeMessage);

/// <summary>
/// Retrieves deferred messages by sequence number from an explicit active source.
/// </summary>
public interface IDeferredMessageService
{
    /// <summary>
    /// Retrieves a deferred message by sequence number when authorization and source prerequisites pass.
    /// </summary>
    Task<DeferredRetrievalOutcome> RetrieveAsync(
        DeferredRetrievalRequest request,
        CancellationToken cancellationToken = default);
}
