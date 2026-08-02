using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class TopicListViewModel : ReactiveObject
{
    private readonly INamespaceService _namespaceService;
    private readonly ITopicService _svc;
    private readonly ISubscriptionService _subSvc;
    private readonly IMessageBrowseService _browseService;
    private readonly IMessageSendService _sendService;
    private readonly IMessageReceiveService _receiveService;
    private readonly IPurgeService _purgeService;
    private readonly IConfirmationService _confirmationService;
    private readonly SourceList<TopicInfo> _source = new();
    private ConnectionScope _scope = ConnectionScope.Namespace;
    private CapabilitySet _capabilities = CapabilitySet.ForNamespaceScope(adminProbeSucceeded: false);
    private string? _entityPath;
    private ScopedEntityKind _entityKind = ScopedEntityKind.None;
    private bool _isLoading;
    private string? _error;
    private TopicInfo? _selectedTopic;
    private TopicDetailViewModel? _selectedDetail;
    private bool _isCreating;
    private string _newTopicName = "";
    private string? _adminStatus;
    private EntityLifecycleKind? _adminOutcomeKind;
    private bool _isAdminCancelled;

    public ReadOnlyObservableCollection<TopicInfo> Topics { get; }

    /// <summary>Last administration outcome presentation (create/delete validation, auth, conflict, success).</summary>
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

    public TopicInfo? SelectedTopic
    {
        get => _selectedTopic;
        set => this.RaiseAndSetIfChanged(ref _selectedTopic, value);
    }

    public TopicDetailViewModel? SelectedDetail
    {
        get => _selectedDetail;
        private set => this.RaiseAndSetIfChanged(ref _selectedDetail, value);
    }

    public bool IsCreating
    {
        get => _isCreating;
        set => this.RaiseAndSetIfChanged(ref _isCreating, value);
    }

    public string NewTopicName
    {
        get => _newTopicName;
        set => this.RaiseAndSetIfChanged(ref _newTopicName, value);
    }

    public ReactiveCommand<Unit, IReadOnlyList<TopicInfo>> RefreshCommand { get; }
    public ReactiveCommand<CreateTopicOptions, TopicInfo> CreateCommand { get; }
    public ReactiveCommand<string, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> BeginCreateCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCreateCommand { get; }
    public ReactiveCommand<Unit, Unit> QuickCreateCommand { get; }

    public TopicListViewModel(
        INamespaceService namespaceService,
        ITopicService svc,
        ISubscriptionService subSvc,
        IMessageBrowseService browseService,
        IMessageSendService sendService,
        IMessageReceiveService receiveService,
        IPurgeService purgeService,
        IConfirmationService confirmationService,
        LiveConnectionContext? liveContext = null)
    {
        _namespaceService = namespaceService;
        _svc = svc;
        _subSvc = subSvc;
        _browseService = browseService;
        _sendService = sendService;
        _receiveService = receiveService;
        _purgeService = purgeService;
        _confirmationService = confirmationService;

        if (liveContext is not null)
            ApplyConnectionScope(
                liveContext.Scope,
                liveContext.EntityPath,
                liveContext.Capabilities);

        _source.Connect()
            .Bind(out var bound)
            .Subscribe();
        Topics = bound;

        this.WhenAnyValue(x => x.SelectedTopic)
            .Subscribe(t =>
            {
                var detail = t == null
                    ? null
                    : new TopicDetailViewModel(
                        _svc,
                        _subSvc,
                        _browseService,
                        _sendService,
                        _receiveService,
                        _purgeService,
                        _confirmationService,
                        t.Name);
                if (detail != null)
                    detail.NavigateBackRequested.Subscribe(_ => SelectedTopic = null);
                SelectedDetail = detail;
            });

        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsLoading = true;
            Error = null;
            ClearAdminPresentation();
            try
            {
                var result = await _namespaceService.BrowseTopicsAsync(
                    new NamespaceBrowseRequest(
                        _scope,
                        _entityPath,
                        _capabilities,
                        BrowseSurface.Topics,
                        _entityKind));

                _source.Edit(list =>
                {
                    list.Clear();
                    list.AddRange(result.Items);
                });
                Error = result.GuidanceMessage;
                return result.Items;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                _source.Edit(list => list.Clear());
                return (IReadOnlyList<TopicInfo>)Array.Empty<TopicInfo>();
            }
            finally
            {
                IsLoading = false;
            }
        });

        CreateCommand = ReactiveCommand.CreateFromTask<CreateTopicOptions, TopicInfo>(async opts =>
        {
            var result = await _svc.CreateAsync(opts);
            PresentAdminResult(result);
            if (!result.IsSuccess || result.Entity is null)
                throw new InvalidOperationException(result.SafeMessage);
            _source.Add(result.Entity);
            return result.Entity;
        });

        DeleteCommand = ReactiveCommand.CreateFromTask<string, Unit>(async name =>
        {
            var confirmation = await _confirmationService.ConfirmAsync(
                new ConfirmationRequest(
                    name,
                    Source: null,
                    "This topic and its subscriptions will be permanently deleted.",
                    ConfirmationRisk.Irreversible,
                    ConfirmActionLabel: "Delete"));
            if (confirmation != ConfirmationResult.Confirmed)
            {
                PresentAdminCancelled("Delete cancelled — topic was not deleted.");
                return Unit.Default;
            }

            var result = await _svc.DeleteAsync(name);
            PresentAdminResult(result);
            if (result.IsSuccess)
            {
                _source.Edit(list =>
                {
                    var item = list.FirstOrDefault(t => t.Name == name);
                    if (item != null) list.Remove(item);
                });
                if (SelectedTopic?.Name == name)
                    SelectedTopic = null;
                return Unit.Default;
            }

            if (result.Kind == EntityLifecycleKind.Conflict && result.Entity is not null)
                ReplaceTopic(result.Entity);

            return Unit.Default;
        });

        BeginCreateCommand = ReactiveCommand.Create(() => { IsCreating = true; });
        CancelCreateCommand = ReactiveCommand.Create(() =>
        {
            IsCreating = false;
            NewTopicName = "";
        });

        var canQuickCreate = this.WhenAnyValue(x => x.NewTopicName,
            n => !string.IsNullOrWhiteSpace(n));
        QuickCreateCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var result = await _svc.CreateAsync(new CreateTopicOptions(NewTopicName));
            PresentAdminResult(result);
            if (!result.IsSuccess || result.Entity is null)
                return;

            _source.Add(result.Entity);
            IsCreating = false;
            NewTopicName = "";
        }, canQuickCreate);
    }

    private void ReplaceTopic(TopicInfo topic)
    {
        _source.Edit(list =>
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Name == topic.Name)
                {
                    list[i] = topic;
                    return;
                }
            }

            list.Add(topic);
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
        Error = result.IsSuccess ? null : result.SafeMessage;
        RaiseAdminFlags();
    }

    private void ClearAdminPresentation()
    {
        IsAdminCancelled = false;
        AdminOutcomeKind = null;
        AdminStatus = null;
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

    public void ApplyConnectionScope(
        ConnectionScope scope,
        string? entityPath,
        CapabilitySet capabilities,
        ScopedEntityKind entityKind = ScopedEntityKind.None)
    {
        _scope = scope;
        _entityPath = entityPath;
        _capabilities = capabilities;
        _entityKind = EntityScopeHelper.ParseKind(entityPath, entityKind);
    }

    public void ClearBrowseResults()
    {
        _source.Edit(list => list.Clear());
        Error = null;
    }
}
