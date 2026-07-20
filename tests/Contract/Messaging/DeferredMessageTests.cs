#nullable enable
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceBusExplorer.Services;
using Xunit;

namespace ServiceBusExplorer.ContractTests.Messaging;

/// <summary>
/// Contract tests for explicit-source deferred message retrieval by sequence number.
/// </summary>
public sealed class DeferredMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly CapabilitySet Authorized = CapabilitySet.ForNamespaceScope(adminProbeSucceeded: true);

    [Fact]
    public async Task RetrieveAsync_ActiveSource_MapsToMainSubQueue()
    {
        var adapter = new RecordingDeferredAdapter();
        var service = CreateService(adapter);
        var request = CreateRequest(MessageSource.Active, sequenceNumber: 42);

        var outcome = await service.RetrieveAsync(request);

        Assert.Equal(DeferredRetrievalResultKind.Succeeded, outcome.Result);
        Assert.Equal(SubQueue.None, adapter.LastSubQueue);
        Assert.Equal("orders", adapter.LastEntityPath);
        Assert.Equal(42L, adapter.LastSequenceNumber);
    }

    [Theory]
    [InlineData(MessageSource.DeadLetter, SubQueue.DeadLetter)]
    [InlineData(MessageSource.TransferDeadLetter, SubQueue.TransferDeadLetter)]
    public async Task RetrieveAsync_NonActiveSource_IsRejectedWithoutBrokerCall(
        MessageSource source,
        SubQueue _)
    {
        var adapter = new RecordingDeferredAdapter();
        var service = CreateService(adapter);
        var request = CreateRequest(source, sequenceNumber: 7);

        var outcome = await service.RetrieveAsync(request);

        Assert.Equal(DeferredRetrievalResultKind.RejectedUnsupportedSource, outcome.Result);
        Assert.Null(outcome.Message);
        Assert.Contains("active", outcome.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, adapter.ReceiveDeferredCallCount);
    }

    [Fact]
    public async Task RetrieveAsync_LooksUpMessageBySequenceNumber()
    {
        var adapter = new RecordingDeferredAdapter
        {
            DeferredMessage = CreateSdkMessage("deferred-1", sequenceNumber: 9001)
        };
        var service = CreateService(adapter);

        var outcome = await service.RetrieveAsync(CreateRequest(MessageSource.Active, 9001));

        Assert.Equal(DeferredRetrievalResultKind.Succeeded, outcome.Result);
        Assert.NotNull(outcome.Message);
        Assert.Equal(9001L, outcome.Message.SequenceNumber);
        Assert.Equal("deferred-1", outcome.Message.MessageId);
        Assert.Equal(MessageSource.Active, outcome.Message.Source);
        Assert.Equal(MessageReceiveKind.Locked, outcome.Message.ReceiveKind);
        Assert.Equal(SettlementState.Locked, outcome.Message.SettlementState);
    }

    [Fact]
    public async Task RetrieveAsync_WhenUnauthorized_RejectsWithoutAdapterCall()
    {
        var adapter = new RecordingDeferredAdapter();
        var service = CreateService(adapter);
        var capabilities = CapabilitySet.ForEntityScope() with { CanRetrieveDeferredAndRecover = false };
        var request = new DeferredRetrievalRequest(
            new EntityAddress("orders"),
            MessageSource.Active,
            11,
            capabilities);

        var outcome = await service.RetrieveAsync(request);

        Assert.Equal(DeferredRetrievalResultKind.RejectedUnauthorized, outcome.Result);
        Assert.Null(outcome.Message);
        Assert.Contains("authorization", outcome.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, adapter.ReceiveDeferredCallCount);
    }

    [Fact]
    public async Task RetrieveAsync_ReturnedMessage_IsSettleableWhenLockIsCurrent()
    {
        var adapter = new RecordingDeferredAdapter
        {
            DeferredMessage = CreateSdkMessage(
                "deferred-lock",
                sequenceNumber: 3,
                lockedUntil: Now.AddMinutes(5))
        };
        var service = CreateService(adapter);

        var outcome = await service.RetrieveAsync(CreateRequest(MessageSource.Active, 3));

        Assert.Equal(DeferredRetrievalResultKind.Succeeded, outcome.Result);
        Assert.NotNull(outcome.Message);
        Assert.True(outcome.Message.IsSettleableAt(Now));
        Assert.NotNull(outcome.Message.LockToken);
    }

    [Fact]
    public async Task RetrieveAsync_ReturnedMessage_IsNotSettleableWhenLockExpired()
    {
        var adapter = new RecordingDeferredAdapter
        {
            DeferredMessage = CreateSdkMessage(
                "deferred-expired",
                sequenceNumber: 4,
                lockedUntil: Now.AddMinutes(-1))
        };
        var service = CreateService(adapter);

        var outcome = await service.RetrieveAsync(CreateRequest(MessageSource.Active, 4));

        Assert.Equal(DeferredRetrievalResultKind.Succeeded, outcome.Result);
        Assert.NotNull(outcome.Message);
        Assert.False(outcome.Message.IsSettleableAt(Now));
        Assert.Equal(SettlementState.LockExpired, outcome.Message.SettlementStateAt(Now));
    }

    [Fact]
    public async Task RetrieveAsync_HonoursCancellation()
    {
        var adapter = new RecordingDeferredAdapter { ThrowOnReceiveDeferred = true };
        var service = CreateService(adapter);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RetrieveAsync(CreateRequest(MessageSource.Active, 1), cts.Token));

        Assert.Equal(0, adapter.ReceiveDeferredCallCount);
    }

    [Fact]
    public async Task RetrieveAsync_HonoursCancellationBeforeBrokerCall()
    {
        var adapter = new RecordingDeferredAdapter();
        var service = CreateService(adapter);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RetrieveAsync(CreateRequest(MessageSource.Active, 1), cts.Token));
    }

    private static DeferredRetrievalRequest CreateRequest(MessageSource source, long sequenceNumber) =>
        new(new EntityAddress("orders"), source, sequenceNumber, Authorized);

    private static DeferredMessageService CreateService(IServiceBusReceiveAdapter adapter) =>
        new(adapter, NullLogger<DeferredMessageService>.Instance);

    private static ServiceBusReceivedMessage CreateSdkMessage(
        string messageId,
        long sequenceNumber,
        DateTimeOffset? lockedUntil = null)
    {
        return ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(Encoding.UTF8.GetBytes("deferred-body")),
            messageId: messageId,
            sequenceNumber: sequenceNumber,
            contentType: "text/plain",
            lockTokenGuid: Guid.NewGuid(),
            lockedUntil: lockedUntil ?? Now.AddMinutes(5));
    }

    private sealed class RecordingDeferredAdapter : IServiceBusReceiveAdapter
    {
        public ServiceBusReceivedMessage? DeferredMessage { get; init; }
        public bool ThrowOnReceiveDeferred { get; init; }
        public int ReceiveDeferredCallCount { get; private set; }
        public string? LastEntityPath { get; private set; }
        public SubQueue LastSubQueue { get; private set; }
        public long LastSequenceNumber { get; private set; }

        public Task<IReceiveSession> OpenPeekLockAsync(
            string entityPath,
            SubQueue subQueue,
            MessageSource source,
            SessionRequest? sessionRequest = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAndDeleteAsync(
            string entityPath,
            SubQueue subQueue,
            int maxMessages,
            TimeSpan maxWait,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ServiceBusReceivedMessage> ReceiveDeferredMessageAsync(
            string entityPath,
            SubQueue subQueue,
            long sequenceNumber,
            CancellationToken cancellationToken)
        {
            ReceiveDeferredCallCount++;
            LastEntityPath = entityPath;
            LastSubQueue = subQueue;
            LastSequenceNumber = sequenceNumber;
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnReceiveDeferred)
                cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(DeferredMessage ?? CreateSdkMessage("fallback", sequenceNumber));
        }
    }
}
