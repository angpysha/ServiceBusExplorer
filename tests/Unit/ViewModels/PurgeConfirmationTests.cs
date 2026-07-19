using System.Reactive.Threading.Tasks;
using ServiceBusExplorer.ViewModels;
using Xunit;

namespace ServiceBusExplorer.UnitTests.ViewModels;

public class PurgeConfirmationTests
{
    [Fact]
    public async Task Purge_WithNoSelectedSource_DoesNotCallService()
    {
        var purge = new RecordingPurgeService();
        var browse = new NoOpBrowseService();
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Confirmed);
        var viewModel = new QueueDetailViewModel(
            new StubQueueService(),
            browse,
            new StubMessageSendService(),
            new StubMessageReceiveService(),
            purge,
            confirmation,
            "orders");

        await viewModel.PurgeCommand.Execute().ToTask();

        Assert.Empty(purge.Calls);
        Assert.Empty(confirmation.Requests);
        Assert.True(viewModel.IsPurgeFailed);
    }

    [Fact]
    public async Task Purge_WhenCancelled_DoesNotCallService()
    {
        var purge = new RecordingPurgeService();
        var browse = new NoOpBrowseService();
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Cancelled);
        var viewModel = new QueueDetailViewModel(
            new StubQueueService(),
            browse,
            new StubMessageSendService(),
            new StubMessageReceiveService(),
            purge,
            confirmation,
            "orders")
        {
            SelectedSource = MessageSource.DeadLetter
        };

        await viewModel.PurgeCommand.Execute().ToTask();

        Assert.Empty(purge.Calls);
        var request = Assert.Single(confirmation.Requests);
        Assert.Equal("orders", request.Target);
        Assert.Equal(MessageSource.DeadLetter, request.Source);
        Assert.Equal(ConfirmationRisk.Irreversible, request.Risk);
        Assert.True(viewModel.IsPurgeCancelled);
        Assert.Contains("cancelled", viewModel.PurgeStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Purge_WhenConfirmed_UsesExactSelectedSource()
    {
        var purge = new RecordingPurgeService();
        var browse = new NoOpBrowseService();
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Confirmed);
        var viewModel = new QueueDetailViewModel(
            new StubQueueService(),
            browse,
            new StubMessageSendService(),
            new StubMessageReceiveService(),
            purge,
            confirmation,
            "orders")
        {
            SelectedSource = MessageSource.TransferDeadLetter
        };

        await viewModel.PurgeCommand.Execute().ToTask();

        Assert.Equal(
            new[] { ("orders", MessageSource.TransferDeadLetter) },
            purge.Calls);
        Assert.True(viewModel.IsPurgeSucceeded);
    }

    [Fact]
    public async Task SubscriptionPurge_WhenConfirmed_UsesSubscriptionPathAndExactSource()
    {
        var purge = new RecordingPurgeService();
        var browse = new NoOpBrowseService();
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Confirmed);
        var viewModel = new SubscriptionDetailViewModel(
            new StubSubscriptionService(),
            browse,
            new StubMessageSendService(),
            new StubMessageReceiveService(),
            purge,
            confirmation,
            "sales",
            "regional")
        {
            SelectedSource = MessageSource.DeadLetter
        };

        await viewModel.PurgeCommand.Execute().ToTask();

        Assert.Equal(
            new[] { ("sales/Subscriptions/regional", MessageSource.DeadLetter) },
            purge.Calls);
        var request = Assert.Single(confirmation.Requests);
        Assert.Equal("sales/Subscriptions/regional", request.Target);
        Assert.True(viewModel.IsPurgeSucceeded);
    }

    private sealed class StubMessageSendService : IMessageSendService
    {
        public Task<MessageSendResult> SendAsync(
            SendTargetContext target,
            MessageDraft draft,
            int sendCount = 1,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MessageSendResult(MessageSendStatus.Succeeded, target.SuccessDescription));
    }

    private sealed class StubMessageReceiveService : IMessageReceiveService
    {
        public Task<IReceiveSession> OpenPeekLockAsync(
            EntityAddress address,
            MessageSource source,
            SessionRequest? sessionRequest = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ReceiveAndDeleteResult> ReceiveAndDeleteAsync(
            ConfirmedReceiveAndDeleteRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public SettlementItemOutcome RejectPeekedSettlement(
            ObservedMessage message,
            SettlementAction action) =>
            SettlementTracker.RejectPeeked(message, action);

        public Task<SettlementItemOutcome> CompleteAsync(
            IReceiveSession session,
            ReceivedMessage message,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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

    private sealed class RecordingConfirmationService(ConfirmationResult result)
        : IConfirmationService
    {
        public List<ConfirmationRequest> Requests { get; } = [];

        public Task<ConfirmationResult> ConfirmAsync(
            ConfirmationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingPurgeService : IPurgeService
    {
        public List<(string Path, MessageSource Source)> Calls { get; } = [];

        public Task<OperationOutcome> PurgeAsync(
            EntityAddress target,
            MessageSource source,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((target.Path, source));
            return Task.FromResult(OperationOutcome.Succeeded(
                "Purge",
                target.Path,
                source,
                confirmedCount: 0,
                safeMessage: $"Purged 0 message(s) from {target.Path} ({source})."));
        }
    }

    private sealed class StubQueueService : IQueueService
    {
        public Task<IReadOnlyList<QueueInfo>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<QueueInfo>>([]);

        public Task<QueueInfo> GetAsync(string name, CancellationToken ct = default) =>
            Task.FromException<QueueInfo>(new InvalidOperationException("Not required by this test."));

        public Task<QueueInfo> CreateAsync(CreateQueueOptions opts, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<QueueInfo> UpdateAsync(QueueInfo updated, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReceivedMessage>> PeekAsync(
            string name,
            int maxCount,
            MessageSource source,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReceivedMessage>>([]);

        public Task SendAsync(
            string name,
            OutboundMessage message,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task PurgeAsync(
            string name,
            MessageSource source,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReceiveSession> OpenReceiveSessionAsync(
            string name,
            MessageSource source,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubSubscriptionService : ISubscriptionService
    {
        public Task<IReadOnlyList<SubscriptionInfo>> ListAsync(
            string topicName,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionInfo>>([]);

        public Task<SubscriptionInfo> GetAsync(
            string topicName,
            string name,
            CancellationToken ct = default) =>
            Task.FromException<SubscriptionInfo>(
                new InvalidOperationException("Not required by this test."));

        public Task<SubscriptionInfo> CreateAsync(
            CreateSubscriptionOptions opts,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SubscriptionInfo> UpdateAsync(
            SubscriptionInfo updated,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string topicName,
            string name,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RuleInfo>> ListRulesAsync(
            string topicName,
            string subscriptionName,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RuleInfo>>([]);

        public Task<RuleInfo> CreateRuleAsync(
            string topicName,
            string subscriptionName,
            CreateRuleOptions opts,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteRuleAsync(
            string topicName,
            string subscriptionName,
            string ruleName,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoOpBrowseService : IMessageBrowseService
    {
        public Task<MessageBrowseResult> PeekAsync(
            EntityAddress address,
            MessageSource source,
            PageRequest page,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MessageBrowseResult([], null, SourceAvailability.Empty));
    }
}
