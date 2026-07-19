#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Bounded, cancellable purge of an explicitly selected message source.
/// Confirmation MUST complete outside this port before invocation.
/// </summary>
public interface IPurgeService
{
    /// <summary>
    /// Purges messages from <paramref name="target"/> using receive-and-delete on the
    /// exact <paramref name="source"/>. Reports confirmed removals and any uncertain remainder.
    /// Does not automatically retry the whole operation after partial progress.
    /// </summary>
    /// <param name="target">Entity path to purge.</param>
    /// <param name="source">Non-null explicit message source (Active, DeadLetter, or TransferDeadLetter).</param>
    /// <param name="cancellationToken">Observed between batches and passed to each receive.</param>
    Task<OperationOutcome> PurgeAsync(
        EntityAddress target,
        MessageSource source,
        CancellationToken cancellationToken = default);
}
