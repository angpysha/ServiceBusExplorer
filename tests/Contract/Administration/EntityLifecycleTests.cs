#nullable enable
using Microsoft.Extensions.Logging.Abstractions;
using ServiceBusExplorer.Services;
using Xunit;

namespace ServiceBusExplorer.ContractTests.Administration;

/// <summary>
/// Contract tests for queue/topic create/update/delete with version-aware conflict and refresh.
/// </summary>
public sealed class EntityLifecycleTests
{
    [Fact]
    public async Task CreateQueue_WithSupportedFields_ReturnsCreatedEntityAndRefreshableState()
    {
        var adapter = new FakeAdminAdapter();
        var queues = CreateQueueService(adapter);

        var result = await queues.CreateAsync(new CreateQueueOptions(
            "orders",
            LockDuration: TimeSpan.FromSeconds(30),
            DefaultMessageTimeToLive: TimeSpan.FromDays(1),
            RequiresDuplicateDetection: false,
            RequiresSession: false,
            MaxDeliveryCount: 10));

        Assert.Equal(EntityLifecycleKind.Succeeded, result.Kind);
        Assert.NotNull(result.Entity);
        Assert.Equal("orders", result.Entity.Name);
        Assert.Equal(TimeSpan.FromSeconds(30), result.Entity.LockDuration);
        Assert.Equal(10, result.Entity.MaxDeliveryCount);
        Assert.False(string.IsNullOrWhiteSpace(result.Entity.ServiceVersion));
        Assert.Equal(result.Entity.ServiceVersion, result.ServiceVersion);

        var refreshed = await queues.GetAsync("orders");
        Assert.Equal(result.Entity.ServiceVersion, refreshed.ServiceVersion);
        Assert.Equal(result.Entity.LockDuration, refreshed.LockDuration);
    }

    [Fact]
    public async Task CreateTopic_WithSupportedFields_ReturnsCreatedEntityAndRefreshableState()
    {
        var adapter = new FakeAdminAdapter();
        var topics = CreateTopicService(adapter);

        var result = await topics.CreateAsync(new CreateTopicOptions(
            "events",
            DefaultMessageTimeToLive: TimeSpan.FromHours(12),
            EnableBatchedOperations: true,
            EnablePartitioning: false));

        Assert.Equal(EntityLifecycleKind.Succeeded, result.Kind);
        Assert.NotNull(result.Entity);
        Assert.Equal("events", result.Entity.Name);
        Assert.True(result.Entity.EnableBatchedOperations);
        Assert.False(result.Entity.EnablePartitioning);
        Assert.False(string.IsNullOrWhiteSpace(result.Entity.ServiceVersion));

        var refreshed = await topics.GetAsync("events");
        Assert.Equal(result.Entity.ServiceVersion, refreshed.ServiceVersion);
    }

    [Fact]
    public async Task UpdateQueue_WithMatchingVersion_SucceedsAndAdvancesVersion()
    {
        var adapter = new FakeAdminAdapter();
        var queues = CreateQueueService(adapter);
        var created = await queues.CreateAsync(new CreateQueueOptions("orders", MaxDeliveryCount: 5));
        Assert.Equal(EntityLifecycleKind.Succeeded, created.Kind);
        var priorVersion = created.Entity!.ServiceVersion!;

        var update = created.Entity with { MaxDeliveryCount = 12, UserMetadata = "v2" };
        var result = await queues.UpdateAsync(update);

        Assert.Equal(EntityLifecycleKind.Succeeded, result.Kind);
        Assert.NotNull(result.Entity);
        Assert.Equal(12, result.Entity.MaxDeliveryCount);
        Assert.Equal("v2", result.Entity.UserMetadata);
        Assert.NotEqual(priorVersion, result.Entity.ServiceVersion);
    }

    [Fact]
    public async Task UpdateQueue_WithMismatchedVersion_ReturnsConflictWithoutOverwrite()
    {
        var adapter = new FakeAdminAdapter();
        var queues = CreateQueueService(adapter);
        var created = await queues.CreateAsync(new CreateQueueOptions("orders", MaxDeliveryCount: 5));
        var stale = created.Entity! with { MaxDeliveryCount = 99, ServiceVersion = "stale-version" };

        // Concurrent change advances service version.
        var concurrent = await queues.UpdateAsync(created.Entity! with { MaxDeliveryCount = 7 });
        Assert.Equal(EntityLifecycleKind.Succeeded, concurrent.Kind);
        var serviceVersion = concurrent.Entity!.ServiceVersion!;

        var result = await queues.UpdateAsync(stale);

        Assert.Equal(EntityLifecycleKind.Conflict, result.Kind);
        Assert.NotNull(result.Entity);
        Assert.Equal(7, result.Entity.MaxDeliveryCount);
        Assert.Equal(serviceVersion, result.Entity.ServiceVersion);
        Assert.DoesNotContain("99", result.Entity.MaxDeliveryCount.ToString(), StringComparison.Ordinal);

        var refreshed = await queues.GetAsync("orders");
        Assert.Equal(7, refreshed.MaxDeliveryCount);
        Assert.Equal(serviceVersion, refreshed.ServiceVersion);
    }

