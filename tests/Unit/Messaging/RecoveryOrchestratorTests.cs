#nullable enable
using Microsoft.Extensions.Logging.Abstractions;
using ServiceBusExplorer.Services;
using Xunit;

namespace ServiceBusExplorer.UnitTests.Messaging;

public sealed class RecoveryOrchestratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecoverAsync_SendsReplacementBeforeSettlingOriginal_AndExcludesConfirmedSuccessFromRetry()
    {
        var events = new List<string>();
        var queue = new RecordingQueueService(events);
        queue.SendFailuresByMessageId["m3"] = new InvalidOperationException("send failed");

        var receiveSession = new RecordingReceiveSession(
            received: [
                CreateReceived("m1", 1),
                CreateReceived("m2", 2),
                CreateReceived("m3", 3)],
            events: events);
        receiveSession.CompleteFailuresByMessageId["m1"] = new InvalidOperationException("settle failed");

        var receiveService = new RecordingMessageReceiveService(receiveSession);

        var recovery = new RecoveryService(
            queue,
            receiveService,
            NullLogger<RecoveryService>.Instance);

        var request = new RecoveryRequest(
            SourceAddress: new EntityAddress("src/orders"),
            Source: MessageSource.DeadLetter,
            DestinationAddress: new EntityAddress("dst/orders"),
            DiagnosticPropertyTreatment.RetainAsCustom,
            SelectedMessages: [
                CreateSelected("m1", 1),
                CreateSelected("m2", 2),
                CreateSelected("m3", 3)]);

        var operation = await recovery.RecoverAsync(
            request,
            TestContext.Current.CancellationToken);

        // Ordering: settle for an item must only happen after replacement send for that same item.
        var sendM1 = IndexOf(events, "Send:m1");
        var settleM1 = IndexOf(events, "Settle:m1");
        Assert.NotEqual(-1, sendM1);
        Assert.NotEqual(-1, settleM1);
        Assert.True(sendM1 < settleM1);

        var sendM2 = IndexOf(events, "Send:m2");
        var settleM2 = IndexOf(events, "Settle:m2");
        Assert.NotEqual(-1, sendM2);
        Assert.NotEqual(-1, settleM2);
        Assert.True(sendM2 < settleM2);

        // If replacement send fails, the original must not be settled.
        Assert.DoesNotContain(events, e => e == "Settle:m3");

        // Per-item outcomes.
        var byId = operation.Items.ToDictionary(static i => i.MessageId);
        Assert.Equal(RecoveryItemResultKind.Uncertain, byId["m1"].Result);
        Assert.Equal(RecoveryItemResultKind.Succeeded, byId["m2"].Result);
        Assert.Equal(RecoveryItemResultKind.Failed, byId["m3"].Result);

        // Retry candidates exclude confirmed successes only.
        Assert.Equal(
            new[] { "m1", "m3" },
            operation.RetryRequest.Items.Select(i => i.MessageId).ToArray());
    }

    [Fact]
    public async Task RecoverAsync_DiagnosticPropertyTreatment_Remove_DropsDeadLetterDiagnosticKeys()
    {
        var events = new List<string>();
        var queue = new RecordingQueueService(events);

        var receiveSession = new RecordingReceiveSession(
            received: [CreateReceived("m1", 1)],
            events: events);

        var recovery = new RecoveryService(
            queue,
            new RecordingMessageReceiveService(receiveSession),
            NullLogger<RecoveryService>.Instance);

        var selectedProps = new Dictionary<string, object>
        {
            ["DeadLetterReason"] = "r",
            ["DeadLetterErrorDescription"] = "e",
            ["NServiceBus.Transport.Recovery"] = "nr",
            ["x"] = 1
        };

        var request = new RecoveryRequest(
            SourceAddress: new EntityAddress("src/orders"),
            Source: MessageSource.DeadLetter,
            DestinationAddress: new EntityAddress("dst/orders"),
            DiagnosticPropertyTreatment.Remove,
            SelectedMessages: [CreateSelected("m1", 1, selectedProps)]);

        var operation = await recovery.RecoverAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(RecoveryItemResultKind.Succeeded, operation.Items.Single().Result);

        var sent = queue.SentPropertiesByMessageId["m1"];
        Assert.NotNull(sent);
        Assert.DoesNotContain("DeadLetterReason", sent!.Keys);
        Assert.DoesNotContain("DeadLetterErrorDescription", sent!.Keys);
        Assert.DoesNotContain("NServiceBus.Transport.Recovery", sent!.Keys);
        Assert.Contains("x", sent.Keys);
    }

    [Fact]
    public async Task RecoverAsync_DiagnosticPropertyTreatment_RetainAsCustom_PreservesDeadLetterDiagnosticKeys()
    {
        var events = new List<string>();
        var queue = new RecordingQueueService(events);

        var receiveSession = new RecordingReceiveSession(
            received: [CreateReceived("m1", 1)],
            events: events);

        var recovery = new RecoveryService(
            queue,
            new RecordingMessageReceiveService(receiveSession),
            NullLogger<RecoveryService>.Instance);

        var selectedProps = new Dictionary<string, object>
        {
            ["DeadLetterReason"] = "r",
            ["DeadLetterErrorDescription"] = "e",
            ["NServiceBus.Transport.Recovery"] = "nr",
            ["x"] = 1
        };

        var request = new RecoveryRequest(
            SourceAddress: new EntityAddress("src/orders"),
            Source: MessageSource.DeadLetter,
            DestinationAddress: new EntityAddress("dst/orders"),
            DiagnosticPropertyTreatment.RetainAsCustom,
            SelectedMessages: [CreateSelected("m1", 1, selectedProps)]);

        var operation = await recovery.RecoverAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(RecoveryItemResultKind.Succeeded, operation.Items.Single().Result);

        var sent = queue.SentPropertiesByMessageId["m1"];
        Assert.NotNull(sent);
        Assert.Equal("r", sent!["DeadLetterReason"]);
        Assert.Equal("e", sent!["DeadLetterErrorDescription"]);
        Assert.Equal("nr", sent!["NServiceBus.Transport.Recovery"]);
        Assert.Equal(1, sent!["x"]);
    }

    private static int IndexOf(List<string> events, string value) =>
        events.FindIndex(e => e == value);

    private static ObservedMessage CreateSelected(
        string messageId,
        long sequenceNumber,
        IReadOnlyDictionary<string, object>? properties = null) =>
        new(
            messageId,
            MessageSource.DeadLetter,
            MessageReceiveKind.Locked,
            sequenceNumber,
            DeliveryCount: 1,
            EnqueuedAt: Now.AddMinutes(-10),
            ScheduledEnqueueTime: null,
            Body: new MessageBodyRepresentation(
                MessageBodyKind.Text,
                DisplayText: $"body-{messageId}",
                ContentType: "text/plain"),
            Properties: properties ?? new Dictionary<string, object>(),
            SessionId: null,
            CorrelationId: null,
            DeadLetterReason: "DL",
            SettlementState: SettlementState.Locked,
            LockedUntil: Now.AddMinutes(2),
            LockToken: $"lock-{messageId}");

    private static ReceivedMessage CreateReceived(string messageId, long sequenceNumber) =>
        new(
            messageId,
            Body: "original-body",
            ContentType: "text/plain",
            SequenceNumber: sequenceNumber,
            DeliveryCount: 1,
            EnqueuedAt: Now.AddMinutes(-10),
            ExpiresAt: null,
            CorrelationId: null,
            SessionId: null,
            Properties: new Dictionary<string, object>(),
            DeadLetterReason: "DL",
            LockToken: $"lock-{messageId}");

    private sealed class RecordingQueueService : IQueueService
    {
        private readonly List<string> _events;

        public RecordingQueueService(List<string> events) => _events = events;

        public Dictionary<string, Exception> SendFailuresByMessageId { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, IReadOnlyDictionary<string, object>?> SentPropertiesByMessageId { get; } = new(StringComparer.Ordinal);

        public Task SendAsync(string name, OutboundMessage message, CancellationToken ct = default)
        {
            _events.Add($"Send:{message.MessageId}");
            SentPropertiesByMessageId[message.MessageId ?? ""] = message.Properties;

            if (message.MessageId is not null &&
                SendFailuresByMessageId.TryGetValue(message.MessageId, out var ex))
            {
                throw ex;
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<QueueInfo>> ListAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<QueueInfo> GetAsync(string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<QueueInfo>> CreateAsync(
            CreateQueueOptions opts,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<QueueInfo>> UpdateAsync(
            QueueInfo updated,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<QueueInfo?>> DeleteAsync(
            string name,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReceivedMessage>> PeekAsync(
            string name,
            int maxCount,
            MessageSource source,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task PurgeAsync(
            string name,
            MessageSource source,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReceiveSession> OpenReceiveSessionAsync(
            string name,
            MessageSource source,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingMessageReceiveService : IMessageReceiveService
    {
        private readonly IReceiveSession _session;

        public RecordingMessageReceiveService(IReceiveSession session) => _session = session;

        public Task<IReceiveSession> OpenPeekLockAsync(
            EntityAddress address,
            MessageSource source,
            SessionRequest? sessionRequest = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_session);

        public Task<ReceiveAndDeleteResult> ReceiveAndDeleteAsync(
            ConfirmedReceiveAndDeleteRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public SettlementItemOutcome RejectPeekedSettlement(
            ObservedMessage message,
            SettlementAction action) =>
            throw new NotSupportedException();

        public Task<SettlementItemOutcome> CompleteAsync(
            IReceiveSession session,
            ReceivedMessage message,
            CancellationToken cancellationToken = default) =>
            session.CompleteAsync(message, cancellationToken);

        public Task<SettlementItemOutcome> AbandonAsync(
            IReceiveSession session,
            ReceivedMessage message,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SettlementItemOutcome> DeferAsync(
            IReceiveSession session,
            ReceivedMessage message,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SettlementItemOutcome> DeadLetterAsync(
            IReceiveSession session,
            ReceivedMessage message,
            string? reason = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SettlementBatchOutcome> SettleBatchAsync(
            IReceiveSession session,
            IReadOnlyList<ReceivedMessage> messages,
            SettlementAction action,
            string? deadLetterReason = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingReceiveSession : IReceiveSession
    {
        private readonly IReadOnlyList<ReceivedMessage> _received;
        private readonly List<string> _events;

        public RecordingReceiveSession(IReadOnlyList<ReceivedMessage> received, List<string> events)
        {
            _received = received;
            _events = events;
        }

        public Dictionary<string, Exception> CompleteFailuresByMessageId { get; } = new(StringComparer.Ordinal);

        public string EntityPath => "src/orders";

        public MessageSource Source => MessageSource.DeadLetter;

        public string? SessionId => null;

        public DateTimeOffset? SessionLockedUntil => null;

        public bool IsSessionReceiver => false;

        public bool IsSessionLockLost => false;

        public bool IsDisposed { get; private set; }

        public CancellationToken SessionAborted => CancellationToken.None;

        public Task<IReadOnlyList<ReceivedMessage>> ReceiveBatchAsync(
            int maxMessages = 20,
            TimeSpan? maxWait = null,
            CancellationToken ct = default) =>
            Task.FromResult(_received);

        public SettlementState GetSettlementState(ReceivedMessage message, DateTimeOffset? utcNow = null) =>
            SettlementState.Locked;

        public Task<SettlementItemOutcome> CompleteAsync(
            ReceivedMessage message,
            CancellationToken ct = default)
        {
            _events.Add($"Settle:{message.MessageId}");

            if (CompleteFailuresByMessageId.TryGetValue(message.MessageId!, out var ex))
            {
                throw ex;
            }

            var outcome = new SettlementItemOutcome(
                message.MessageId!,
                message.SequenceNumber,
                SettlementAction.Complete,
                SettlementResultKind.Succeeded,
                SettlementState.Locked,
                SettlementState.Completed,
                "Completed.",
                message.LockToken);

            return Task.FromResult(outcome);
        }

        public Task<SettlementItemOutcome> AbandonAsync(
            ReceivedMessage message,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SettlementItemOutcome> DeadLetterAsync(
            ReceivedMessage message,
            string? reason = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SettlementItemOutcome> DeferAsync(
            ReceivedMessage message,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> TryRenewSessionLockAsync(CancellationToken ct = default) =>
            Task.FromResult(false);

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

