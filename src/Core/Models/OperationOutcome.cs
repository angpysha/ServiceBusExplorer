#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// High-level result category for an application operation.
/// Distinguishes completion, cancellation, partial progress, and failure.
/// </summary>
public enum OperationOutcomeKind
{
    /// <summary>Operation finished successfully with no remaining uncertainty.</summary>
    Succeeded,

    /// <summary>Caller cancelled before any confirmed progress.</summary>
    Cancelled,

    /// <summary>
    /// Some work was confirmed, but the remainder is incomplete or uncertain
    /// (for example cancelled or failed mid-batch).
    /// </summary>
    Partial,

    /// <summary>Operation failed with no safe claim of full completion.</summary>
    Failed
}

/// <summary>
/// Guidance for whether a caller may manually continue remaining work.
/// Automatic whole-operation retries after confirmed destructive progress are never allowed.
/// </summary>
public enum OperationRetryGuidance
{
    /// <summary>No further action is appropriate.</summary>
    None,

    /// <summary>
    /// The operator may manually start a new attempt for remaining work only.
    /// Confirmed successes must not be repeated automatically.
    /// </summary>
    ManualRemainderOnly,

    /// <summary>Do not retry; surface the failure for investigation.</summary>
    DoNotRetry
}

/// <summary>
/// Secret-safe operation result: category, target context, retry guidance, and counts.
/// Must not contain credentials, tokens, message bodies, or raw SDK exception text.
/// </summary>
public sealed record OperationOutcome(
    OperationOutcomeKind Kind,
    string Operation,
    string Target,
    MessageSource? Source,
    string SafeMessage,
    OperationRetryGuidance RetryGuidance,
    long ConfirmedCount = 0,
    bool HasUncertainRemainder = false)
{
    /// <summary>
    /// Destructive orchestration MUST NEVER automatically retry the whole operation
    /// after any confirmed progress.
    /// </summary>
    public bool AllowsAutomaticWholeOperationRetry => false;

    public static OperationOutcome Succeeded(
        string operation,
        string target,
        MessageSource? source,
        long confirmedCount,
        string safeMessage) =>
        new(
            OperationOutcomeKind.Succeeded,
            operation,
            target,
            source,
            safeMessage,
            OperationRetryGuidance.None,
            confirmedCount,
            HasUncertainRemainder: false);

    public static OperationOutcome Cancelled(
        string operation,
        string target,
        MessageSource? source,
        string safeMessage,
        long confirmedCount = 0,
        bool hasUncertainRemainder = false) =>
        new(
            confirmedCount > 0 ? OperationOutcomeKind.Partial : OperationOutcomeKind.Cancelled,
            operation,
            target,
            source,
            safeMessage,
            confirmedCount > 0
                ? OperationRetryGuidance.ManualRemainderOnly
                : OperationRetryGuidance.None,
            confirmedCount,
            hasUncertainRemainder);

    public static OperationOutcome Failed(
        string operation,
        string target,
        MessageSource? source,
        string safeMessage,
        long confirmedCount = 0,
        bool hasUncertainRemainder = false) =>
        new(
            confirmedCount > 0 ? OperationOutcomeKind.Partial : OperationOutcomeKind.Failed,
            operation,
            target,
            source,
            safeMessage,
            confirmedCount > 0
                ? OperationRetryGuidance.ManualRemainderOnly
                : OperationRetryGuidance.DoNotRetry,
            confirmedCount,
            hasUncertainRemainder);
}