    [Fact]
    public async Task UpdateTopic_WithMismatchedVersion_ReturnsConflictWithoutOverwrite()
    {
        var adapter = new FakeAdminAdapter();
        var topics = CreateTopicService(adapter);
        var created = await topics.CreateAsync(new CreateTopicOptions("events"));
        var stale = created.Entity! with
        {
            UserMetadata = "stale-write",
            ServiceVersion = "old"
        };

        var concurrent = await topics.UpdateAsync(created.Entity! with { UserMetadata = "winner" });
        Assert.Equal(EntityLifecycleKind.Succeeded, concurrent.Kind);

        var result = await topics.UpdateAsync(stale);

        Assert.Equal(EntityLifecycleKind.Conflict, result.Kind);
        Assert.NotNull(result.Entity);
        Assert.Equal("winner", result.Entity.UserMetadata);
        Assert.NotEqual("old", result.Entity.ServiceVersion);

        var refreshed = await topics.GetAsync("events");
        Assert.Equal("winner", refreshed.UserMetadata);
        Assert.Equal(result.Entity.ServiceVersion, refreshed.ServiceVersion);
    }

    [Fact]
    public async Task DeleteQueue_Succeeds_AndSubsequentGetListReflectAuthoritativeState()
    {
        var adapter = new FakeAdminAdapter();
        var queues = CreateQueueService(adapter);
        await queues.CreateAsync(new CreateQueueOptions("orders"));
        await queues.CreateAsync(new CreateQueueOptions("payments"));

        var deleted = await queues.DeleteAsync("orders");

        Assert.Equal(EntityLifecycleKind.Succeeded, deleted.Kind);
        Assert.Null(deleted.Entity);

        await Assert.ThrowsAnyAsync<Exception>(() => queues.GetAsync("orders"));
        var list = await queues.ListAsync();
        Assert.DoesNotContain(list, q => q.Name == "orders");
        Assert.Contains(list, q => q.Name == "payments");
    }

    [Fact]
    public async Task DeleteTopic_Succeeds_AndSubsequentGetListReflectAuthoritativeState()
    {
        var adapter = new FakeAdminAdapter();
        var topics = CreateTopicService(adapter);
        await topics.CreateAsync(new CreateTopicOptions("events"));

        var deleted = await topics.DeleteAsync("events");

        Assert.Equal(EntityLifecycleKind.Succeeded, deleted.Kind);
        Assert.Null(deleted.Entity);
        await Assert.ThrowsAnyAsync<Exception>(() => topics.GetAsync("events"));
        Assert.Empty(await topics.ListAsync());
    }

