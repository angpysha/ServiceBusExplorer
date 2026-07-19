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
    private readonly IQueueService _queueSvc;
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

    public ReadOnlyObservableCollection<TopicInfo> Topics { get; }

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
        IQueueService queueSvc,
        IConfirmationService confirmationService,
        LiveConnectionContext? liveContext = null)
    {
        _namespaceService = namespaceService;
        _svc = svc;
        _subSvc = subSvc;
        _queueSvc = queueSvc;
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
                        _queueSvc,
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
            var created = await _svc.CreateAsync(opts);
            _source.Add(created);
            return created;
        });

        DeleteCommand = ReactiveCommand.CreateFromTask<string, Unit>(async name =>
        {
            await _svc.DeleteAsync(name);
            _source.Edit(list =>
            {
                var item = list.FirstOrDefault(t => t.Name == name);
                if (item != null) list.Remove(item);
            });
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
            var created = await _svc.CreateAsync(new CreateTopicOptions(NewTopicName));
            _source.Add(created);
            IsCreating = false;
            NewTopicName = "";
        }, canQuickCreate);
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
