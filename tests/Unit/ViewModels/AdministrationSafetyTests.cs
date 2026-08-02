#nullable enable
using System.Reactive.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceBusExplorer.Services;
using ServiceBusExplorer.ViewModels;
using Xunit;

namespace ServiceBusExplorer.UnitTests.ViewModels;

/// <summary>
/// Administration delete confirmation and lifecycle presentation (validation, auth, conflict, refresh).
/// </summary>
public sealed class AdministrationSafetyTests
{
    [Fact]
    public async Task DeleteQueue_WhenCancelled_DoesNotCallService()
    {
        var queues = new RecordingQueueService();
        queues.Seed(CreateQueue("orders"));
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Cancelled);
        var vm = CreateQueueList(queues, confirmation);
        await SeedQueueListAsync(vm, queues);

        await vm.DeleteCommand.Execute("orders").ToTask();

        Assert.Empty(queues.DeleteCalls);
        var request = Assert.Single(confirmation.Requests);
        Assert.Equal("orders", request.Target);
        Assert.Null(request.Source);
        Assert.Equal(ConfirmationRisk.Irreversible, request.Risk);
        Assert.True(vm.IsAdminCancelled);
        Assert.Contains("cancelled", vm.AdminStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Single(vm.Queues);
    }

    [Fact]
    public async Task DeleteQueue_WhenConfirmed_CallsDeleteOnceAndRemovesFromList()
    {
        var queues = new RecordingQueueService();
        queues.Seed(CreateQueue("orders"));
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Confirmed);
        var vm = CreateQueueList(queues, confirmation);
        await SeedQueueListAsync(vm, queues);

        await vm.DeleteCommand.Execute("orders").ToTask();

        Assert.Equal(new[] { "orders" }, queues.DeleteCalls);
        Assert.Empty(vm.Queues);
        Assert.True(vm.IsAdminSucceeded);
        Assert.Contains("deleted", vm.AdminStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteTopic_WhenCancelled_DoesNotCallService()
    {
        var topics = new RecordingTopicService();
        topics.Seed(CreateTopic("events"));
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Cancelled);
        var vm = CreateTopicList(topics, confirmation);
        await SeedTopicListAsync(vm, topics);

        await vm.DeleteCommand.Execute("events").ToTask();

        Assert.Empty(topics.DeleteCalls);
        var request = Assert.Single(confirmation.Requests);
        Assert.Equal("events", request.Target);
        Assert.Null(request.Source);
        Assert.True(vm.IsAdminCancelled);
    }

    [Fact]
    public async Task DeleteTopic_WhenConfirmed_CallsDeleteOnce()
    {
        var topics = new RecordingTopicService();
        topics.Seed(CreateTopic("events"));
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Confirmed);
        var vm = CreateTopicList(topics, confirmation);
        await SeedTopicListAsync(vm, topics);

        await vm.DeleteCommand.Execute("events").ToTask();

