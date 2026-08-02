#nullable enable
using Microsoft.Extensions.Logging.Abstractions;
using ServiceBusExplorer.Services;
using Xunit;

namespace ServiceBusExplorer.ContractTests.Administration;

/// <summary>
/// Contract tests for subscription/rule lifecycle: CRUD, typed filters, catch-all, conflict, refresh.
/// </summary>
public sealed class SubscriptionAndRuleLifecycleTests
{
    private const string Topic = "orders";
    private const string SubName = "retail";

    [Fact]
    public async Task Subscription_Create_Then_List_ContainsSubscription()
    {
        var (service, _) = CreateService();

        var create = await service.CreateAsync(new CreateSubscriptionOptions(Topic, SubName));
        Assert.Equal(EntityLifecycleKind.Succeeded, create.Kind);
        Assert.NotNull(create.Entity);
        Assert.False(string.IsNullOrEmpty(create.Entity!.ServiceVersion));

        var list = await service.ListAsync(Topic);
        Assert.Contains(list, s => s.Name == SubName && s.ServiceVersion == create.Entity.ServiceVersion);
    }

    [Fact]
    public async Task Subscription_Update_WhenVersionMatches_SucceedsAndRefreshesVersion()
    {
        var (service, _) = CreateService();
        var created = (await service.CreateAsync(new CreateSubscriptionOptions(Topic, SubName))).Entity!;

        var updated = created with { MaxDeliveryCount = 15 };
        var result = await service.UpdateAsync(updated);

        Assert.Equal(EntityLifecycleKind.Succeeded, result.Kind);
        Assert.NotNull(result.Entity);
        Assert.Equal(15, result.Entity!.MaxDeliveryCount);
        Assert.NotEqual(created.ServiceVersion, result.Entity.ServiceVersion);

        var listed = await service.ListAsync(Topic);
        Assert.Equal(15, listed.Single(s => s.Name == SubName).MaxDeliveryCount);
    }

    [Fact]
    public async Task Subscription_Update_WhenVersionStale_ReturnsConflictWithAuthoritativeRefresh()
    {
        var (service, store) = CreateService();
        var created = (await service.CreateAsync(new CreateSubscriptionOptions(Topic, SubName))).Entity!;

        store.BumpSubscriptionVersion(Topic, SubName);
        var authoritativeBefore = await service.GetAsync(Topic, SubName);

        var stale = created with { MaxDeliveryCount = 99 };
        var result = await service.UpdateAsync(stale);

        Assert.Equal(EntityLifecycleKind.Conflict, result.Kind);
        Assert.NotNull(result.Entity);
        Assert.Equal(authoritativeBefore.ServiceVersion, result.Entity!.ServiceVersion);
        Assert.NotEqual(99, result.Entity.MaxDeliveryCount);

        var listed = await service.ListAsync(Topic);
        Assert.NotEqual(99, listed.Single().MaxDeliveryCount);
    }

    [Fact]
    public async Task Subscription_Delete_WhenVersionStale_ReturnsConflict_AndEntityRemains()
    {
        var (service, store) = CreateService();
        var created = (await service.CreateAsync(new CreateSubscriptionOptions(Topic, SubName))).Entity!;
        store.BumpSubscriptionVersion(Topic, SubName);

        var result = await service.DeleteAsync(Topic, SubName, created.ServiceVersion);

        Assert.Equal(EntityLifecycleKind.Conflict, result.Kind);
        Assert.NotNull(await service.GetAsync(Topic, SubName));
    }

    [Fact]
    public async Task Subscription_Delete_WhenVersionMatches_RemovesFromList()
    {
        var (service, _) = CreateService();
        var created = (await service.CreateAsync(new CreateSubscriptionOptions(Topic, SubName))).Entity!;

        var result = await service.DeleteAsync(Topic, SubName, created.ServiceVersion);
        Assert.Equal(EntityLifecycleKind.Succeeded, result.Kind);

        var list = await service.ListAsync(Topic);
        Assert.DoesNotContain(list, s => s.Name == SubName);
    }

