#nullable enable
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceBusExplorer.Services;
using Xunit;

namespace ServiceBusExplorer.ContractTests.Messaging;

public sealed class MessageReceiveContractTests
{
    [Theory]
    [InlineData(MessageSource.Active, SubQueue.None)]
    [InlineData(MessageSource.DeadLetter, SubQueue.DeadLetter)]
    [InlineData(MessageSource.TransferDeadLetter, SubQueue.TransferDeadLetter)]
    public async Task OpenPeekLockAsync_MapsExplicitSourceExhaustively(
        MessageSource source,
        SubQueue expectedSubQueue)
    {
        var adapter = new RecordingReceiveAdapter();
        var service = CreateService(adapter);

        var session = await service.OpenPeekLockAsync(
            new EntityAddress("orders"),
            source);

        Assert.Equal("orders", session.EntityPath);
        Assert.Equal(source, session.Source);
        Assert.Equal(expectedSubQueue, adapter.LastPeekLockSubQueue);
        Assert.False(session.IsDisposed);
        Assert.False(session.SessionAborted.IsCancellationRequested);
    }

    [Fact]
    public async Task OpenPeekLockAsync_WithNextSessionRequest_AcceptsSessionReceiver()
    {
        var adapter = new RecordingReceiveAdapter();
        var service = CreateService(adapter);

        var session = await service.OpenPeekLockAsync(
            new EntityAddress("orders"),
            MessageSource.Active,
            new SessionRequest());

        Assert.True(session.IsSessionReceiver);
        Assert.Equal("next-session", session.SessionId);
        Assert.NotNull(session.SessionLockedUntil);
        Assert.Equal(1, adapter.PeekLockOpenCount);
        Assert.True(adapter.LastSessionRequestWasNext);
    }

    [Fact]
    public async Task OpenPeekLockAsync_WithSpecificSessionRequest_PassesSessionId()
    {
        var adapter = new RecordingReceiveAdapter();
        var service = CreateService(adapter);

        var session = await service.OpenPeekLockAsync(
            new EntityAddress("orders"),
            MessageSource.Active,
            new SessionRequest("session-a"));

        Assert.Equal("session-a", session.SessionId);
        Assert.Equal("session-a", adapter.LastRequestedSessionId);
        Assert.False(adapter.LastSessionRequestWasNext);
    }

    [Fact]
    public async Task OpenPeekLockAsync_WithoutSessionRequest_UsesNonSessionReceiver()
    {
        var adapter = new RecordingReceiveAdapter();
        var service = CreateService(adapter);

        var session = await service.OpenPeekLockAsync(
            new EntityAddress("orders"),
            MessageSource.Active);

        Assert.False(session.IsSessionReceiver);
        Assert.Null(session.SessionId);
        Assert.Null(adapter.LastRequestedSessionId);
    }

