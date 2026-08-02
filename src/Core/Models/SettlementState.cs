#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Settlement lifecycle for an observed or peek-locked message.
/// Only <see cref="Locked"/> is settleable.
/// </summary>
public enum SettlementState
{
    Peeked,
    Locked,
    Completed,
    Abandoned,
    Deferred,
    DeadLettered,
    LockLost,
    LockExpired,
    Ineligible
}

/// <summary>
/// Settlement action requested by the operator.
/// </summary>
public enum SettlementAction
{
    Complete,
    Abandon,
    Defer,
    DeadLetter
}

/// <summary>
/// Per-attempt settlement result kind. Confirmed successes are never auto-retried.
/// </summary>
public enum SettlementResultKind
{
    Succeeded,
    RejectedIneligible,
    Failed
}

/// <summary>
/// Typed outcome of a single settlement attempt.
/// </summary>
public sealed record SettlementItemOutcome(
    string MessageId,
    long SequenceNumber,
    SettlementAction Action,
    SettlementResultKind Result,
    SettlementState StateBefore,
    SettlementState StateAfter,
    string SafeMessage,
    string? LockToken = null);

/// <summary>
/// Aggregate outcome of a bulk settlement pass (one attempt per item).
/// </summary>
public sealed record SettlementBatchOutcome(IReadOnlyList<SettlementItemOutcome> Items)
{
    public int SucceededCount => Count(SettlementResultKind.Succeeded);

    public int RejectedCount => Count(SettlementResultKind.RejectedIneligible);

    public int FailedCount => Count(SettlementResultKind.Failed);

    public bool IsPartialSuccess =>
        SucceededCount > 0 && (RejectedCount > 0 || FailedCount > 0);

    public IReadOnlyList<SettlementItemOutcome> ConfirmedSuccesses =>
        Items.Where(static i => i.Result == SettlementResultKind.Succeeded).ToList();

    /// <summary>
    /// Items that were not confirmed successes and may be retried manually.
    /// Automatic whole-batch retry MUST exclude <see cref="ConfirmedSuccesses"/>.
    /// </summary>
    public IReadOnlyList<SettlementItemOutcome> RetryCandidates =>
        Items.Where(static i => i.Result != SettlementResultKind.Succeeded).ToList();

    private int Count(SettlementResultKind kind) => Items.Count(i => i.Result == kind);
}

/// <summary>
/// Pure settlement eligibility transitions. Terminal and peeked states never settle.
/// </summary>
public static class SettlementStateMachine
{
    public static bool CanSettle(SettlementState state) => state == SettlementState.Locked;

    public static bool IsTerminal(SettlementState state) =>
        state is SettlementState.Completed
            or SettlementState.Abandoned
            or SettlementState.Deferred
            or SettlementState.DeadLettered
            or SettlementState.LockLost
            or SettlementState.LockExpired
            or SettlementState.Ineligible
            or SettlementState.Peeked;

    public static SettlementState RefreshForClock(
        SettlementState state,
        DateTimeOffset? lockedUntil,
        DateTimeOffset utcNow)
    {
        if (state == SettlementState.Locked
            && lockedUntil is { } expiry
            && expiry <= utcNow)
        {
            return SettlementState.LockExpired;
        }

        return state;
    }

    public static SettlementState AfterSuccessfulAction(SettlementAction action) =>
        action switch
        {
            SettlementAction.Complete => SettlementState.Completed,
            SettlementAction.Abandon => SettlementState.Abandoned,
            SettlementAction.Defer => SettlementState.Deferred,
            SettlementAction.DeadLetter => SettlementState.DeadLettered,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

    public static string DescribeIneligibility(SettlementState state) =>
        state switch
        {
            SettlementState.Peeked => "Peeked messages cannot be settled.",
            SettlementState.LockExpired => "Message lock has expired.",
            SettlementState.LockLost => "Message lock was lost.",
            SettlementState.Completed => "Message was already completed.",
            SettlementState.Abandoned => "Message was already abandoned.",
            SettlementState.Deferred => "Message was already deferred.",
            SettlementState.DeadLettered => "Message was already dead-lettered.",
            SettlementState.Ineligible => "Message is not eligible for settlement.",
            SettlementState.Locked => "Message is eligible for settlement.",
            _ => "Message is not eligible for settlement."
        };
}

/// <summary>
/// Tracks per-lock settlement state for a receive session. Thread-affinity is caller-owned.
/// </summary>
public sealed class SettlementTracker
{
    private readonly Dictionary<string, TrackedLock> _locks = new(StringComparer.Ordinal);

    private readonly record struct TrackedLock(SettlementState State, DateTimeOffset? LockedUntil);

