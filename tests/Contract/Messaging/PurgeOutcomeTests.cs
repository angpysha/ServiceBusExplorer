#nullable enable
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceBusExplorer.Services;
using Xunit;

namespace ServiceBusExplorer.ContractTests.Messaging;

/// <summary>
/// Contract tests for bounded, cancellable purge outcomes and operation-state reporting.
/// </summary>
public sealed class PurgeOutcomeTests
{
    [Theory]
    [InlineData(MessageSource.Active, SubQueue.None)]
    [InlineData(MessageSource.DeadLetter, SubQueue.DeadLetter)]
    [InlineData(MessageSource.TransferDeadLetter, SubQueue.TransferDeadLetter)]
    public async Task PurgeAsync_MapsExplicitSourceExhaustively(
        MessageSource source,
        SubQueue expectedSubQueue)
    {
        var adapter = new ScriptedReceiveAdapter { Batches = [[],] };
        var service = CreateService(adapter);

        var outcome = await service.PurgeAsync(new EntityAddress("orders"), source);

        Assert.Equal(expectedSubQueue, adapter.LastSubQueue);
        Assert.Equal("orders", adapter.LastPath);
        Assert.Equal(OperationOutcomeKind.Succeeded, outcome.Kind);
        Assert.Equal(source, outcome.Source);
        Assert.False(outcome.AllowsAutomaticWholeOperationRetry);
    }

    [Fact]
    public async Task PurgeAsync_UsesBoundedBatchSize_NeverExceedsMaxPerReceive()
    {
        var adapter = new ScriptedReceiveAdapter
        {
            Batches =
            [
                CreateMessages(100),
                CreateMessages(0)
            ]
        };
        var service = CreateService(adapter);

        await service.PurgeAsync(new EntityAddress("orders"), MessageSource.Active);

        Assert.All(adapter.RequestedMaxMessages, max => Assert.True(max <= PurgeService.MaxBatchCount));
        Assert.Equal(PurgeService.MaxBatchCount, adapter.RequestedMaxMessages[0]);
    }

    [Fact]
    public async Task PurgeAsync_WhenCancelledBetweenBatches_ReportsConfirmedAndUncertainRemainder()
    {
        using var cts = new CancellationTokenSource();
        var adapter = new ScriptedReceiveAdapter
        {
            Batches =
            [
                CreateMessages(40),
                CreateMessages(40)
            ],
            AfterBatch = call =>
            {
                if (call == 1)
                    cts.Cancel();
            }
        };
        var service = CreateService(adapter);

        var outcome = await service.PurgeAsync(
            new EntityAddress("orders"),
            MessageSource.DeadLetter,
            cts.Token);

        Assert.Equal(OperationOutcomeKind.Partial, outcome.Kind);
        Assert.Equal(40, outcome.ConfirmedCount);
        Assert.True(outcome.HasUncertainRemainder);
        Assert.Equal(OperationRetryGuidance.ManualRemainderOnly, outcome.RetryGuidance);
        Assert.False(outcome.AllowsAutomaticWholeOperationRetry);
        Assert.Contains("40", outcome.SafeMessage, StringComparison.Ordinal);
        Assert.Contains("uncertain", outcome.SafeMessage, StringComparison.OrdinalIgnoreCase);
        // Second batch must not run after cancellation between batches.
        Assert.Equal(1, adapter.CallCount);
    }

    [Fact]
    public async Task PurgeAsync_WhenCancelledBeforeAnyProgress_ReportsCancelled()
    {
        var adapter = new ScriptedReceiveAdapter();
        var service = CreateService(adapter);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var outcome = await service.PurgeAsync(
            new EntityAddress("orders"),
            MessageSource.Active,
            cts.Token);

        Assert.Equal(OperationOutcomeKind.Cancelled, outcome.Kind);
        Assert.Equal(0, outcome.ConfirmedCount);
        Assert.False(outcome.HasUncertainRemainder);
        Assert.Equal(0, adapter.CallCount);
        Assert.False(outcome.AllowsAutomaticWholeOperationRetry);
    }

    [Fact]
    public async Task PurgeAsync_WhenFailureAfterPartialProgress_DoesNotRetryWholeOperation()
    {
        var adapter = new ScriptedReceiveAdapter
        {
            Batches =
            [
                CreateMessages(25),
                CreateMessages(25)
            ],
            ThrowOnCall = 2
        };
        var service = CreateService(adapter);

        var outcome = await service.PurgeAsync(
            new EntityAddress("orders"),
            MessageSource.TransferDeadLetter);

        Assert.Equal(OperationOutcomeKind.Partial, outcome.Kind);
        Assert.Equal(25, outcome.ConfirmedCount);
        Assert.True(outcome.HasUncertainRemainder);
        Assert.Equal(OperationRetryGuidance.ManualRemainderOnly, outcome.RetryGuidance);
        Assert.False(outcome.AllowsAutomaticWholeOperationRetry);
        // Exactly one successful batch + one failing attempt — no whole-operation restart.
        Assert.Equal(2, adapter.CallCount);
    }

