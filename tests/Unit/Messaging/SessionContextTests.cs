#nullable enable
using Xunit;

namespace ServiceBusExplorer.UnitTests.Messaging;

public sealed class SessionContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly EntityAddress Orders = new("orders");
    private const MessageSource Source = MessageSource.Active;

    [Fact]
    public void BeginAcquisition_WithNextSessionRequest_EntersAcquiringState()
    {
        var context = SessionContext.BeginAcquisition(Orders, Source, new SessionRequest());

        Assert.Equal(SessionOwnershipState.Acquiring, context.OwnershipState);
        Assert.Null(context.RequestedSessionId);
        Assert.Null(context.AcceptedSessionId);
        Assert.False(context.IsLockVisible);
        Assert.False(context.CanOperateMessages(Now));
        Assert.Contains("next available", context.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BeginAcquisition_WithSpecificSessionRequest_PreservesRequestedId()
    {
        var context = SessionContext.BeginAcquisition(Orders, Source, new SessionRequest("session-a"));

        Assert.Equal(SessionOwnershipState.Acquiring, context.OwnershipState);
        Assert.Equal("session-a", context.RequestedSessionId);
        Assert.Contains("session-a", context.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarkOwned_FromAcquiring_ExposesAcceptedSessionAndVisibleLock()
    {
        var acquiring = SessionContext.BeginAcquisition(Orders, Source, new SessionRequest("session-a"));
        var owned = acquiring.MarkOwned("session-a", Now.AddMinutes(1));

        Assert.Equal(SessionOwnershipState.Owned, owned.OwnershipState);
        Assert.Equal("session-a", owned.AcceptedSessionId);
        Assert.Equal(Now.AddMinutes(1), owned.LockExpiresAt);
        Assert.True(owned.IsLockVisible);
        Assert.True(owned.CanOperateMessages(Now));
    }

    [Fact]
    public void RefreshAt_WhenLockExpired_TransitionsToLostAndDisablesWork()
    {
        var owned = SessionContext
            .BeginAcquisition(Orders, Source, new SessionRequest("session-a"))
            .MarkOwned("session-a", Now.AddSeconds(-1));

        var lost = owned.RefreshAt(Now);

        Assert.Equal(SessionOwnershipState.Lost, lost.OwnershipState);
        Assert.False(lost.CanOperateMessages(Now));
        Assert.True(lost.CanReacquire);
        Assert.Contains("expired", lost.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarkLost_DisablesUnsafeContinuation()
    {
        var owned = SessionContext
            .BeginAcquisition(Orders, Source, new SessionRequest())
            .MarkOwned("next-session", Now.AddMinutes(5));

        var lost = owned.MarkLost();

        Assert.Equal(SessionOwnershipState.Lost, lost.OwnershipState);
        Assert.False(lost.CanOperateMessages(Now));
        Assert.True(lost.CanReacquire);
        Assert.Contains("disabled", lost.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarkCancelled_DuringAcquisition_ReleasesSession()
    {
        var acquiring = SessionContext.BeginAcquisition(Orders, Source, new SessionRequest("session-a"));

        var released = acquiring.MarkCancelled();

        Assert.Equal(SessionOwnershipState.Released, released.OwnershipState);
        Assert.True(released.CanReacquire);
        Assert.False(released.CanOperateMessages(Now));
    }

    [Fact]
    public void Reacquire_AfterLoss_StartsNewAcquisition()
    {
        var lost = SessionContext
            .BeginAcquisition(Orders, Source, new SessionRequest("old"))
            .MarkOwned("old", Now.AddMinutes(1))
            .MarkLost();

        var reacquire = SessionContext.BeginAcquisition(Orders, Source, new SessionRequest("new"));

        Assert.Equal(SessionOwnershipState.Acquiring, reacquire.OwnershipState);
        Assert.Equal("new", reacquire.RequestedSessionId);
        Assert.NotEqual(lost.AcceptedSessionId, reacquire.AcceptedSessionId);
    }

    [Fact]
    public void SyncFromReceiveSession_WhenSessionLockLost_MarksContextLost()
    {
        var owned = SessionContext
            .BeginAcquisition(Orders, Source, new SessionRequest("session-a"))
            .MarkOwned("session-a", Now.AddMinutes(1));
        var session = new FakeSessionReceiver
        {
            SessionId = "session-a",
            SessionLockedUntil = Now.AddMinutes(1),
            IsSessionLockLost = true
        };

        var synced = owned.SyncFromReceiveSession(session, Now);

        Assert.Equal(SessionOwnershipState.Lost, synced.OwnershipState);
        Assert.False(synced.CanOperateMessages(Now));
    }

    [Fact]
    public void SyncFromReceiveSession_FromAcquiring_MarksOwnedWithBrokerLock()
    {
        var acquiring = SessionContext.BeginAcquisition(Orders, Source, new SessionRequest());
        var session = new FakeSessionReceiver
        {
            SessionId = "broker-session",
            SessionLockedUntil = Now.AddMinutes(2)
        };

        var synced = acquiring.SyncFromReceiveSession(session, Now);

        Assert.Equal(SessionOwnershipState.Owned, synced.OwnershipState);
        Assert.Equal("broker-session", synced.AcceptedSessionId);
        Assert.True(synced.IsLockVisible);
        Assert.True(synced.CanOperateMessages(Now));
    }

    [Fact]
    public void MarkRenewed_UpdatesVisibleLockExpiry()
    {
        var renewing = SessionContext
            .BeginAcquisition(Orders, Source, new SessionRequest("session-a"))
            .MarkOwned("session-a", Now.AddMinutes(1))
            .BeginRenewing();

        var renewed = renewing.MarkRenewed(Now.AddMinutes(3));

        Assert.Equal(SessionOwnershipState.Owned, renewed.OwnershipState);
        Assert.Equal(Now.AddMinutes(3), renewed.LockExpiresAt);
        Assert.True(renewed.IsLockVisible);
        Assert.True(renewed.CanOperateMessages(Now));
    }

    private sealed class FakeSessionReceiver : IReceiveSession
    {
        public string EntityPath => Orders.Path;
        public MessageSource Source => MessageSource.Active;
        public bool IsDisposed => false;
        public CancellationToken SessionAborted => CancellationToken.None;
        public string? SessionId { get; init; }
        public DateTimeOffset? SessionLockedUntil { get; init; }
        public bool IsSessionReceiver => SessionId is not null;
        public bool IsSessionLockLost { get; init; }

        public Task<IReadOnlyList<ReceivedMessage>> ReceiveBatchAsync(
            int maxMessages = 20,
            TimeSpan? maxWait = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReceivedMessage>>([]);

        public SettlementState GetSettlementState(ReceivedMessage message, DateTimeOffset? utcNow = null) =>
            SettlementState.Locked;

        public Task<SettlementItemOutcome> CompleteAsync(ReceivedMessage message, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SettlementItemOutcome> AbandonAsync(ReceivedMessage message, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SettlementItemOutcome> DeadLetterAsync(
            ReceivedMessage message,
            string? reason = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SettlementItemOutcome> DeferAsync(ReceivedMessage message, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> TryRenewSessionLockAsync(CancellationToken ct = default) =>
            Task.FromResult(false);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