    [Fact]
    public async Task OpenPeekLockAsync_Dispose_CancelsSessionAndMarksDisposed()
    {
        var service = CreateService(new RecordingReceiveAdapter());
        var session = await service.OpenPeekLockAsync(
            new EntityAddress("orders"),
            MessageSource.Active);

        await session.DisposeAsync();

        Assert.True(session.IsDisposed);
        Assert.True(session.SessionAborted.IsCancellationRequested);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            session.ReceiveBatchAsync(1));
    }

    [Fact]
    public async Task OpenPeekLockAsync_HonoursCallerCancellationBeforeOpen()
    {
        var adapter = new RecordingReceiveAdapter();
        var service = CreateService(adapter);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.OpenPeekLockAsync(
                new EntityAddress("orders"),
                MessageSource.Active,
                cancellationToken: cts.Token));

        Assert.Equal(0, adapter.PeekLockOpenCount);
    }

    [Fact]
    public void ReceiveAndDeleteConfirmation_CannotBeCreatedFromCancelledResult()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ReceiveAndDeleteConfirmation.Create(
                ConfirmationResult.Cancelled,
                new EntityAddress("orders"),
                MessageSource.Active));

        Assert.False(ReceiveAndDeleteConfirmation.TryCreate(
            ConfirmationResult.Cancelled,
            new EntityAddress("orders"),
            MessageSource.DeadLetter,
            out var confirmation));
        Assert.Null(confirmation);
    }

    [Fact]
    public void ReceiveAndDeleteAsync_WithoutConfirmationEvidence_IsImpossibleAtTypeBoundary()
    {
        // Compile-time contract: ConfirmedReceiveAndDeleteRequest requires
        // ReceiveAndDeleteConfirmation, which itself requires ConfirmationResult.Confirmed.
        var confirmation = ReceiveAndDeleteConfirmation.Create(
            ConfirmationResult.Confirmed,
            new EntityAddress("orders"),
            MessageSource.Active);
        Assert.NotNull(confirmation);
    }

    [Fact]
    public async Task ReceiveAndDeleteAsync_WithConfirmedToken_DeletesAndReportsDisplayLoss()
    {
        var adapter = new RecordingReceiveAdapter
        {
            ReceiveAndDeleteMessages =
            [
                CreateMessage("m1", 1),
                CreateMessage("m2", 2)
            ]
        };
        var service = CreateService(adapter);
        var confirmation = ReceiveAndDeleteConfirmation.Create(
            ConfirmationResult.Confirmed,
            new EntityAddress("orders"),
            MessageSource.DeadLetter);

        var result = await service.ReceiveAndDeleteAsync(
            new ConfirmedReceiveAndDeleteRequest(confirmation, MaxMessages: 10));

        Assert.Equal(2, result.Messages.Count);
        Assert.True(result.ReportsDisplayLossRisk);
        Assert.Contains("Permanently removed", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SubQueue.DeadLetter, adapter.LastReceiveAndDeleteSubQueue);
        Assert.Equal("orders", adapter.LastReceiveAndDeletePath);
        Assert.Equal(1, adapter.ReceiveAndDeleteCallCount);
    }

    [Fact]
    public async Task ReceiveAndDeleteAsync_UsesExplicitSourceOnly_NoActiveDefault()
    {
        var adapter = new RecordingReceiveAdapter();
        var service = CreateService(adapter);
        var confirmation = ReceiveAndDeleteConfirmation.Create(
            ConfirmationResult.Confirmed,
            new EntityAddress("sales/Subscriptions/regional"),
            MessageSource.TransferDeadLetter);

        await service.ReceiveAndDeleteAsync(
            new ConfirmedReceiveAndDeleteRequest(confirmation, MaxMessages: 5));

        Assert.Equal(SubQueue.TransferDeadLetter, adapter.LastReceiveAndDeleteSubQueue);
        Assert.NotEqual(SubQueue.None, adapter.LastReceiveAndDeleteSubQueue);
    }

    [Fact]
    public async Task ReceiveAndDeleteAsync_HonoursCancellation()
    {
        var adapter = new RecordingReceiveAdapter { ThrowOnReceiveAndDelete = true };
        var service = CreateService(adapter);
        var confirmation = ReceiveAndDeleteConfirmation.Create(
            ConfirmationResult.Confirmed,
            new EntityAddress("orders"),
            MessageSource.Active);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ReceiveAndDeleteAsync(
                new ConfirmedReceiveAndDeleteRequest(confirmation, 5),
                cts.Token));
    }

    private static MessageReceiveService CreateService(IServiceBusReceiveAdapter adapter) =>
        new(adapter, NullLogger<MessageReceiveService>.Instance);

    private static ServiceBusReceivedMessage CreateMessage(string messageId, long sequenceNumber)
    {
        return ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(Encoding.UTF8.GetBytes("sample")),
            messageId: messageId,
            sequenceNumber: sequenceNumber,
            contentType: "text/plain",
            lockTokenGuid: Guid.NewGuid());
    }

    private sealed class RecordingReceiveAdapter : IServiceBusReceiveAdapter
    {
        public IReadOnlyList<ServiceBusReceivedMessage> ReceiveAndDeleteMessages { get; init; } = [];
        public bool ThrowOnReceiveAndDelete { get; init; }
        public int PeekLockOpenCount { get; private set; }
        public int ReceiveAndDeleteCallCount { get; private set; }
        public SubQueue LastPeekLockSubQueue { get; private set; }
        public SubQueue LastReceiveAndDeleteSubQueue { get; private set; }
        public string? LastReceiveAndDeletePath { get; private set; }
        public string? LastRequestedSessionId { get; private set; }
        public bool LastSessionRequestWasNext { get; private set; }

        public Task<IReceiveSession> OpenPeekLockAsync(
            string entityPath,
            SubQueue subQueue,
            MessageSource source,
            SessionRequest? sessionRequest = null,
            CancellationToken cancellationToken = default)
        {
            PeekLockOpenCount++;
            LastPeekLockSubQueue = subQueue;
            cancellationToken.ThrowIfCancellationRequested();

            if (sessionRequest is null)
            {
                LastRequestedSessionId = null;
                LastSessionRequestWasNext = false;
                return Task.FromResult<IReceiveSession>(new FakeReceiveSession(entityPath, source));
            }

            LastRequestedSessionId = sessionRequest.SessionId;
            LastSessionRequestWasNext = sessionRequest.SessionId is null;
            var sessionId = sessionRequest.SessionId ?? "next-session";
            return Task.FromResult<IReceiveSession>(
                new FakeReceiveSession(entityPath, source, sessionId, DateTimeOffset.UtcNow.AddMinutes(1)));
        }

        public Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAndDeleteAsync(
            string entityPath,
            SubQueue subQueue,
            int maxMessages,
            TimeSpan maxWait,
            CancellationToken cancellationToken)
        {
            ReceiveAndDeleteCallCount++;
            LastReceiveAndDeleteSubQueue = subQueue;
            LastReceiveAndDeletePath = entityPath;
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnReceiveAndDelete)
                cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ReceiveAndDeleteMessages);
        }

        public Task<ServiceBusReceivedMessage> ReceiveDeferredMessageAsync(
            string entityPath,
            SubQueue subQueue,
            long sequenceNumber,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeReceiveSession : IReceiveSession
    {
        private readonly CancellationTokenSource _abortCts = new();
        private int _disposed;

        public FakeReceiveSession(string entityPath, MessageSource source)
            : this(entityPath, source, sessionId: null, sessionLockedUntil: null)
        {
        }

        public FakeReceiveSession(
            string entityPath,
            MessageSource source,
            string? sessionId,
            DateTimeOffset? sessionLockedUntil)
        {
            EntityPath = entityPath;
            Source = source;
            SessionId = sessionId;
            SessionLockedUntil = sessionLockedUntil;
        }

        public string EntityPath { get; }
        public MessageSource Source { get; }
        public string? SessionId { get; }
        public DateTimeOffset? SessionLockedUntil { get; }
        public bool IsSessionReceiver => SessionId is not null;
        public bool IsSessionLockLost { get; private set; }
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
        public CancellationToken SessionAborted =>
            IsDisposed ? new CancellationToken(canceled: true) : _abortCts.Token;

        public Task<IReadOnlyList<ReceivedMessage>> ReceiveBatchAsync(
            int maxMessages = 20, TimeSpan? maxWait = null, CancellationToken ct = default)
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(FakeReceiveSession));
            return Task.FromResult<IReadOnlyList<ReceivedMessage>>([]);
        }

        public Task<SettlementItemOutcome> CompleteAsync(ReceivedMessage message, CancellationToken ct = default) =>
            Task.FromResult(Succeeded(message, SettlementAction.Complete));

        public Task<SettlementItemOutcome> AbandonAsync(ReceivedMessage message, CancellationToken ct = default) =>
            Task.FromResult(Succeeded(message, SettlementAction.Abandon));

        public Task<SettlementItemOutcome> DeadLetterAsync(
            ReceivedMessage message, string? reason = null, CancellationToken ct = default) =>
            Task.FromResult(Succeeded(message, SettlementAction.DeadLetter));

        public Task<SettlementItemOutcome> DeferAsync(ReceivedMessage message, CancellationToken ct = default) =>
            Task.FromResult(Succeeded(message, SettlementAction.Defer));

        public Task<bool> TryRenewSessionLockAsync(CancellationToken ct = default) =>
            Task.FromResult(IsSessionReceiver && !IsSessionLockLost);

        public SettlementState GetSettlementState(ReceivedMessage message, DateTimeOffset? utcNow = null) =>
            SettlementState.Locked;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return ValueTask.CompletedTask;
            _abortCts.Cancel();
            _abortCts.Dispose();
            return ValueTask.CompletedTask;
        }

        private static SettlementItemOutcome Succeeded(ReceivedMessage message, SettlementAction action) =>
            new(
                message.MessageId,
                message.SequenceNumber,
                action,
                SettlementResultKind.Succeeded,
                SettlementState.Locked,
                SettlementStateMachine.AfterSuccessfulAction(action),
                $"Settlement {action} succeeded.",
                message.LockToken);
    }
}