        Assert.Equal(new[] { "events" }, topics.DeleteCalls);
        Assert.Empty(vm.Topics);
        Assert.True(vm.IsAdminSucceeded);
    }

    [Fact]
    public async Task DeleteSubscription_WhenCancelled_DoesNotCallService()
    {
        var subs = new RecordingSubscriptionService();
        subs.Info = CreateSubscription("sales", "regional", "v1");
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Cancelled);
        var vm = new SubscriptionDetailViewModel(
            subs,
            new NoOpBrowseService(),
            new StubMessageSendService(),
            new StubMessageReceiveService(),
            new NoOpPurgeService(),
            confirmation,
            "sales",
            "regional");

        await vm.DeleteCommand.Execute().ToTask();

        Assert.Empty(subs.DeleteCalls);
        var request = Assert.Single(confirmation.Requests);
        Assert.Equal("regional", request.Target);
        Assert.Null(request.Source);
        Assert.True(vm.IsAdminCancelled);
    }

    [Fact]
    public async Task DeleteSubscription_WhenConfirmed_CallsDeleteOnce()
    {
        var subs = new RecordingSubscriptionService();
        subs.Info = CreateSubscription("sales", "regional", "v1");
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Confirmed);
        var vm = new SubscriptionDetailViewModel(
            subs,
            new NoOpBrowseService(),
            new StubMessageSendService(),
            new StubMessageReceiveService(),
            new NoOpPurgeService(),
            confirmation,
            "sales",
            "regional");

        await vm.DeleteCommand.Execute().ToTask();

        var call = Assert.Single(subs.DeleteCalls);
        Assert.Equal(("sales", "regional"), (call.Topic, call.Name));
        Assert.True(vm.IsAdminSucceeded);
    }

    [Fact]
    public async Task DeleteRule_WhenCancelled_DoesNotCallService()
    {
        var subs = new RecordingSubscriptionService();
        subs.Rules.Add(new SubscriptionRule("filter-a", RuleFilterKind.Sql, "1=1", null, "v1"));
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Cancelled);
        var vm = new RuleListViewModel(subs, confirmation, "sales", "regional");
        await vm.RefreshCommand.Execute().ToTask();

        await vm.DeleteCommand.Execute("filter-a").ToTask();

        Assert.Empty(subs.DeleteRuleCalls);
        var request = Assert.Single(confirmation.Requests);
        Assert.Equal("filter-a", request.Target);
        Assert.Null(request.Source);
        Assert.True(vm.IsAdminCancelled);
        Assert.Single(vm.Rules);
    }

    [Fact]
    public async Task DeleteRule_WhenConfirmed_CallsDeleteOnceAndRemovesRule()
    {
        var subs = new RecordingSubscriptionService();
        subs.Rules.Add(new SubscriptionRule("filter-a", RuleFilterKind.Sql, "1=1", null, "v1"));
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Confirmed);
        var vm = new RuleListViewModel(subs, confirmation, "sales", "regional");
        await vm.RefreshCommand.Execute().ToTask();

        await vm.DeleteCommand.Execute("filter-a").ToTask();

        Assert.Equal(new[] { "filter-a" }, subs.DeleteRuleCalls.Select(c => c.RuleName));
        Assert.Empty(vm.Rules);
        Assert.True(vm.IsAdminSucceeded);
    }

    [Fact]
    public async Task DeleteQueue_WhenConflict_PresentsStaleStateWithoutRemoving()
    {
        var queues = new RecordingQueueService();
        var current = CreateQueue("orders", version: "v2", maxDelivery: 7);
        queues.Seed(CreateQueue("orders", version: "v1", maxDelivery: 5));
        queues.DeleteResult = EntityLifecycleResult<QueueInfo?>.Conflict(
            current,
            "v2",
            "Queue was changed elsewhere; delete was not applied.");
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Confirmed);
        var vm = CreateQueueList(queues, confirmation);
        await SeedQueueListAsync(vm, queues);

        await vm.DeleteCommand.Execute("orders").ToTask();

        Assert.Single(queues.DeleteCalls);
        Assert.Single(vm.Queues);
        Assert.Equal("v2", vm.Queues[0].ServiceVersion);
        Assert.Equal(7, vm.Queues[0].MaxDeliveryCount);
        Assert.True(vm.IsAdminConflict);
        Assert.True(vm.IsAdminStale);
        Assert.Contains("changed elsewhere", vm.AdminStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteQueue_AfterConflict_RefreshLoadsAuthoritativeState()
    {
        var queues = new RecordingQueueService();
        queues.Seed(CreateQueue("orders", version: "v1", maxDelivery: 5));
        queues.DeleteResult = EntityLifecycleResult<QueueInfo?>.Conflict(
            CreateQueue("orders", version: "v2", maxDelivery: 9),
            "v2",
            "stale");
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Confirmed);
        var vm = CreateQueueList(queues, confirmation);
        await SeedQueueListAsync(vm, queues);
        await vm.DeleteCommand.Execute("orders").ToTask();
        Assert.True(vm.IsAdminStale);

        queues.Replace(CreateQueue("orders", version: "v3", maxDelivery: 11));
        queues.ListOverride = null;
        await vm.RefreshCommand.Execute().ToTask();

        Assert.Equal("v3", vm.Queues[0].ServiceVersion);
        Assert.Equal(11, vm.Queues[0].MaxDeliveryCount);
        Assert.False(vm.IsAdminStale);
    }

    [Fact]
    public async Task CreateQueue_WhenValidationFails_SurfacesActionableMessage()
    {
        var queues = new RecordingQueueService
        {
            CreateResult = EntityLifecycleResult<QueueInfo>.ValidationFailed(
                "Queue name is invalid.",
                "Name must be 1–260 characters.")
        };
        var vm = CreateQueueList(queues, new RecordingConfirmationService(ConfirmationResult.Confirmed));
        vm.IsCreating = true;
        vm.NewQueueName = "!!!";

        await vm.QuickCreateCommand.Execute().ToTask();

        Assert.Single(queues.CreateCalls);
        Assert.True(vm.IsAdminValidationFailed);
        Assert.Contains("invalid", vm.AdminStatus, StringComparison.OrdinalIgnoreCase);
        Assert.True(vm.IsCreating);
    }

    [Fact]
    public async Task DeleteQueue_WhenAuthorizationFails_SurfacesActionableMessage()
    {
        var queues = new RecordingQueueService();
        queues.Seed(CreateQueue("orders"));
        queues.DeleteResult = EntityLifecycleResult<QueueInfo?>.Failed(
            "Not authorized to delete queue 'orders'.");
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Confirmed);
        var vm = CreateQueueList(queues, confirmation);
        await SeedQueueListAsync(vm, queues);

        await vm.DeleteCommand.Execute("orders").ToTask();

        Assert.Single(queues.DeleteCalls);
        Assert.Single(vm.Queues);
        Assert.True(vm.IsAdminFailed);
        Assert.Contains("not authorized", vm.AdminStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateSubscription_WhenConflict_PresentsStaleAuthoritativeInfo()
    {
        var original = CreateSubscription("sales", "regional", "v1", maxDelivery: 5);
        var authoritative = original with { ServiceVersion = "v2", MaxDeliveryCount = 8 };
        var subs = new RecordingSubscriptionService
        {
            Info = original,
            UpdateResult = EntityLifecycleResult<SubscriptionInfo>.Conflict(
                authoritative,
                "v2",
                "Subscription was changed elsewhere; your edits were not applied.")
        };
        var vm = new SubscriptionDetailViewModel(
            subs,
            new NoOpBrowseService(),
            new StubMessageSendService(),
            new StubMessageReceiveService(),
            new NoOpPurgeService(),
            new RecordingConfirmationService(ConfirmationResult.Confirmed),
            "sales",
            "regional");

        await vm.RefreshInfoCommand.Execute().ToTask();
        vm.MaxDeliveryCount = 99;
        await vm.UpdateCommand.Execute().ToTask();

        Assert.True(vm.IsAdminConflict);
        Assert.True(vm.IsAdminStale);
        Assert.Equal(8, vm.Info!.MaxDeliveryCount);
        Assert.Equal("v2", vm.Info.ServiceVersion);
        Assert.Contains("changed elsewhere", vm.AdminStatus!, StringComparison.OrdinalIgnoreCase);
    }

    private static QueueListViewModel CreateQueueList(
        RecordingQueueService queues,
        IConfirmationService confirmation)
    {
        var ns = new NamespaceService(
            NullLogger<NamespaceService>.Instance,
            queueService: queues,
            topicService: new RecordingTopicService());
        return new QueueListViewModel(
            ns,
            queues,
            new NoOpBrowseService(),
            new StubMessageSendService(),
            new StubMessageReceiveService(),
            new NoOpPurgeService(),
            confirmation);
    }

    private static TopicListViewModel CreateTopicList(
        RecordingTopicService topics,
        IConfirmationService confirmation)
    {
        var ns = new NamespaceService(
            NullLogger<NamespaceService>.Instance,
            queueService: new RecordingQueueService(),
            topicService: topics);
        return new TopicListViewModel(
            ns,
            topics,
            new RecordingSubscriptionService(),
            new NoOpBrowseService(),
            new StubMessageSendService(),
            new StubMessageReceiveService(),
            new NoOpPurgeService(),
            confirmation);
    }

    private static async Task SeedQueueListAsync(QueueListViewModel vm, RecordingQueueService queues)
    {
        queues.ListOverride = queues.Items.ToList();
        vm.ApplyConnectionScope(
            ConnectionScope.Namespace,
            null,
            CapabilitySet.ForNamespaceScope(adminProbeSucceeded: true));
        await vm.RefreshCommand.Execute().ToTask();
    }

    private static async Task SeedTopicListAsync(TopicListViewModel vm, RecordingTopicService topics)
    {
        topics.ListOverride = topics.Items.ToList();
        vm.ApplyConnectionScope(
            ConnectionScope.Namespace,
            null,
            CapabilitySet.ForNamespaceScope(adminProbeSucceeded: true));
        await vm.RefreshCommand.Execute().ToTask();
    }

    private static QueueInfo CreateQueue(string name, string version = "v1", int maxDelivery = 10) =>
        new(
            name,
            ActiveMessageCount: 0,
            DeadLetterCount: 0,
            ScheduledMessageCount: 0,
            LockDuration: TimeSpan.FromSeconds(30),
            RequiresDuplicateDetection: false,
            RequiresSession: false,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            Status: EntityStatus.Active,
            MaxDeliveryCount: maxDelivery,
            ServiceVersion: version);

    private static TopicInfo CreateTopic(string name, string version = "v1") =>
        new(
            name,
            SubscriptionCount: 0,
            SizeInBytes: 0,
            EnableBatchedOperations: true,
            EnablePartitioning: false,
            Status: EntityStatus.Active,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            ServiceVersion: version);

    private static SubscriptionInfo CreateSubscription(
        string topic,
        string name,
        string version,
        int maxDelivery = 10) =>
        new(
            topic,
            name,
            ActiveMessageCount: 0,
            DeadLetterCount: 0,
            LockDuration: TimeSpan.FromSeconds(30),
            MaxDeliveryCount: maxDelivery,
            Status: EntityStatus.Active,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            ServiceVersion: version);

    private sealed class RecordingConfirmationService(ConfirmationResult result) : IConfirmationService
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
        public List<QueueInfo> Items { get; } = [];
        public List<string> DeleteCalls { get; } = [];
        public List<string> CreateCalls { get; } = [];
        public List<QueueInfo>? ListOverride { get; set; }
        public EntityLifecycleResult<QueueInfo?>? DeleteResult { get; set; }
        public EntityLifecycleResult<QueueInfo>? CreateResult { get; set; }

        public void Seed(QueueInfo queue)
        {
            Items.RemoveAll(q => q.Name == queue.Name);
            Items.Add(queue);
        }

        public void Replace(QueueInfo queue) => Seed(queue);

        public Task<IReadOnlyList<QueueInfo>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<QueueInfo>>(ListOverride ?? Items.ToList());

        public Task<QueueInfo> GetAsync(string name, CancellationToken ct = default)
        {
            var item = Items.FirstOrDefault(q => q.Name == name)
                ?? throw new InvalidOperationException($"Queue '{name}' not found.");
            return Task.FromResult(item);
        }

        public Task<EntityLifecycleResult<QueueInfo>> CreateAsync(
            CreateQueueOptions opts,
            CancellationToken ct = default)
        {
            CreateCalls.Add(opts.Name);
            if (CreateResult is not null)
                return Task.FromResult(CreateResult);

            var created = CreateQueue(opts.Name);
            Seed(created);
            return Task.FromResult(EntityLifecycleResult<QueueInfo>.Succeeded(created, created.ServiceVersion, "ok"));
        }

        public Task<EntityLifecycleResult<QueueInfo>> UpdateAsync(
            QueueInfo updated,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<QueueInfo?>> DeleteAsync(
            string name,
            CancellationToken ct = default)
        {
            DeleteCalls.Add(name);
            if (DeleteResult is not null)
                return Task.FromResult(DeleteResult);

            Items.RemoveAll(q => q.Name == name);
            return Task.FromResult(EntityLifecycleResult<QueueInfo?>.Succeeded(
                null,
                null,
                $"Queue '{name}' was deleted."));
        }

        public Task<IReadOnlyList<ReceivedMessage>> PeekAsync(
            string name,
            int maxCount,
            MessageSource source,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReceivedMessage>>([]);

        public Task SendAsync(string name, OutboundMessage message, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task PurgeAsync(string name, MessageSource source, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReceiveSession> OpenReceiveSessionAsync(
            string name,
            MessageSource source,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingTopicService : ITopicService
    {
        public List<TopicInfo> Items { get; } = [];
        public List<string> DeleteCalls { get; } = [];
        public List<TopicInfo>? ListOverride { get; set; }

        public void Seed(TopicInfo topic)
        {
            Items.RemoveAll(t => t.Name == topic.Name);
            Items.Add(topic);
        }

        public Task<IReadOnlyList<TopicInfo>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TopicInfo>>(ListOverride ?? Items.ToList());

        public Task<TopicInfo> GetAsync(string name, CancellationToken ct = default)
        {
            var item = Items.FirstOrDefault(t => t.Name == name)
                ?? throw new InvalidOperationException($"Topic '{name}' not found.");
            return Task.FromResult(item);
        }

        public Task<EntityLifecycleResult<TopicInfo>> CreateAsync(
            CreateTopicOptions opts,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<TopicInfo>> UpdateAsync(
            TopicInfo updated,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<TopicInfo?>> DeleteAsync(
            string name,
            CancellationToken ct = default)
        {
            DeleteCalls.Add(name);
            Items.RemoveAll(t => t.Name == name);
            return Task.FromResult(EntityLifecycleResult<TopicInfo?>.Succeeded(
                null,
                null,
                $"Topic '{name}' was deleted."));
        }
    }

    private sealed class RecordingSubscriptionService : ISubscriptionService
    {
        public SubscriptionInfo? Info { get; set; }
        public List<SubscriptionRule> Rules { get; } = [];
        public List<(string Topic, string Name, string? Version)> DeleteCalls { get; } = [];
        public List<(string Topic, string Sub, string RuleName)> DeleteRuleCalls { get; } = [];
        public EntityLifecycleResult<SubscriptionInfo>? UpdateResult { get; set; }

        public Task<IReadOnlyList<SubscriptionInfo>> ListAsync(string topicName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionInfo>>(
                Info is null ? [] : [Info]);

        public Task<SubscriptionInfo> GetAsync(string topicName, string name, CancellationToken ct = default) =>
            Task.FromResult(Info ?? throw new InvalidOperationException("missing"));

        public Task<EntityLifecycleResult<SubscriptionInfo>> CreateAsync(
            CreateSubscriptionOptions opts,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<SubscriptionInfo>> UpdateAsync(
            SubscriptionInfo updated,
            CancellationToken ct = default) =>
            Task.FromResult(UpdateResult
                ?? EntityLifecycleResult<SubscriptionInfo>.Succeeded(updated, updated.ServiceVersion, "ok"));

        public Task<EntityLifecycleResult<SubscriptionInfo?>> DeleteAsync(
            string topicName,
            string name,
            string? expectedVersion = null,
            CancellationToken ct = default)
        {
            DeleteCalls.Add((topicName, name, expectedVersion));
            return Task.FromResult(EntityLifecycleResult<SubscriptionInfo?>.Succeeded(
                null,
                null,
                $"Subscription '{name}' was deleted."));
        }

        public Task<IReadOnlyList<SubscriptionRule>> ListRulesAsync(
            string topicName,
            string subscriptionName,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionRule>>(Rules.ToList());

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
            CancellationToken ct = default)
        {
            DeleteRuleCalls.Add((topicName, subscriptionName, ruleName));
            Rules.RemoveAll(r => r.Name == ruleName);
            return Task.FromResult(EntityLifecycleResult<SubscriptionRule?>.Succeeded(
                null,
                null,
                $"Rule '{ruleName}' was deleted."));
        }
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
                confirmedCount: 0,
                safeMessage: "ok"));
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
