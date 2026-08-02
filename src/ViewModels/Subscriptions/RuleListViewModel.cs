#nullable enable
using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class RuleListViewModel : ReactiveObject
{
    private readonly ISubscriptionService _svc;
    private readonly IConfirmationService _confirmationService;
    private readonly string _topicName;
    private readonly string _subscriptionName;
    private readonly SourceList<SubscriptionRule> _source = new();
    private bool _isLoading;
    private string? _error;
    private SubscriptionRule? _selectedRule;
    private bool _isCreating;
    private bool _isEditing;
    private string _newRuleName = "";
    private string _newRuleExpression = "";
    private RuleFilterKind _newRuleFilterKind = RuleFilterKind.Sql;
    private string? _editExpression;
    private RuleFilterKind _editFilterKind = RuleFilterKind.Sql;
    private string? _adminStatus;
    private EntityLifecycleKind? _adminOutcomeKind;
    private bool _isAdminCancelled;

    public ReadOnlyObservableCollection<SubscriptionRule> Rules { get; }

    public string? AdminStatus
    {
        get => _adminStatus;
        private set => this.RaiseAndSetIfChanged(ref _adminStatus, value);
    }

    public EntityLifecycleKind? AdminOutcomeKind
    {
        get => _adminOutcomeKind;
        private set => this.RaiseAndSetIfChanged(ref _adminOutcomeKind, value);
    }

    public bool IsAdminCancelled
    {
        get => _isAdminCancelled;
        private set => this.RaiseAndSetIfChanged(ref _isAdminCancelled, value);
    }

    public bool IsAdminConflict => AdminOutcomeKind == EntityLifecycleKind.Conflict;
    public bool IsAdminStale => IsAdminConflict;
    public bool IsAdminValidationFailed => AdminOutcomeKind == EntityLifecycleKind.ValidationFailed;
    public bool IsAdminFailed => AdminOutcomeKind == EntityLifecycleKind.Failed;
    public bool IsAdminSucceeded => AdminOutcomeKind == EntityLifecycleKind.Succeeded;

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

    public SubscriptionRule? SelectedRule
    {
        get => _selectedRule;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedRule, value);
            if (value is not null)
            {
                EditFilterKind = value.FilterKind;
                EditExpression = value.FilterExpression ?? "";
            }
        }
    }

    public bool IsCreating
    {
        get => _isCreating;
        set => this.RaiseAndSetIfChanged(ref _isCreating, value);
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => this.RaiseAndSetIfChanged(ref _isEditing, value);
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

    public RuleFilterKind NewRuleFilterKind
    {
        get => _newRuleFilterKind;
        set
        {
            this.RaiseAndSetIfChanged(ref _newRuleFilterKind, value);
            this.RaisePropertyChanged(nameof(IsNewRuleCatchAll));
            if (value == RuleFilterKind.CatchAll)
                NewRuleExpression = "";
        }
    }

    public string? EditExpression
    {
        get => _editExpression;
        set => this.RaiseAndSetIfChanged(ref _editExpression, value);
    }

    public RuleFilterKind EditFilterKind
    {
        get => _editFilterKind;
        set
        {
            this.RaiseAndSetIfChanged(ref _editFilterKind, value);
            this.RaisePropertyChanged(nameof(IsEditRuleCatchAll));
            if (value == RuleFilterKind.CatchAll)
                EditExpression = "";
        }
    }

    /// <summary>True when the create form is set to explicit catch-all (no expression needed).</summary>
    public bool IsNewRuleCatchAll => NewRuleFilterKind == RuleFilterKind.CatchAll;

    /// <summary>True when the edit form is set to explicit catch-all.</summary>
    public bool IsEditRuleCatchAll => EditFilterKind == RuleFilterKind.CatchAll;

    /// <summary>Filter kinds offered in create/edit UI, including explicit catch-all.</summary>
    public static IReadOnlyList<RuleFilterKind> FilterKinds { get; } =
    [
        RuleFilterKind.Sql,
        RuleFilterKind.Correlation,
        RuleFilterKind.CatchAll
    ];

    public ReactiveCommand<Unit, IReadOnlyList<SubscriptionRule>> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> CreateCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveEditCommand { get; }
    public ReactiveCommand<string, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> BeginCreateCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCreateCommand { get; }
    public ReactiveCommand<Unit, Unit> BeginEditCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelEditCommand { get; }

    public RuleListViewModel(
        ISubscriptionService svc,
        IConfirmationService confirmationService,
        string topicName,
        string subscriptionName)
    {
        _svc = svc;
        _confirmationService = confirmationService;
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
                _source.Edit(list =>
                {
                    list.Clear();
                    list.AddRange(items);
                });
                return items;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return (IReadOnlyList<SubscriptionRule>)Array.Empty<SubscriptionRule>();
            }
            finally
            {
                IsLoading = false;
            }
        });

        var canCreate = this.WhenAnyValue(
            x => x.NewRuleName,
            x => x.NewRuleFilterKind,
            x => x.NewRuleExpression,
            (n, kind, expr) =>
                !string.IsNullOrWhiteSpace(n) &&
                (kind == RuleFilterKind.CatchAll || !string.IsNullOrWhiteSpace(expr)));

        CreateCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            Error = null;
            var opts = new CreateSubscriptionRuleOptions(
                NewRuleName.Trim(),
                NewRuleFilterKind,
                NewRuleFilterKind == RuleFilterKind.CatchAll ? null : NewRuleExpression,
                ActionExpression: null);
            var result = await _svc.CreateRuleAsync(_topicName, _subscriptionName, opts);
            PresentAdminResult(result);
            if (result.IsSuccess && result.Entity is not null)
            {
                _source.Add(result.Entity);
                ResetCreateForm();
                return;
            }

            Error = result.SafeMessage;
            await RefreshAuthoritativeAsync();
        }, canCreate);

        var canEdit = this.WhenAnyValue(
            x => x.SelectedRule,
            x => x.EditFilterKind,
            x => x.EditExpression,
            (sel, kind, expr) =>
                sel is not null &&
                (kind == RuleFilterKind.CatchAll || !string.IsNullOrWhiteSpace(expr)));

        SaveEditCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedRule is null) return;
            Error = null;
            var updated = SelectedRule with
            {
                FilterKind = EditFilterKind,
                FilterExpression = EditFilterKind == RuleFilterKind.CatchAll ? null : EditExpression
            };
            var result = await _svc.UpdateRuleAsync(_topicName, _subscriptionName, updated);
            PresentAdminResult(result);
            if (result.IsSuccess && result.Entity is not null)
            {
                ReplaceRule(result.Entity);
                IsEditing = false;
                SelectedRule = result.Entity;
                return;
            }

            if (result.Kind == EntityLifecycleKind.Conflict && result.Entity is not null)
            {
                ReplaceRule(result.Entity);
                SelectedRule = result.Entity;
                EditFilterKind = result.Entity.FilterKind;
                EditExpression = result.Entity.FilterExpression ?? "";
                Error = result.SafeMessage;
                return;
            }

            Error = result.SafeMessage;
            await RefreshAuthoritativeAsync();
        }, canEdit);

        DeleteCommand = ReactiveCommand.CreateFromTask<string, Unit>(async name =>
        {
            Error = null;
            var confirmation = await _confirmationService.ConfirmAsync(
                new ConfirmationRequest(
                    name,
                    Source: null,
                    "This subscription rule will be permanently deleted.",
                    ConfirmationRisk.Irreversible,
                    ConfirmActionLabel: "Delete"));
            if (confirmation != ConfirmationResult.Confirmed)
            {
                PresentAdminCancelled("Delete cancelled — rule was not deleted.");
                return Unit.Default;
            }

            var existing = _source.Items.FirstOrDefault(r => r.Name == name);
            var result = await _svc.DeleteRuleAsync(
                _topicName,
                _subscriptionName,
                name,
                existing?.ServiceVersion);
            PresentAdminResult(result);
            if (result.IsSuccess)
            {
                _source.Edit(list =>
                {
                    var item = list.FirstOrDefault(r => r.Name == name);
                    if (item != null) list.Remove(item);
                });
                return Unit.Default;
            }

            if (result.Kind == EntityLifecycleKind.Conflict && result.Entity is not null)
                ReplaceRule(result.Entity);

            Error = result.SafeMessage;
            await RefreshAuthoritativeAsync();
            return Unit.Default;
        });

        BeginCreateCommand = ReactiveCommand.Create(() =>
        {
            IsEditing = false;
            IsCreating = true;
            NewRuleFilterKind = RuleFilterKind.Sql;
            NewRuleExpression = "";
        });
        CancelCreateCommand = ReactiveCommand.Create(ResetCreateForm);
        BeginEditCommand = ReactiveCommand.Create(() =>
        {
            if (SelectedRule is null) return;
            IsCreating = false;
            IsEditing = true;
            EditFilterKind = SelectedRule.FilterKind;
            EditExpression = SelectedRule.FilterExpression ?? "";
        });
        CancelEditCommand = ReactiveCommand.Create(() => { IsEditing = false; });
    }

    private void ResetCreateForm()
    {
        IsCreating = false;
        NewRuleName = "";
        NewRuleExpression = "";
        NewRuleFilterKind = RuleFilterKind.Sql;
    }

    private void ReplaceRule(SubscriptionRule rule)
    {
        _source.Edit(list =>
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Name == rule.Name)
                {
                    list[i] = rule;
                    return;
                }
            }

            list.Add(rule);
        });
    }

    private void PresentAdminCancelled(string message)
    {
        IsAdminCancelled = true;
        AdminOutcomeKind = null;
        AdminStatus = message;
        Error = null;
        RaiseAdminFlags();
    }

    private void PresentAdminResult<T>(EntityLifecycleResult<T> result)
    {
        IsAdminCancelled = false;
        AdminOutcomeKind = result.Kind;
        AdminStatus = result.SafeMessage;
        if (!result.IsSuccess)
            Error = result.SafeMessage;
        RaiseAdminFlags();
    }

    private void RaiseAdminFlags()
    {
        this.RaisePropertyChanged(nameof(IsAdminConflict));
        this.RaisePropertyChanged(nameof(IsAdminStale));
        this.RaisePropertyChanged(nameof(IsAdminValidationFailed));
        this.RaisePropertyChanged(nameof(IsAdminFailed));
        this.RaisePropertyChanged(nameof(IsAdminSucceeded));
    }

    private async Task RefreshAuthoritativeAsync()
    {
        var items = await _svc.ListRulesAsync(_topicName, _subscriptionName);
        _source.Edit(list =>
        {
            list.Clear();
            list.AddRange(items);
        });
    }
}