    [Fact]
    public async Task SqlRule_CreateEditDelete_AndListReflectsState()
    {
        var (service, _) = CreateService();
        await service.CreateAsync(new CreateSubscriptionOptions(Topic, SubName));

        var created = await service.CreateRuleAsync(
            Topic,
            SubName,
            new CreateSubscriptionRuleOptions("sql-rule", RuleFilterKind.Sql, "sys.Label = 'A'"));
        Assert.Equal(EntityLifecycleKind.Succeeded, created.Kind);
        Assert.Equal(RuleFilterKind.Sql, created.Entity!.FilterKind);
        Assert.Equal("sys.Label = 'A'", created.Entity.FilterExpression);
        Assert.False(created.Entity.IsCatchAll);

        var edited = await service.UpdateRuleAsync(
            Topic,
            SubName,
            created.Entity with { FilterExpression = "sys.Label = 'B'" });
        Assert.Equal(EntityLifecycleKind.Succeeded, edited.Kind);
        Assert.Equal("sys.Label = 'B'", edited.Entity!.FilterExpression);
        Assert.NotEqual(created.Entity.ServiceVersion, edited.Entity.ServiceVersion);

        var listAfterEdit = await service.ListRulesAsync(Topic, SubName);
        Assert.Equal("sys.Label = 'B'", listAfterEdit.Single(r => r.Name == "sql-rule").FilterExpression);

        var deleted = await service.DeleteRuleAsync(
            Topic, SubName, "sql-rule", edited.Entity.ServiceVersion);
        Assert.Equal(EntityLifecycleKind.Succeeded, deleted.Kind);
        Assert.DoesNotContain(await service.ListRulesAsync(Topic, SubName), r => r.Name == "sql-rule");
    }

    [Fact]
    public async Task CorrelationRule_CreateEditDelete_AndListReflectsState()
    {
        var (service, _) = CreateService();
        await service.CreateAsync(new CreateSubscriptionOptions(Topic, SubName));

        var created = await service.CreateRuleAsync(
            Topic,
            SubName,
            new CreateSubscriptionRuleOptions("corr-rule", RuleFilterKind.Correlation, "corr-1"));
        Assert.Equal(EntityLifecycleKind.Succeeded, created.Kind);
        Assert.Equal(RuleFilterKind.Correlation, created.Entity!.FilterKind);
        Assert.Equal("corr-1", created.Entity.FilterExpression);

        var edited = await service.UpdateRuleAsync(
            Topic,
            SubName,
            created.Entity with { FilterExpression = "corr-2" });
        Assert.Equal(EntityLifecycleKind.Succeeded, edited.Kind);
        Assert.Equal("corr-2", edited.Entity!.FilterExpression);

        var deleted = await service.DeleteRuleAsync(
            Topic, SubName, "corr-rule", edited.Entity.ServiceVersion);
        Assert.Equal(EntityLifecycleKind.Succeeded, deleted.Kind);
        Assert.Empty(await service.ListRulesAsync(Topic, SubName));
    }

    [Fact]
    public async Task CatchAllRule_IsExplicitTyped_NotSqlOneEqualsOne()
    {
        var (service, store) = CreateService();
        await service.CreateAsync(new CreateSubscriptionOptions(Topic, SubName));

        store.SeedCatchAllRule(Topic, SubName, "$Default");

        var listed = await service.ListRulesAsync(Topic, SubName);
        var catchAll = Assert.Single(listed);
        Assert.Equal(RuleFilterKind.CatchAll, catchAll.FilterKind);
        Assert.True(catchAll.IsCatchAll);
        Assert.Null(catchAll.FilterExpression);
        Assert.Equal("Catch-all", catchAll.FilterDisplay);

        var created = await service.CreateRuleAsync(
            Topic,
            SubName,
            new CreateSubscriptionRuleOptions("explicit-catch", RuleFilterKind.CatchAll));
        Assert.Equal(EntityLifecycleKind.Succeeded, created.Kind);
        Assert.Equal(RuleFilterKind.CatchAll, created.Entity!.FilterKind);
        Assert.True(created.Entity.IsCatchAll);
        Assert.Null(created.Entity.FilterExpression);

        var sql = await service.CreateRuleAsync(
            Topic,
            SubName,
            new CreateSubscriptionRuleOptions("sql-true", RuleFilterKind.Sql, "1=1"));
        Assert.Equal(RuleFilterKind.Sql, sql.Entity!.FilterKind);
        Assert.False(sql.Entity.IsCatchAll);
        Assert.Equal("1=1", sql.Entity.FilterExpression);
    }

    [Fact]
    public async Task Rule_Update_WhenVersionStale_ReturnsConflictWithAuthoritativeRefresh()
    {
        var (service, store) = CreateService();
        await service.CreateAsync(new CreateSubscriptionOptions(Topic, SubName));
        var created = (await service.CreateRuleAsync(
            Topic,
            SubName,
            new CreateSubscriptionRuleOptions("r1", RuleFilterKind.Sql, "sys.Label = 'A'"))).Entity!;

        store.BumpRuleVersion(Topic, SubName, "r1");
        var current = (await service.ListRulesAsync(Topic, SubName)).Single(r => r.Name == "r1");

        var result = await service.UpdateRuleAsync(
            Topic,
            SubName,
            created with { FilterExpression = "sys.Label = 'STALE'" });

        Assert.Equal(EntityLifecycleKind.Conflict, result.Kind);
        Assert.NotNull(result.Entity);
        Assert.Equal(current.ServiceVersion, result.Entity!.ServiceVersion);
        Assert.Equal("sys.Label = 'A'", result.Entity.FilterExpression);

        var listed = await service.ListRulesAsync(Topic, SubName);
        Assert.Equal("sys.Label = 'A'", listed.Single().FilterExpression);
    }

