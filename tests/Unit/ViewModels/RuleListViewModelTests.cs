#nullable enable
using System.Reactive.Threading.Tasks;
using ServiceBusExplorer.ViewModels;
using Xunit;

namespace ServiceBusExplorer.UnitTests.ViewModels;

/// <summary>
/// Presentation tests for typed catch-all rules in <see cref="RuleListViewModel"/>.
/// </summary>
public sealed class RuleListViewModelTests
{
    [Fact]
    public async Task Refresh_PresentsCatchAllAsTypedNotSqlOneEqualsOne()
    {
        var svc = new FakeSubscriptionService();
        svc.Rules.Add(new SubscriptionRule(
            "$Default",
            RuleFilterKind.CatchAll,
            FilterExpression: null,
            ActionExpression: null,
            ServiceVersion: "v1"));
        svc.Rules.Add(new SubscriptionRule(
            "sql-true",
            RuleFilterKind.Sql,
            "1=1",
            ActionExpression: null,
            ServiceVersion: "v1"));

        var vm = new RuleListViewModel(svc, new ConfirmAllService(), "orders", "retail");
        await vm.RefreshCommand.Execute().ToTask();

        Assert.Equal(2, vm.Rules.Count);
        var catchAll = Assert.Single(vm.Rules, r => r.Name == "$Default");
        Assert.True(catchAll.IsCatchAll);
        Assert.Equal(RuleFilterKind.CatchAll, catchAll.FilterKind);
        Assert.Equal("Catch-all", catchAll.FilterDisplay);
        Assert.Null(catchAll.FilterExpression);

        var sql = Assert.Single(vm.Rules, r => r.Name == "sql-true");
        Assert.False(sql.IsCatchAll);
        Assert.Equal("1=1", sql.FilterExpression);
    }

    [Fact]
    public void SelectingCatchAllFilterKind_ClearsExpressionAndExposesCatchAllFlag()
    {
        var vm = new RuleListViewModel(new FakeSubscriptionService(), new ConfirmAllService(), "orders", "retail");
        vm.NewRuleExpression = "sys.Label = 'x'";
        vm.NewRuleFilterKind = RuleFilterKind.CatchAll;

        Assert.True(vm.IsNewRuleCatchAll);
        Assert.Equal("", vm.NewRuleExpression);
        Assert.Contains(RuleFilterKind.CatchAll, RuleListViewModel.FilterKinds);
    }

    [Fact]
    public async Task SaveEdit_WhenConflict_AppliesAuthoritativeRuleAndSurfacesError()
    {
        var svc = new FakeSubscriptionService();
        var original = new SubscriptionRule(
            "r1", RuleFilterKind.Sql, "sys.Label = 'A'", null, "v1");
        svc.Rules.Add(original);
        svc.UpdateConflict = EntityLifecycleResult<SubscriptionRule>.Conflict(
            original with { ServiceVersion = "v2", FilterExpression = "sys.Label = 'A'" },
            "v2",
            "stale version");

        var vm = new RuleListViewModel(svc, new ConfirmAllService(), "orders", "retail");
        await vm.RefreshCommand.Execute().ToTask();
        vm.SelectedRule = vm.Rules[0];
        vm.IsEditing = true;
        vm.EditExpression = "sys.Label = 'STALE'";

        await vm.SaveEditCommand.Execute().ToTask();

        Assert.NotNull(vm.Error);
        Assert.Contains("stale", vm.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("v2", vm.SelectedRule!.ServiceVersion);
        Assert.Equal("sys.Label = 'A'", vm.SelectedRule.FilterExpression);
    }

    private sealed class FakeSubscriptionService : ISubscriptionService
    {
        public List<SubscriptionRule> Rules { get; } = [];
        public EntityLifecycleResult<SubscriptionRule>? UpdateConflict { get; set; }

        public Task<IReadOnlyList<SubscriptionInfo>> ListAsync(string topicName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SubscriptionInfo>>([]);

        public Task<SubscriptionInfo> GetAsync(string topicName, string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<SubscriptionInfo>> CreateAsync(
            CreateSubscriptionOptions opts,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<SubscriptionInfo>> UpdateAsync(
            SubscriptionInfo updated,
            CancellationToken ct = default) =>
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
            CancellationToken ct = default)
        {
            if (UpdateConflict is not null)
                return Task.FromResult(UpdateConflict);
            var next = updated with { ServiceVersion = "v-next" };
            var idx = Rules.FindIndex(r => r.Name == updated.Name);
            if (idx >= 0) Rules[idx] = next;
            return Task.FromResult(EntityLifecycleResult<SubscriptionRule>.Succeeded(
                next, next.ServiceVersion, "ok"));
        }

        public Task<EntityLifecycleResult<SubscriptionRule?>> DeleteRuleAsync(
            string topicName,
            string subscriptionName,
            string ruleName,
            string? expectedVersion = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ConfirmAllService : IConfirmationService
    {
        public Task<ConfirmationResult> ConfirmAsync(
            ConfirmationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ConfirmationResult.Confirmed);
    }
}