    [Fact]
    public async Task PurgeAsync_WhenEmpty_ReportsSucceededWithZeroConfirmed()
    {
        var adapter = new ScriptedReceiveAdapter { Batches = [[],] };
        var service = CreateService(adapter);

        var outcome = await service.PurgeAsync(new EntityAddress("orders"), MessageSource.Active);

        Assert.Equal(OperationOutcomeKind.Succeeded, outcome.Kind);
        Assert.Equal(0, outcome.ConfirmedCount);
        Assert.False(outcome.HasUncertainRemainder);
        Assert.Equal(OperationRetryGuidance.None, outcome.RetryGuidance);
    }

    [Fact]
    public async Task PurgeAsync_DrainsAllBatches_ThenSucceedsWithConfirmedCount()
    {
        var adapter = new ScriptedReceiveAdapter
        {
            Batches =
            [
                CreateMessages(100),
                CreateMessages(15),
                CreateMessages(0)
            ]
        };
        var service = CreateService(adapter);

        var outcome = await service.PurgeAsync(new EntityAddress("orders"), MessageSource.Active);

        Assert.Equal(OperationOutcomeKind.Succeeded, outcome.Kind);
        Assert.Equal(115, outcome.ConfirmedCount);
        Assert.False(outcome.HasUncertainRemainder);
        Assert.Equal(3, adapter.CallCount);
        Assert.False(outcome.AllowsAutomaticWholeOperationRetry);
    }

    [Fact]
    public void OperationOutcome_NeverAllowsAutomaticWholeOperationRetry()
    {
        var outcomes = new[]
        {
            OperationOutcome.Succeeded("Purge", "orders", MessageSource.Active, 10, "ok"),
            OperationOutcome.Cancelled("Purge", "orders", MessageSource.Active, "cancelled"),
            OperationOutcome.Cancelled("Purge", "orders", MessageSource.Active, "partial cancel", 5, true),
            OperationOutcome.Failed("Purge", "orders", MessageSource.Active, "failed", 3, true),
            OperationOutcome.Failed("Purge", "orders", MessageSource.Active, "failed")
        };

        Assert.All(outcomes, o => Assert.False(o.AllowsAutomaticWholeOperationRetry));
        Assert.Equal(OperationOutcomeKind.Partial, outcomes[2].Kind);
        Assert.Equal(OperationOutcomeKind.Partial, outcomes[3].Kind);
        Assert.Equal(OperationOutcomeKind.Failed, outcomes[4].Kind);
        Assert.Equal(OperationOutcomeKind.Cancelled, outcomes[1].Kind);
    }

    private static PurgeService CreateService(IServiceBusReceiveAdapter adapter) =>
        new(adapter, NullLogger<PurgeService>.Instance);

    private static IReadOnlyList<ServiceBusReceivedMessage> CreateMessages(int count)
    {
        if (count == 0)
            return [];

        var list = new List<ServiceBusReceivedMessage>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: new BinaryData(Encoding.UTF8.GetBytes("x")),
                messageId: $"m{i}",
                sequenceNumber: i + 1,
                lockTokenGuid: Guid.NewGuid()));
        }

        return list;
    }

    private sealed class ScriptedReceiveAdapter : IServiceBusReceiveAdapter
    {
        private int _index;

        public IReadOnlyList<IReadOnlyList<ServiceBusReceivedMessage>> Batches { get; init; } = [];
        public Action<int>? AfterBatch { get; init; }
        public int? ThrowOnCall { get; init; }
        public int CallCount { get; private set; }
        public SubQueue LastSubQueue { get; private set; }
        public string? LastPath { get; private set; }
        public List<int> RequestedMaxMessages { get; } = [];

        public Task<IReceiveSession> OpenPeekLockAsync(
            string entityPath,
            SubQueue subQueue,
            MessageSource source,
            SessionRequest? sessionRequest = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReceiveSession>(new NotSupportedException());

        public Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveAndDeleteAsync(
            string entityPath,
            SubQueue subQueue,
            int maxMessages,
            TimeSpan maxWait,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastPath = entityPath;
            LastSubQueue = subQueue;
            RequestedMaxMessages.Add(maxMessages);
            cancellationToken.ThrowIfCancellationRequested();

            if (ThrowOnCall == CallCount)
                throw new InvalidOperationException("Simulated transport failure.");

            IReadOnlyList<ServiceBusReceivedMessage> batch =
                _index < Batches.Count ? Batches[_index++] : [];

            AfterBatch?.Invoke(CallCount);
            return Task.FromResult(batch);
        }

        public Task<ServiceBusReceivedMessage> ReceiveDeferredMessageAsync(
            string entityPath,
            SubQueue subQueue,
            long sequenceNumber,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
