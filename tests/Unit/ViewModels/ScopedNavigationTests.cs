#nullable enable
using System.Reactive.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceBusExplorer.Services;
using ServiceBusExplorer.ViewModels;
using Xunit;

namespace ServiceBusExplorer.UnitTests.ViewModels;

public sealed class ScopedNavigationTests
{
    [Fact]
    public async Task NamespaceScope_WithBrowseCapability_RefreshesQueuesAndTopicsFromServices()
    {
        var queueSvc = new CountingQueueService(
        [
            CreateQueue("queue-a"),
            CreateQueue("queue-b"),
        ]);
        var topicSvc = new CountingTopicService(
        [
            CreateTopic("topic-a"),
            CreateTopic("topic-b"),
        ]);
        var main = CreateMain(queueSvc, topicSvc, CapabilitySet.ForNamespaceScope(adminProbeSucceeded: true));

        await main.Queues.RefreshCommand.Execute().ToTask();
        await main.Topics.RefreshCommand.Execute().ToTask();

        Assert.Equal(2, main.Queues.Queues.Count);
        Assert.Equal(2, main.Topics.Topics.Count);
        Assert.Equal(1, queueSvc.ListAsyncCallCount);
        Assert.Equal(1, topicSvc.ListAsyncCallCount);
        Assert.Null(main.Queues.Error);
        Assert.Null(main.Topics.Error);
    }

    [Fact]
    public async Task EntityScope_QueuePath_ShowsOnlyThatQueue_AndSkipsListAsync()
    {
        var queueSvc = new CountingQueueService([CreateQueue("other-queue")]);
        var topicSvc = new CountingTopicService([CreateTopic("other-topic")]);
        var main = CreateMain(
            queueSvc,
            topicSvc,
            CapabilitySet.ForEntityScope(),
            ConnectionScope.Entity,
            "orders",
            ScopedEntityKind.Queue);

        await main.Queues.RefreshCommand.Execute().ToTask();
        await main.Topics.RefreshCommand.Execute().ToTask();

        var queue = Assert.Single(main.Queues.Queues);
        Assert.Equal("orders", queue.Name);
        Assert.Empty(main.Topics.Topics);
        Assert.Equal(0, queueSvc.ListAsyncCallCount);
        Assert.Equal(0, topicSvc.ListAsyncCallCount);
    }

    [Fact]
    public async Task EntityScope_TopicPath_ShowsOnlyThatTopic_AndSkipsListAsync()
    {
        var queueSvc = new CountingQueueService([CreateQueue("other-queue")]);
        var topicSvc = new CountingTopicService([CreateTopic("other-topic")]);
        var main = CreateMain(
            queueSvc,
            topicSvc,
            CapabilitySet.ForEntityScope(),
            ConnectionScope.Entity,
            "events",
            ScopedEntityKind.Topic);

        await main.Queues.RefreshCommand.Execute().ToTask();
        await main.Topics.RefreshCommand.Execute().ToTask();

        Assert.Empty(main.Queues.Queues);
        var topic = Assert.Single(main.Topics.Topics);
        Assert.Equal("events", topic.Name);
        Assert.Equal(0, queueSvc.ListAsyncCallCount);
        Assert.Equal(0, topicSvc.ListAsyncCallCount);
    }

