using System.Reactive.Threading.Tasks;
using ServiceBusExplorer.ViewModels;
using Xunit;

namespace ServiceBusExplorer.UnitTests.ViewModels;

public class PurgeConfirmationTests
{
    [Fact]
    public async Task Purge_WithNoSelectedSource_DoesNotCallService()
    {
        var queue = new RecordingQueueService();
        var browse = new NoOpBrowseService();
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Confirmed);
        var viewModel = new QueueDetailViewModel(queue, browse, new StubMessageSendService(), confirmation, "orders");

        await viewModel.PurgeCommand.Execute().ToTask();

        Assert.Empty(queue.PurgeCalls);
        Assert.Empty(confirmation.Requests);
    }

    [Fact]
    public async Task Purge_WhenCancelled_DoesNotCallService()
    {
        var queue = new RecordingQueueService();
        var browse = new NoOpBrowseService();
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Cancelled);
        var viewModel = new QueueDetailViewModel(queue, browse, new StubMessageSendService(), confirmation, "orders")
        {
            SelectedSource = MessageSource.DeadLetter
        };

        await viewModel.PurgeCommand.Execute().ToTask();

        Assert.Empty(queue.PurgeCalls);
        var request = Assert.Single(confirmation.Requests);
        Assert.Equal("orders", request.Target);
        Assert.Equal(MessageSource.DeadLetter, request.Source);
        Assert.Equal(ConfirmationRisk.Irreversible, request.Risk);
    }

    [Fact]
    public async Task Purge_WhenConfirmed_UsesExactSelectedSource()
    {
        var queue = new RecordingQueueService();
        var browse = new NoOpBrowseService();
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Confirmed);
        var viewModel = new QueueDetailViewModel(queue, browse, new StubMessageSendService(), confirmation, "orders")
        {
            SelectedSource = MessageSource.TransferDeadLetter
        };

        await viewModel.PurgeCommand.Execute().ToTask();

        Assert.Equal(
            new[] { ("orders", MessageSource.TransferDeadLetter) },
            queue.PurgeCalls);
    }

    [Fact]
    public async Task SubscriptionPurge_WhenConfirmed_UsesSubscriptionPathAndExactSource()
    {
        var queue = new RecordingQueueService();
        var browse = new NoOpBrowseService();
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Confirmed);
        var viewModel = new SubscriptionDetailViewModel(
            new StubSubscriptionService(),
            queue,
            browse,
            new StubMessageSendService(),
            confirmation,
            "sales",
            "regional")
        {
            SelectedSource = MessageSource.DeadLetter
        };

        await viewModel.PurgeCommand.Execute().ToTask();

        Assert.Equal(
            new[] { ("sales/Subscriptions/regional", MessageSource.DeadLetter) },
            queue.PurgeCalls);
        var request = Assert.Single(confirmation.Requests);
        Assert.Equal("sales/Subscriptions/regional", request.Target);
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

    private sealed class RecordingQueueService : IQueueService
    {
        public List<(string Name, MessageSource Source)> PurgeCalls { get; } = [];

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
            CancellationToken ct = default)
        {
            PurgeCalls.Add((name, source));
            return Task.CompletedTask;
        }

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
