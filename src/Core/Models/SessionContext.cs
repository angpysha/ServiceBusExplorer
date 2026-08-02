#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Session ownership lifecycle for session-enabled peek-lock receive.
/// Message operations require <see cref="SessionOwnershipState.Owned"/> with a valid lock.
/// </summary>
public enum SessionOwnershipState
{
    Acquiring,
    Owned,
    Renewing,
    Lost,
    Released,
    Faulted
}

/// <summary>
/// Tracks requested/accepted session identity, lock expiry, and ownership state.
/// Immutable transitions — each method returns a new instance.
/// </summary>
public sealed class SessionContext
{
    private SessionContext(
        EntityAddress entity,
        MessageSource source,
        string? requestedSessionId,
        string? acceptedSessionId,
        SessionOwnershipState ownershipState,
        DateTimeOffset? lockExpiresAt,
        string? statusMessage)
    {
        Entity = entity;
        Source = source;
        RequestedSessionId = requestedSessionId;
        AcceptedSessionId = acceptedSessionId;
        OwnershipState = ownershipState;
        LockExpiresAt = lockExpiresAt;
        StatusMessage = statusMessage;
    }

    public EntityAddress Entity { get; }

    public MessageSource Source { get; }

    /// <summary>Specific session id requested, or null when acquiring the next available session.</summary>
    public string? RequestedSessionId { get; }

    /// <summary>Broker-accepted session id after successful acquisition.</summary>
    public string? AcceptedSessionId { get; }

    public SessionOwnershipState OwnershipState { get; }

    /// <summary>Visible session lock expiry while <see cref="IsLockVisible"/> is true.</summary>
    public DateTimeOffset? LockExpiresAt { get; }

    /// <summary>Operator-safe status text for the current ownership state.</summary>
    public string? StatusMessage { get; }

    /// <summary>True when lock expiry is exposed for UI/programmatic session state.</summary>
    public bool IsLockVisible =>
        OwnershipState is SessionOwnershipState.Owned or SessionOwnershipState.Renewing
        && LockExpiresAt is not null;

    /// <summary>True when the operator may receive or settle session messages.</summary>
    public bool CanOperateMessages(DateTimeOffset utcNow) =>
        OwnershipState == SessionOwnershipState.Owned
        && LockExpiresAt is { } expiry
        && expiry > utcNow;

    /// <summary>True after loss, release, or fault — a new acquisition may be started.</summary>
    public bool CanReacquire =>
        OwnershipState is SessionOwnershipState.Lost
            or SessionOwnershipState.Released
            or SessionOwnershipState.Faulted;

    public static SessionContext BeginAcquisition(
        EntityAddress entity,
        MessageSource source,
        SessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(request);

        return new SessionContext(
            entity,
            source,
            request.SessionId,
            acceptedSessionId: null,
            SessionOwnershipState.Acquiring,
            lockExpiresAt: null,
            statusMessage: request.SessionId is null
                ? "Acquiring next available session…"
                : $"Acquiring session '{request.SessionId}'…");
    }

    public SessionContext MarkOwned(string acceptedSessionId, DateTimeOffset lockExpiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(acceptedSessionId);
        EnsureState(SessionOwnershipState.Acquiring);

        return new SessionContext(
            Entity,
            Source,
            RequestedSessionId,
            acceptedSessionId,
            SessionOwnershipState.Owned,
            lockExpiresAt,
            $"Session '{acceptedSessionId}' acquired.");
    }

    public SessionContext BeginRenewing()
    {
        EnsureState(SessionOwnershipState.Owned);

        return new SessionContext(
            Entity,
            Source,
            RequestedSessionId,
            AcceptedSessionId,
            SessionOwnershipState.Renewing,
            LockExpiresAt,
            $"Renewing session lock for '{AcceptedSessionId}'…");
    }

    public SessionContext MarkRenewed(DateTimeOffset lockExpiresAt)
    {
        EnsureState(SessionOwnershipState.Renewing);

        return new SessionContext(
            Entity,
            Source,
            RequestedSessionId,
            AcceptedSessionId,
            SessionOwnershipState.Owned,
            lockExpiresAt,
            $"Session '{AcceptedSessionId}' lock renewed.");
    }

    public SessionContext RefreshAt(DateTimeOffset utcNow)
    {
        if (OwnershipState != SessionOwnershipState.Owned
            || LockExpiresAt is not { } expiry
            || expiry > utcNow)
        {
            return this;
        }

        return MarkLost("Session lock expired.");
    }

    public SessionContext MarkLost(string? statusMessage = null) =>
        new(
            Entity,
            Source,
            RequestedSessionId,
            AcceptedSessionId,
            SessionOwnershipState.Lost,
            LockExpiresAt,
            statusMessage ?? "Session lock was lost. Further message actions are disabled.");

    public SessionContext MarkReleased() =>
        new(
            Entity,
            Source,
            RequestedSessionId,
            AcceptedSessionId,
            SessionOwnershipState.Released,
            lockExpiresAt: null,
            "Session released.");

    public SessionContext MarkFaulted(string safeMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);

        return new SessionContext(
            Entity,
            Source,
            RequestedSessionId,
            AcceptedSessionId,
            SessionOwnershipState.Faulted,
            lockExpiresAt: null,
            safeMessage);
    }

    public SessionContext MarkCancelled() =>
        OwnershipState == SessionOwnershipState.Acquiring
            ? MarkReleased()
            : this;

    /// <summary>
    /// Syncs lock expiry and loss from a live session receiver after broker interaction.
    /// </summary>
    public SessionContext SyncFromReceiveSession(IReceiveSession session, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.IsSessionLockLost)
        {
            return MarkLost();
        }

        var refreshed = RefreshAt(utcNow);
        if (refreshed.OwnershipState == SessionOwnershipState.Lost)
        {
            return refreshed;
        }

        if (!session.IsSessionReceiver
            || session.SessionId is not { } sessionId
            || session.SessionLockedUntil is not { } lockedUntil)
        {
            return refreshed;
        }

        if (OwnershipState is SessionOwnershipState.Acquiring)
        {
            return MarkOwned(sessionId, lockedUntil);
        }

        if (OwnershipState is SessionOwnershipState.Owned or SessionOwnershipState.Renewing)
        {
            return new SessionContext(
                Entity,
                Source,
                RequestedSessionId,
                sessionId,
                SessionOwnershipState.Owned,
                lockedUntil,
                $"Session '{sessionId}' lock active until {lockedUntil:O}.");
        }

        return refreshed;
    }

    private void EnsureState(SessionOwnershipState expected)
    {
        if (OwnershipState != expected)
        {
            throw new InvalidOperationException(
                $"Session context must be in {expected} state but was {OwnershipState}.");
        }
    }
}