    public void Register(string lockToken, DateTimeOffset? lockedUntil)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockToken);
        _locks[lockToken] = new TrackedLock(SettlementState.Locked, lockedUntil);
    }

    public SettlementState GetState(string? lockToken, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(lockToken) || !_locks.TryGetValue(lockToken, out var tracked))
        {
            return SettlementState.Ineligible;
        }

        var refreshed = SettlementStateMachine.RefreshForClock(
            tracked.State,
            tracked.LockedUntil,
            utcNow);

        if (refreshed != tracked.State)
        {
            _locks[lockToken] = tracked with { State = refreshed };
        }

        return refreshed;
    }

    public bool TryGetLockedUntil(string lockToken, out DateTimeOffset? lockedUntil)
    {
        if (_locks.TryGetValue(lockToken, out var tracked))
        {
            lockedUntil = tracked.LockedUntil;
            return true;
        }

        lockedUntil = null;
        return false;
    }

    /// <summary>
    /// Begins a single settlement attempt. Returns a rejection outcome when ineligible;
    /// otherwise returns null and the caller may invoke the broker once.
    /// </summary>
    public SettlementItemOutcome? TryBegin(
        ReceivedMessage message,
        SettlementAction action,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(message.LockToken)
            || !_locks.TryGetValue(message.LockToken, out var tracked))
        {
            return Reject(
                message,
                action,
                SettlementState.Ineligible,
                SettlementState.Ineligible);
        }

        var before = SettlementStateMachine.RefreshForClock(
            tracked.State,
            tracked.LockedUntil,
            utcNow);

        if (before != tracked.State)
        {
            _locks[message.LockToken] = tracked with { State = before };
        }

        if (!SettlementStateMachine.CanSettle(before))
        {
            return Reject(message, action, before, before);
        }

        return null;
    }

    public SettlementItemOutcome MarkSucceeded(ReceivedMessage message, SettlementAction action)
    {
        ArgumentNullException.ThrowIfNull(message);
        var after = SettlementStateMachine.AfterSuccessfulAction(action);
        var before = SettlementState.Locked;

        if (!string.IsNullOrWhiteSpace(message.LockToken)
            && _locks.TryGetValue(message.LockToken, out var tracked))
        {
            before = tracked.State;
            _locks[message.LockToken] = tracked with { State = after };
        }

        return new SettlementItemOutcome(
            message.MessageId,
            message.SequenceNumber,
            action,
            SettlementResultKind.Succeeded,
            before,
            after,
            $"Settlement {action} succeeded.",
            message.LockToken);
    }

    public SettlementItemOutcome MarkLockLost(ReceivedMessage message, SettlementAction action)
    {
        ArgumentNullException.ThrowIfNull(message);
        var before = SettlementState.Locked;

        if (!string.IsNullOrWhiteSpace(message.LockToken)
            && _locks.TryGetValue(message.LockToken, out var tracked))
        {
            before = tracked.State;
            _locks[message.LockToken] = tracked with { State = SettlementState.LockLost };
        }

        return new SettlementItemOutcome(
            message.MessageId,
            message.SequenceNumber,
            action,
            SettlementResultKind.RejectedIneligible,
            before,
            SettlementState.LockLost,
            SettlementStateMachine.DescribeIneligibility(SettlementState.LockLost),
            message.LockToken);
    }

    public SettlementItemOutcome MarkFailed(
        ReceivedMessage message,
        SettlementAction action,
        string safeMessage)
    {
        ArgumentNullException.ThrowIfNull(message);
        var state = SettlementState.Locked;

        if (!string.IsNullOrWhiteSpace(message.LockToken)
            && _locks.TryGetValue(message.LockToken, out var tracked))
        {
            state = tracked.State;
        }

        return new SettlementItemOutcome(
            message.MessageId,
            message.SequenceNumber,
            action,
            SettlementResultKind.Failed,
            state,
            state,
            safeMessage,
            message.LockToken);
    }

    public static SettlementItemOutcome RejectPeeked(
        ObservedMessage message,
        SettlementAction action)
    {
        ArgumentNullException.ThrowIfNull(message);
        var state = message.SettlementState == SettlementState.Peeked
            || message.ReceiveKind == MessageReceiveKind.Peeked
            ? SettlementState.Peeked
            : message.SettlementState;

        return new SettlementItemOutcome(
            message.MessageId,
            message.SequenceNumber,
            action,
            SettlementResultKind.RejectedIneligible,
            state,
            state,
            SettlementStateMachine.DescribeIneligibility(state));
    }

    private static SettlementItemOutcome Reject(
        ReceivedMessage message,
        SettlementAction action,
        SettlementState before,
        SettlementState after) =>
        new(
            message.MessageId,
            message.SequenceNumber,
            action,
            SettlementResultKind.RejectedIneligible,
            before,
            after,
            SettlementStateMachine.DescribeIneligibility(after),
            message.LockToken);
}