    [Fact]
    public async Task ListRules_AfterMutations_ReflectsServiceState()
    {
        var (service, _) = CreateService();
        await service.CreateAsync(new CreateSubscriptionOptions(Topic, SubName));

        await service.CreateRuleAsync(
            Topic, SubName, new CreateSubscriptionRuleOptions("a", RuleFilterKind.Sql, "1=1"));
        await service.CreateRuleAsync(
            Topic, SubName, new CreateSubscriptionRuleOptions("b", RuleFilterKind.Correlation, "x"));

        var mid = await service.ListRulesAsync(Topic, SubName);
        Assert.Equal(2, mid.Count);

        var b = mid.Single(r => r.Name == "b");
        await service.DeleteRuleAsync(Topic, SubName, "b", b.ServiceVersion);

        var after = await service.ListRulesAsync(Topic, SubName);
        Assert.Single(after);
        Assert.Equal("a", after[0].Name);
    }

    private static (SubscriptionService Service, InMemorySubscriptionAdminStore Store) CreateService()
    {
        var store = new InMemorySubscriptionAdminStore();
        var service = new SubscriptionService(store, NullLogger<SubscriptionService>.Instance);
        return (service, store);
    }

    private sealed class InMemorySubscriptionAdminStore : ISubscriptionAdministrationAdapter
    {
        private readonly Dictionary<string, SubscriptionInfo> _subs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, SubscriptionRule>> _rules =
            new(StringComparer.OrdinalIgnoreCase);
        private int _version;

        private static string Key(string topic, string name) => $"{topic}/{name}";

        public void BumpSubscriptionVersion(string topic, string name)
        {
            var key = Key(topic, name);
            if (_subs.TryGetValue(key, out var sub))
                _subs[key] = sub with { ServiceVersion = NextVersion() };
        }

        public void BumpRuleVersion(string topic, string subscription, string ruleName)
        {
            var rules = RulesFor(topic, subscription);
            if (rules.TryGetValue(ruleName, out var rule))
                rules[ruleName] = rule with { ServiceVersion = NextVersion() };
        }

        public void SeedCatchAllRule(string topic, string subscription, string ruleName)
        {
            RulesFor(topic, subscription)[ruleName] = new SubscriptionRule(
                ruleName,
                RuleFilterKind.CatchAll,
                FilterExpression: null,
                ActionExpression: null,
                ServiceVersion: NextVersion());
        }