    [Fact]
    public async Task BrowseDisabled_ReturnsEmptyLists_WithGuidance_AndNoListCalls()
    {
        var capabilities = CapabilitySet.ForNamespaceScope(adminProbeSucceeded: false) with
        {
            CanBrowseEntities = false,
        };
        var queueSvc = new CountingQueueService([CreateQueue("queue-a")]);
        var topicSvc = new CountingTopicService([CreateTopic("topic-a")]);
        var main = CreateMain(queueSvc, topicSvc, capabilities);

        await main.Queues.RefreshCommand.Execute().ToTask();
        await main.Topics.RefreshCommand.Execute().ToTask();

        Assert.Empty(main.Queues.Queues);
        Assert.Empty(main.Topics.Topics);
        Assert.Contains("not permitted", main.Queues.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not permitted", main.Topics.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, queueSvc.ListAsyncCallCount);
        Assert.Equal(0, topicSvc.ListAsyncCallCount);
    }

    [Fact]
    public async Task Refresh_RespectsScope_AfterApplyConnectionScope()
    {
        var queueSvc = new CountingQueueService(
        [
            CreateQueue("queue-a"),
            CreateQueue("queue-b"),
        ]);
        var topicSvc = new CountingTopicService([CreateTopic("topic-a")]);
        var main = CreateMain(
            queueSvc,
            topicSvc,
            CapabilitySet.ForNamespaceScope(adminProbeSucceeded: true));

        await main.RefreshCommand.Execute().ToTask();
        Assert.Equal(2, main.Queues.Queues.Count);
        Assert.Single(main.Topics.Topics);
        Assert.Equal(1, queueSvc.ListAsyncCallCount);
        Assert.Equal(1, topicSvc.ListAsyncCallCount);

        var entityContext = LiveConnectionContext.Create(
            "example.servicebus.windows.net",
            ConnectionScope.Entity,
            CapabilitySet.ForEntityScope(),
            profileId: null,
            entityPath: "orders",
            disposeAsync: null);

        main.ApplyConnectionScope(entityContext, ScopedEntityKind.Queue);
        queueSvc.ListAsyncCallCount = 0;
        topicSvc.ListAsyncCallCount = 0;

        await main.RefreshCommand.Execute().ToTask();

        var queue = Assert.Single(main.Queues.Queues);
        Assert.Equal("orders", queue.Name);
        Assert.Empty(main.Topics.Topics);
        Assert.Equal(0, queueSvc.ListAsyncCallCount);
        Assert.Equal(0, topicSvc.ListAsyncCallCount);
        Assert.True(main.IsEntityScoped);
        Assert.False(main.CanBrowseNamespace);
        Assert.Equal("orders", main.ScopedEntityPath);
    }

    [Fact]
    public void ApplyConnectionScope_SetsVisibilityFlags_ForEntityQueue()
    {
        var main = CreateMain(
            new CountingQueueService([]),
            new CountingTopicService([]),
            CapabilitySet.ForEntityScope(),
            ConnectionScope.Entity,
            "orders",
            ScopedEntityKind.Queue);

        Assert.True(main.ShowQueuesPanel);
        Assert.False(main.ShowTopicsPanel);
        Assert.Contains("Entity scope", main.ScopeBannerText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orders", main.ScopeBannerText, StringComparison.OrdinalIgnoreCase);
    }

    private static MainViewModel CreateMain(
        CountingQueueService queueSvc,
        CountingTopicService topicSvc,
        CapabilitySet capabilities,
        ConnectionScope scope = ConnectionScope.Namespace,
        string? entityPath = null,
        ScopedEntityKind entityKind = ScopedEntityKind.None)
    {
        var confirmation = new NoOpConfirmationService();
        var browse = new NoOpBrowseService();
        var namespaceSvc = new NamespaceService(
            NullLogger<NamespaceService>.Instance,
            queueService: queueSvc,
            topicService: topicSvc);

        var eventHubSvc = new StubEventHubService();
        var eventHubDetail = new EventHubDetailViewModel(eventHubSvc);
        var queues = new QueueListViewModel(
            namespaceSvc,
            queueSvc,
            browse,
            new StubMessageSendService(),
            new StubMessageReceiveService(),
            new NoOpPurgeService(),
            confirmation);
        var topics = new TopicListViewModel(
            namespaceSvc,
            topicSvc,
            new StubSubscriptionService(),
            browse,
            new StubMessageSendService(),
            new StubMessageReceiveService(),
            new NoOpPurgeService(),
            confirmation);

        var context = LiveConnectionContext.Create(
            "example.servicebus.windows.net",
            scope,
            capabilities,
            profileId: null,
            entityPath: entityPath,
            disposeAsync: null);

        var main = new MainViewModel(
            namespaceSvc,
            queues,
            topics,
            new EventHubListViewModel(eventHubSvc, eventHubDetail),
            new RelayListViewModel(new StubRelayService()),
            new NotificationHubListViewModel(new StubNotificationHubService()),
            new DashboardViewModel(queues, topics),
            context);

        if (entityKind != ScopedEntityKind.None)
            main.ApplyConnectionScope(context, entityKind);

        return main;
    }

    private static QueueInfo CreateQueue(string name) =>
        new(
            name,
            ActiveMessageCount: 1,
            DeadLetterCount: 0,
            ScheduledMessageCount: 0,
            LockDuration: TimeSpan.FromMinutes(1),
            RequiresDuplicateDetection: false,
            RequiresSession: false,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            Status: EntityStatus.Active);

    private static TopicInfo CreateTopic(string name) =>
        new(
            name,
            SubscriptionCount: 1,
            SizeInBytes: 0,
            EnableBatchedOperations: true,
            EnablePartitioning: false,
            Status: EntityStatus.Active);

    private sealed class CountingQueueService(IReadOnlyList<QueueInfo> items) : IQueueService
    {
        public int ListAsyncCallCount { get; set; }

        public Task<IReadOnlyList<QueueInfo>> ListAsync(CancellationToken ct = default)
        {
            ListAsyncCallCount++;
            return Task.FromResult(items);
        }

        public Task<QueueInfo> GetAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(items.First(q => q.Name == name));

        public Task<EntityLifecycleResult<QueueInfo>> CreateAsync(CreateQueueOptions opts, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<QueueInfo>> UpdateAsync(QueueInfo updated, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<QueueInfo?>> DeleteAsync(string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReceivedMessage>> PeekAsync(
            string name,
            int maxCount,
            MessageSource source,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReceivedMessage>>([]);

        public Task SendAsync(string name, OutboundMessage message, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task PurgeAsync(string name, MessageSource source, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReceiveSession> OpenReceiveSessionAsync(
            string name,
            MessageSource source,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class CountingTopicService(IReadOnlyList<TopicInfo> items) : ITopicService
    {
        public int ListAsyncCallCount { get; set; }

        public Task<IReadOnlyList<TopicInfo>> ListAsync(CancellationToken ct = default)
        {
            ListAsyncCallCount++;
            return Task.FromResult(items);
        }

        public Task<TopicInfo> GetAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(items.First(t => t.Name == name));

        public Task<EntityLifecycleResult<TopicInfo>> CreateAsync(CreateTopicOptions opts, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<TopicInfo>> UpdateAsync(TopicInfo updated, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<TopicInfo?>> DeleteAsync(string name, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoOpPurgeService : IPurgeService
    {
        public Task<OperationOutcome> PurgeAsync(
            EntityAddress target,
            MessageSource source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationOutcome.Succeeded(
                "Purge",
                target.Path,
                source,
                0,
                "No-op purge."));
    }

    private sealed class NoOpConfirmationService : IConfirmationService
    {
        public Task<ConfirmationResult> ConfirmAsync(
            ConfirmationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ConfirmationResult.Confirmed);
    }

    private sealed class StubSubscriptionService : ISubscriptionService
    {
        public Task<IReadOnlyList<SubscriptionInfo>> ListAsync(string topicName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionInfo>>([]);

        public Task<SubscriptionInfo> GetAsync(string topicName, string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<SubscriptionInfo>> CreateAsync(CreateSubscriptionOptions opts, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<SubscriptionInfo>> UpdateAsync(SubscriptionInfo updated, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<SubscriptionInfo?>> DeleteAsync(
            string topicName,
            string name,
            string? expectedVersion = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SubscriptionRule>> ListRulesAsync(
            string topicName,
            string subscriptionName,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionRule>>([]);

        public Task<EntityLifecycleResult<SubscriptionRule>> CreateRuleAsync(
            string topicName,
            string subscriptionName,
            CreateSubscriptionRuleOptions opts,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<SubscriptionRule>> UpdateRuleAsync(
            string topicName,
            string subscriptionName,
            SubscriptionRule updated,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<SubscriptionRule?>> DeleteRuleAsync(
            string topicName,
            string subscriptionName,
            string ruleName,
            string? expectedVersion = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubEventHubService : IEventHubService
    {
        public Task<EventHubInfo> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(new EventHubInfo("test-hub", [], DateTimeOffset.UtcNow));

        public Task<IReadOnlyList<ConsumerGroupInfo>> ListConsumerGroupsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConsumerGroupInfo>>([]);

        public Task<IReadOnlyList<PartitionInfo>> ListPartitionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PartitionInfo>>([]);

        public Task<PartitionInfo> GetPartitionAsync(string partitionId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubRelayService : IRelayService
    {
        public Task<IReadOnlyList<RelayInfo>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RelayInfo>>([]);

        public Task<RelayInfo> GetAsync(string name, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubNotificationHubService : INotificationHubService
    {
        public Task<IReadOnlyList<NotificationHubInfo>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NotificationHubInfo>>([]);

        public Task<NotificationHubInfo> GetAsync(string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<NotificationHubInfo> CreateAsync(string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string name, CancellationToken ct = default) =>
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
}
