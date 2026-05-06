using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class RuleListViewModel : ReactiveObject
{
    private readonly ISubscriptionService _svc;
    private readonly string _topicName;
    private readonly string _subscriptionName;
    private readonly SourceList<RuleInfo> _source = new();
    private bool _isLoading;
    private string? _error;
    private RuleInfo? _selectedRule;
    private bool _isCreating;
    private string _newRuleName = "";
    private string _newRuleExpression = "1=1";
    private string _newRuleFilterType = "SqlFilter";

    public ReadOnlyObservableCollection<RuleInfo> Rules { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public string? Error
    {
        get => _error;
        private set => this.RaiseAndSetIfChanged(ref _error, value);
    }

    public RuleInfo? SelectedRule
    {
        get => _selectedRule;
        set => this.RaiseAndSetIfChanged(ref _selectedRule, value);
    }

    public bool IsCreating
    {
        get => _isCreating;
        set => this.RaiseAndSetIfChanged(ref _isCreating, value);
    }

    public string NewRuleName
    {
        get => _newRuleName;
        set => this.RaiseAndSetIfChanged(ref _newRuleName, value);
    }

    public string NewRuleExpression
    {
        get => _newRuleExpression;
        set => this.RaiseAndSetIfChanged(ref _newRuleExpression, value);
    }

    public string NewRuleFilterType
    {
        get => _newRuleFilterType;
        set => this.RaiseAndSetIfChanged(ref _newRuleFilterType, value);
    }

    public static IReadOnlyList<string> FilterTypes { get; } = new[] { "SqlFilter", "CorrelationFilter" };

    public ReactiveCommand<Unit, IReadOnlyList<RuleInfo>> RefreshCommand { get; }
    public ReactiveCommand<Unit, RuleInfo> CreateCommand { get; }
    public ReactiveCommand<string, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> BeginCreateCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCreateCommand { get; }

    public RuleListViewModel(ISubscriptionService svc, string topicName, string subscriptionName)
    {
        _svc = svc;
        _topicName = topicName;
        _subscriptionName = subscriptionName;

        _source.Connect().Bind(out var bound).Subscribe();
        Rules = bound;

        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsLoading = true;
            Error = null;
            try
            {
                var items = await _svc.ListRulesAsync(_topicName, _subscriptionName);
                _source.Edit(list => { list.Clear(); list.AddRange(items); });
                return items;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return (IReadOnlyList<RuleInfo>)Array.Empty<RuleInfo>();
            }
            finally
            {
                IsLoading = false;
            }
        });

        var canCreate = this.WhenAnyValue(
            x => x.NewRuleName, x => x.NewRuleExpression,
            (n, e) => !string.IsNullOrWhiteSpace(n) && !string.IsNullOrWhiteSpace(e));

        CreateCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var opts = new CreateRuleOptions(NewRuleName, NewRuleExpression, NewRuleFilterType);
            var created = await _svc.CreateRuleAsync(_topicName, _subscriptionName, opts);
            _source.Add(created);
            IsCreating = false;
            NewRuleName = "";
            NewRuleExpression = "1=1";
            return created;
        }, canCreate);

        DeleteCommand = ReactiveCommand.CreateFromTask<string, Unit>(async name =>
        {
            await _svc.DeleteRuleAsync(_topicName, _subscriptionName, name);
            _source.Edit(list =>
            {
                var item = list.FirstOrDefault(r => r.Name == name);
                if (item != null) list.Remove(item);
            });
            return Unit.Default;
        });

        BeginCreateCommand = ReactiveCommand.Create(() => { IsCreating = true; });
        CancelCreateCommand = ReactiveCommand.Create(() =>
        {
            IsCreating = false;
            NewRuleName = "";
            NewRuleExpression = "1=1";
        });
    }
}