        public Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(
            string topicName,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionInfo>>(
                _subs.Values.Where(s => s.TopicName == topicName).ToList());

        public Task<SubscriptionInfo?> GetSubscriptionAsync(
            string topicName,
            string name,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                _subs.TryGetValue(Key(topicName, name), out var s) ? s : null);

        public Task<EntityLifecycleResult<SubscriptionInfo>> CreateSubscriptionAsync(
            CreateSubscriptionOptions options,
            CancellationToken cancellationToken)
        {
            var info = new SubscriptionInfo(
                options.TopicName,
                options.Name,
                ActiveMessageCount: 0,
                DeadLetterCount: 0,
                LockDuration: options.LockDuration ?? TimeSpan.FromSeconds(30),
                MaxDeliveryCount: options.MaxDeliveryCount ?? 10,
                Status: EntityStatus.Active,
                ServiceVersion: NextVersion());
            _subs[Key(options.TopicName, options.Name)] = info;
            RulesFor(options.TopicName, options.Name);
            return Task.FromResult(EntityLifecycleResult<SubscriptionInfo>.Succeeded(
                info, info.ServiceVersion, "created"));
        }

        public Task<EntityLifecycleResult<SubscriptionInfo>> UpdateSubscriptionAsync(
            SubscriptionInfo updated,
            string expectedVersion,
            CancellationToken cancellationToken)
        {
            var key = Key(updated.TopicName, updated.Name);
            if (!_subs.TryGetValue(key, out var current))
                return Task.FromResult(EntityLifecycleResult<SubscriptionInfo>.NotFound("not found"));

            if (!string.Equals(current.ServiceVersion, expectedVersion, StringComparison.Ordinal))
            {
                return Task.FromResult(EntityLifecycleResult<SubscriptionInfo>.Conflict(
                    current,
                    current.ServiceVersion,
                    "stale version"));
            }

            var next = updated with { ServiceVersion = NextVersion() };
            _subs[key] = next;
            return Task.FromResult(EntityLifecycleResult<SubscriptionInfo>.Succeeded(
                next, next.ServiceVersion, "ok"));
        }

        public Task<EntityLifecycleResult<SubscriptionInfo?>> DeleteSubscriptionAsync(
            string topicName,
            string name,
            string? expectedVersion,
            CancellationToken cancellationToken)
        {
            var key = Key(topicName, name);
            if (!_subs.TryGetValue(key, out var current))
                return Task.FromResult(EntityLifecycleResult<SubscriptionInfo?>.NotFound("not found"));

            if (!string.IsNullOrEmpty(expectedVersion) &&
                !string.Equals(current.ServiceVersion, expectedVersion, StringComparison.Ordinal))
            {
                return Task.FromResult(EntityLifecycleResult<SubscriptionInfo?>.Conflict(
                    current,
                    current.ServiceVersion,
                    "stale"));
            }

            _subs.Remove(key);
            _rules.Remove(key);
            return Task.FromResult(EntityLifecycleResult<SubscriptionInfo?>.Succeeded(
                null, null, "deleted"));
        }

        public Task<IReadOnlyList<SubscriptionRule>> ListRulesAsync(
            string topicName,
            string subscriptionName,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubscriptionRule>>(
                RulesFor(topicName, subscriptionName).Values.ToList());

        public Task<EntityLifecycleResult<SubscriptionRule>> CreateRuleAsync(
            string topicName,
            string subscriptionName,
            CreateSubscriptionRuleOptions options,
            CancellationToken cancellationToken)
        {
            ValidateFilter(options.FilterKind, options.FilterExpression);
            var rules = RulesFor(topicName, subscriptionName);
            if (rules.ContainsKey(options.Name))
                return Task.FromResult(EntityLifecycleResult<SubscriptionRule>.Failed("exists"));

            var rule = new SubscriptionRule(
                options.Name,
                options.FilterKind,
                options.FilterKind == RuleFilterKind.CatchAll ? null : options.FilterExpression,
                options.ActionExpression,
                NextVersion());
            rules[options.Name] = rule;
            return Task.FromResult(EntityLifecycleResult<SubscriptionRule>.Succeeded(
                rule, rule.ServiceVersion, "created"));
        }

        public Task<EntityLifecycleResult<SubscriptionRule>> UpdateRuleAsync(
            string topicName,
            string subscriptionName,
            SubscriptionRule updated,
            string expectedVersion,
            CancellationToken cancellationToken)
        {
            var rules = RulesFor(topicName, subscriptionName);
            if (!rules.TryGetValue(updated.Name, out var current))
                return Task.FromResult(EntityLifecycleResult<SubscriptionRule>.NotFound("not found"));

            if (!string.Equals(current.ServiceVersion, expectedVersion, StringComparison.Ordinal))
            {
                return Task.FromResult(EntityLifecycleResult<SubscriptionRule>.Conflict(
                    current,
                    current.ServiceVersion,
                    "stale version"));
            }

            ValidateFilter(updated.FilterKind, updated.FilterExpression);
            var next = updated with
            {
                FilterExpression = updated.FilterKind == RuleFilterKind.CatchAll
                    ? null
                    : updated.FilterExpression,
                ServiceVersion = NextVersion()
            };
            rules[updated.Name] = next;
            return Task.FromResult(EntityLifecycleResult<SubscriptionRule>.Succeeded(
                next, next.ServiceVersion, "updated"));
        }

        public Task<EntityLifecycleResult<SubscriptionRule?>> DeleteRuleAsync(
            string topicName,
            string subscriptionName,
            string ruleName,
            string? expectedVersion,
            CancellationToken cancellationToken)
        {
            var rules = RulesFor(topicName, subscriptionName);
            if (!rules.TryGetValue(ruleName, out var current))
                return Task.FromResult(EntityLifecycleResult<SubscriptionRule?>.NotFound("not found"));

            if (!string.IsNullOrEmpty(expectedVersion) &&
                !string.Equals(current.ServiceVersion, expectedVersion, StringComparison.Ordinal))
            {
                return Task.FromResult(EntityLifecycleResult<SubscriptionRule?>.Conflict(
                    current,
                    current.ServiceVersion,
                    "stale"));
            }

            rules.Remove(ruleName);
            return Task.FromResult(EntityLifecycleResult<SubscriptionRule?>.Succeeded(
                null, null, "deleted"));
        }

        private Dictionary<string, SubscriptionRule> RulesFor(string topic, string subscription)
        {
            var key = Key(topic, subscription);
            if (!_rules.TryGetValue(key, out var rules))
            {
                rules = new Dictionary<string, SubscriptionRule>(StringComparer.OrdinalIgnoreCase);
                _rules[key] = rules;
            }

            return rules;
        }

        private string NextVersion() => $"v{Interlocked.Increment(ref _version)}";

        private static void ValidateFilter(RuleFilterKind kind, string? expression)
        {
            if (kind == RuleFilterKind.CatchAll)
                return;
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException("Filter expression is required for non-catch-all rules.");
        }
    }
}