    [Fact]
    public async Task CreateQueue_WithInvalidMaxDeliveryCount_ReturnsTypedValidation_NotSilentClamp()
    {
        var adapter = new FakeAdminAdapter();
        var queues = CreateQueueService(adapter);

        var result = await queues.CreateAsync(new CreateQueueOptions("orders", MaxDeliveryCount: 0));

        Assert.Equal(EntityLifecycleKind.ValidationFailed, result.Kind);
        Assert.Null(result.Entity);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains(result.ValidationErrors, e =>
            e.Contains("MaxDeliveryCount", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await queues.ListAsync());
    }

    [Fact]
    public async Task CreateQueue_WithInvalidLockDuration_ReturnsTypedValidation_NotSilentClamp()
    {
        var adapter = new FakeAdminAdapter();
        var queues = CreateQueueService(adapter);

        var result = await queues.CreateAsync(new CreateQueueOptions(
            "orders",
            LockDuration: TimeSpan.FromHours(1)));

        Assert.Equal(EntityLifecycleKind.ValidationFailed, result.Kind);
        Assert.Null(result.Entity);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains(result.ValidationErrors, e =>
            e.Contains("LockDuration", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(adapter.Queues);
    }

    [Fact]
    public async Task UpdateQueue_ChangingImmutableRequiresSession_ReturnsValidationFailed()
    {
        var adapter = new FakeAdminAdapter();
        var queues = CreateQueueService(adapter);
        var created = await queues.CreateAsync(new CreateQueueOptions("orders", RequiresSession: false));

        var result = await queues.UpdateAsync(created.Entity! with { RequiresSession = true });

        Assert.Equal(EntityLifecycleKind.ValidationFailed, result.Kind);
        Assert.Contains(result.ValidationErrors ?? [], e =>
            e.Contains("RequiresSession", StringComparison.OrdinalIgnoreCase));
        var current = await queues.GetAsync("orders");
        Assert.False(current.RequiresSession);
    }

    [Fact]
    public async Task AfterConflict_RefreshReturnsCurrentServiceVersion()
    {
        var adapter = new FakeAdminAdapter();
        var queues = CreateQueueService(adapter);
        var created = await queues.CreateAsync(new CreateQueueOptions("orders"));
        var concurrent = await queues.UpdateAsync(created.Entity! with { MaxDeliveryCount = 8 });
        var conflict = await queues.UpdateAsync(created.Entity! with
        {
            MaxDeliveryCount = 1,
            ServiceVersion = "stale"
        });

        Assert.Equal(EntityLifecycleKind.Conflict, conflict.Kind);
        Assert.Equal(concurrent.Entity!.ServiceVersion, conflict.ServiceVersion);

        var refreshed = await queues.GetAsync("orders");
        Assert.Equal(concurrent.Entity.ServiceVersion, refreshed.ServiceVersion);
        Assert.Equal(8, refreshed.MaxDeliveryCount);
    }

    private static QueueService CreateQueueService(IServiceBusAdminAdapter adapter) =>
        new(adapter, client: null!, NullLogger<QueueService>.Instance, allowNullClient: true);

    private static TopicService CreateTopicService(IServiceBusAdminAdapter adapter) =>
        new(adapter, NullLogger<TopicService>.Instance);

    /// <summary>In-memory admin seam with monotonic version tokens.</summary>
    private sealed class FakeAdminAdapter : IServiceBusAdminAdapter
    {
        private int _version;

        public Dictionary<string, QueueAdminSnapshot> Queues { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, TopicAdminSnapshot> Topics { get; } = new(StringComparer.OrdinalIgnoreCase);

        private string NextVersion() => $"v{Interlocked.Increment(ref _version)}";

        public Task<IReadOnlyList<QueueAdminSnapshot>> ListQueuesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QueueAdminSnapshot>>(Queues.Values.ToList());

        public Task<QueueAdminSnapshot?> GetQueueAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(Queues.TryGetValue(name, out var q) ? q : null);

        public Task<QueueAdminSnapshot> CreateQueueAsync(
            QueueAdminCreateRequest request,
            CancellationToken cancellationToken)
        {
            if (Queues.ContainsKey(request.Name))
                throw new InvalidOperationException("Queue already exists.");

            var snap = new QueueAdminSnapshot(
                request.Name,
                NextVersion(),
                request.LockDuration ?? TimeSpan.FromSeconds(30),
                request.DefaultMessageTimeToLive ?? TimeSpan.FromDays(14),
                TimeSpan.MaxValue,
                request.RequiresDuplicateDetection,
                request.RequiresSession,
                request.MaxDeliveryCount ?? 10,
                1024,
                true,
                false,
                null,
                null,
                null,
                TimeSpan.FromMinutes(1),
                EntityStatus.Active,
                0,
                0,
                0,
                0);
            Queues[request.Name] = snap;
            return Task.FromResult(snap);
        }

        public Task<QueueAdminSnapshot> UpdateQueueAsync(
            QueueAdminSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            var updated = snapshot with { ServiceVersion = NextVersion() };
            Queues[snapshot.Name] = updated;
            return Task.FromResult(updated);
        }

        public Task DeleteQueueAsync(string name, CancellationToken cancellationToken)
        {
            Queues.Remove(name);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TopicAdminSnapshot>> ListTopicsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TopicAdminSnapshot>>(Topics.Values.ToList());

        public Task<TopicAdminSnapshot?> GetTopicAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(Topics.TryGetValue(name, out var t) ? t : null);

        public Task<TopicAdminSnapshot> CreateTopicAsync(
            TopicAdminCreateRequest request,
            CancellationToken cancellationToken)
        {
            var snap = new TopicAdminSnapshot(
                request.Name,
                NextVersion(),
                request.DefaultMessageTimeToLive ?? TimeSpan.FromDays(14),
                TimeSpan.MaxValue,
                1024,
                request.EnableBatchedOperations,
                request.EnablePartitioning,
                false,
                TimeSpan.FromMinutes(1),
                null,
                EntityStatus.Active,
                0,
                0);
            Topics[request.Name] = snap;
            return Task.FromResult(snap);
        }

        public Task<TopicAdminSnapshot> UpdateTopicAsync(
            TopicAdminSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            var updated = snapshot with { ServiceVersion = NextVersion() };
            Topics[snapshot.Name] = updated;
            return Task.FromResult(updated);
        }

        public Task DeleteTopicAsync(string name, CancellationToken cancellationToken)
        {
            Topics.Remove(name);
            return Task.CompletedTask;
        }
    }
}
