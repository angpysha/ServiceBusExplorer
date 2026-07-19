#nullable enable
using Xunit;

namespace ServiceBusExplorer.UnitTests.Messaging;

public sealed class SettlementEligibilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PeekedObservedMessage_IsNeverSettleable()
    {
        var peeked = CreateObserved(
            MessageReceiveKind.Peeked,
            SettlementState.Peeked,
            lockedUntil: Now.AddMinutes(5));

        Assert.False(peeked.IsSettleableAt(Now));

        var rejection = SettlementTracker.RejectPeeked(peeked, SettlementAction.Complete);
        Assert.Equal(SettlementResultKind.RejectedIneligible, rejection.Result);
        Assert.Equal(SettlementState.Peeked, rejection.StateAfter);
        Assert.Contains("Peeked", rejection.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(SettlementAction.Complete, SettlementState.Completed)]
    [InlineData(SettlementAction.Abandon, SettlementState.Abandoned)]
    [InlineData(SettlementAction.Defer, SettlementState.Deferred)]
    [InlineData(SettlementAction.DeadLetter, SettlementState.DeadLettered)]
    public void LockedMessage_SuccessfulSettlement_BecomesTerminalAndCannotSettleTwice(
        SettlementAction action,
        SettlementState expectedTerminal)
    {
        var tracker = new SettlementTracker();
        var message = CreateReceived("m1", 1, "lock-1");
        tracker.Register(message.LockToken!, Now.AddMinutes(5));

        Assert.Null(tracker.TryBegin(message, action, Now));
        var success = tracker.MarkSucceeded(message, action);

        Assert.Equal(SettlementResultKind.Succeeded, success.Result);
        Assert.Equal(expectedTerminal, success.StateAfter);
        Assert.Equal(expectedTerminal, tracker.GetState(message.LockToken, Now));

        var second = tracker.TryBegin(message, action, Now);
        Assert.NotNull(second);
        Assert.Equal(SettlementResultKind.RejectedIneligible, second!.Result);
        Assert.Equal(expectedTerminal, second.StateAfter);
    }

    [Fact]
    public void LockExpired_RejectsSettlementWithoutBrokerAttempt()
    {
        var tracker = new SettlementTracker();
        var message = CreateReceived("m1", 1, "lock-expired");
        tracker.Register(message.LockToken!, Now.AddMinutes(-1));

        var rejection = tracker.TryBegin(message, SettlementAction.Complete, Now);
        Assert.NotNull(rejection);
        Assert.Equal(SettlementResultKind.RejectedIneligible, rejection!.Result);
        Assert.Equal(SettlementState.LockExpired, rejection.StateAfter);
        Assert.Equal(SettlementState.LockExpired, tracker.GetState(message.LockToken, Now));
    }

    [Fact]
    public void LockLost_RejectsFurtherSettlement()
    {
        var tracker = new SettlementTracker();
        var message = CreateReceived("m1", 1, "lock-lost");
        tracker.Register(message.LockToken!, Now.AddMinutes(5));

        var lost = tracker.MarkLockLost(message, SettlementAction.Abandon);
        Assert.Equal(SettlementState.LockLost, lost.StateAfter);

        var again = tracker.TryBegin(message, SettlementAction.Complete, Now);
        Assert.NotNull(again);
        Assert.Equal(SettlementResultKind.RejectedIneligible, again!.Result);
        Assert.Equal(SettlementState.LockLost, again.StateAfter);
    }

    [Fact]
    public void UnknownOrMissingLockToken_IsIneligible()
    {
        var tracker = new SettlementTracker();
        var message = CreateReceived("m1", 1, lockToken: null);

        var rejection = tracker.TryBegin(message, SettlementAction.Defer, Now);
        Assert.NotNull(rejection);
        Assert.Equal(SettlementState.Ineligible, rejection!.StateAfter);
    }

    [Fact]
    public void FailedAttempt_RemainsLocked_ForManualRetry_ButIsNotAutoSuccess()
    {
        var tracker = new SettlementTracker();
        var message = CreateReceived("m1", 1, "lock-retry");
        tracker.Register(message.LockToken!, Now.AddMinutes(5));

        var failed = tracker.MarkFailed(message, SettlementAction.Complete, "Transient broker failure.");
        Assert.Equal(SettlementResultKind.Failed, failed.Result);
        Assert.Equal(SettlementState.Locked, tracker.GetState(message.LockToken, Now));

        Assert.Null(tracker.TryBegin(message, SettlementAction.Complete, Now));
    }

    [Fact]
    public void BulkSettlement_ReportsPartialOutcome_AndExcludesSuccessesFromRetryCandidates()
    {
        var tracker = new SettlementTracker();
        var eligible = CreateReceived("ok", 1, "lock-ok");
        var peekedAsReceived = CreateReceived("peek", 2, lockToken: null);
        var expired = CreateReceived("exp", 3, "lock-exp");

        tracker.Register(eligible.LockToken!, Now.AddMinutes(5));
        tracker.Register(expired.LockToken!, Now.AddMinutes(-2));

        var outcomes = new List<SettlementItemOutcome>();

        foreach (var message in new[] { eligible, peekedAsReceived, expired })
        {
            var rejection = tracker.TryBegin(message, SettlementAction.Complete, Now);
            if (rejection is not null)
            {
                outcomes.Add(rejection);
                continue;
            }

            outcomes.Add(tracker.MarkSucceeded(message, SettlementAction.Complete));
        }

        var batch = new SettlementBatchOutcome(outcomes);
        Assert.Equal(1, batch.SucceededCount);
        Assert.Equal(2, batch.RejectedCount);
        Assert.True(batch.IsPartialSuccess);
        Assert.Single(batch.ConfirmedSuccesses);
        Assert.Equal(2, batch.RetryCandidates.Count);
        Assert.DoesNotContain(
            batch.RetryCandidates,
            o => o.Result == SettlementResultKind.Succeeded);

        // Confirmed success must not be settleable again (no automatic repeat).
        var repeat = tracker.TryBegin(eligible, SettlementAction.Complete, Now);
        Assert.NotNull(repeat);
        Assert.Equal(SettlementResultKind.RejectedIneligible, repeat!.Result);
    }

    [Fact]
    public void LockedObservedMessage_IsSettleableUntilExpiry()
    {
        var locked = CreateObserved(
            MessageReceiveKind.Locked,
            SettlementState.Locked,
            lockedUntil: Now.AddMinutes(2),
            lockToken: "tok");

        Assert.True(locked.IsSettleableAt(Now));
        Assert.False(locked.IsSettleableAt(Now.AddMinutes(3)));
    }

    [Theory]
    [InlineData(SettlementState.Completed)]
    [InlineData(SettlementState.Abandoned)]
    [InlineData(SettlementState.Deferred)]
    [InlineData(SettlementState.DeadLettered)]
    [InlineData(SettlementState.LockLost)]
    [InlineData(SettlementState.LockExpired)]
    [InlineData(SettlementState.Ineligible)]
    public void TerminalObservedStates_AreNotSettleable(SettlementState state)
    {
        var message = CreateObserved(
            MessageReceiveKind.Locked,
            state,
            lockedUntil: Now.AddMinutes(5),
            lockToken: "tok");

        Assert.False(message.IsSettleableAt(Now));
        Assert.True(SettlementStateMachine.IsTerminal(state));
    }

    [Fact]
    public async Task MessageReceiveService_SettleBatchAsync_SkipsAlreadySettledSuccesses()
    {
        var session = new TrackingReceiveSession();
        var first = CreateReceived("a", 1, "lock-a");
        var second = CreateReceived("b", 2, "lock-b");
        session.Register(first);
        session.Register(second);

        var service = new Services.MessageReceiveService(
            new UnusedReceiveAdapter(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Services.MessageReceiveService>.Instance);

        var firstPass = await service.SettleBatchAsync(
            session,
            [first, second],
            SettlementAction.Complete);

        Assert.Equal(2, firstPass.SucceededCount);

        var secondPass = await service.SettleBatchAsync(
            session,
            [first, second],
            SettlementAction.Complete);

        Assert.Equal(0, secondPass.SucceededCount);
        Assert.Equal(2, secondPass.RejectedCount);
        Assert.All(
            secondPass.Items,
            item => Assert.Equal(SettlementState.Completed, item.StateAfter));
    }

    private sealed class UnusedReceiveAdapter : Services.IServiceBusReceiveAdapter
    {
        public IReceiveSession OpenPeekLock(string entityPath, Azure.Messaging.ServiceBus.SubQueue subQueue, MessageSource source) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Azure.Messaging.ServiceBus.ServiceBusReceivedMessage>> ReceiveAndDeleteAsync(
            string entityPath,
            Azure.Messaging.ServiceBus.SubQueue subQueue,
            int maxMessages,
            TimeSpan maxWait,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TrackingReceiveSession : IReceiveSession
    {
        private readonly SettlementTracker _tracker = new();

        public string EntityPath => "orders";
        public MessageSource Source => MessageSource.Active;
        public bool IsDisposed => false;
        public CancellationToken SessionAborted => CancellationToken.None;

        public void Register(ReceivedMessage message) =>
            _tracker.Register(message.LockToken!, Now.AddMinutes(5));

        public Task<IReadOnlyList<ReceivedMessage>> ReceiveBatchAsync(
            int maxMessages = 20, TimeSpan? maxWait = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReceivedMessage>>([]);

        public SettlementState GetSettlementState(ReceivedMessage message, DateTimeOffset? utcNow = null) =>
            _tracker.GetState(message.LockToken, utcNow ?? Now);

        public Task<SettlementItemOutcome> CompleteAsync(ReceivedMessage message, CancellationToken ct = default) =>
            Settle(message, SettlementAction.Complete);

        public Task<SettlementItemOutcome> AbandonAsync(ReceivedMessage message, CancellationToken ct = default) =>
            Settle(message, SettlementAction.Abandon);

        public Task<SettlementItemOutcome> DeadLetterAsync(
            ReceivedMessage message, string? reason = null, CancellationToken ct = default) =>
            Settle(message, SettlementAction.DeadLetter);

        public Task<SettlementItemOutcome> DeferAsync(ReceivedMessage message, CancellationToken ct = default) =>
            Settle(message, SettlementAction.Defer);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private Task<SettlementItemOutcome> Settle(ReceivedMessage message, SettlementAction action)
        {
            var rejection = _tracker.TryBegin(message, action, Now);
            if (rejection is not null)
                return Task.FromResult(rejection);
            return Task.FromResult(_tracker.MarkSucceeded(message, action));
        }
    }

    private static ObservedMessage CreateObserved(
        MessageReceiveKind receiveKind,
        SettlementState settlementState,
        DateTimeOffset? lockedUntil = null,
        string? lockToken = null) =>
        new(
            "msg",
            MessageSource.Active,
            receiveKind,
            SequenceNumber: 1,
            DeliveryCount: 1,
            EnqueuedAt: Now.AddMinutes(-10),
            ScheduledEnqueueTime: null,
            Body: new MessageBodyRepresentation(MessageBodyKind.Text, "body"),
            Properties: new Dictionary<string, object>(),
            SessionId: null,
            SettlementState: settlementState,
            LockedUntil: lockedUntil,
            LockToken: lockToken);

    private static ReceivedMessage CreateReceived(string id, long sequence, string? lockToken) =>
        new(
            id,
            Body: "body",
            ContentType: "text/plain",
            SequenceNumber: sequence,
            DeliveryCount: 1,
            EnqueuedAt: Now.AddMinutes(-5),
            ExpiresAt: null,
            CorrelationId: null,
            SessionId: null,
            Properties: new Dictionary<string, object>(),
            LockToken: lockToken);
}
