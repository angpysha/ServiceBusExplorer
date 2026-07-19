using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class QueueListViewModel : ReactiveObject
{
    private readonly INamespaceService _namespaceService;
    private readonly IQueueService _svc;
    private readonly IMessageBrowseService _browseService;
    private readonly IMessageSendService _sendService;
    private readonly IMessageReceiveService _receiveService;
    private readonly IPurgeService _purgeService;
    private readonly IConfirmationService _confirmationService;
    private readonly SourceList<QueueInfo> _source = new();
    private ConnectionScope _scope = ConnectionScope.Namespace;
    private CapabilitySet _capabilities = CapabilitySet.ForNamespaceScope(adminProbeSucceeded: false);
    private string? _entityPath;
    private ScopedEntityKind _entityKind = ScopedEntityKind.None;
    private bool _isLoading;
    private string? _error;
    private QueueInfo? _selectedQueue;
    private QueueDetailViewModel? _selectedDetail;
    private bool _isCreating;
    private string _newQueueName = "";

    public ReadOnlyObservableCollection<QueueInfo> Queues { get; }

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

    public QueueInfo? SelectedQueue
    {
        get => _selectedQueue;
        set => this.RaiseAndSetIfChanged(ref _selectedQueue, value);
    }

    public QueueDetailViewModel? SelectedDetail
    {
        get => _selectedDetail;
        private set => this.RaiseAndSetIfChanged(ref _selectedDetail, value);
    }

    public bool IsCreating
    {
        get => _isCreating;
        set => this.RaiseAndSetIfChanged(ref _isCreating, value);
    }

    public string NewQueueName
    {
        get => _newQueueName;
        set => this.RaiseAndSetIfChanged(ref _newQueueName, value);
    }

    public ReactiveCommand<Unit, IReadOnlyList<QueueInfo>> RefreshCommand { get; }
    public ReactiveCommand<CreateQueueOptions, QueueInfo> CreateCommand { get; }
    public ReactiveCommand<string, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> BeginCreateCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCreateCommand { get; }
    public ReactiveCommand<Unit, Unit> QuickCreateCommand { get; }

    public QueueListViewModel(
        INamespaceService namespaceService,
        IQueueService svc,
        IMessageBrowseService browseService,
        IMessageSendService sendService,
        IMessageReceiveService receiveService,
        IPurgeService purgeService,
        IConfirmationService confirmationService,
        LiveConnectionContext? liveContext = null)
    {
        _namespaceService = namespaceService;
        _svc = svc;
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
        Queues = bound;

        this.WhenAnyValue(x => x.SelectedQueue)
            .Subscribe(q =>
            {
                var detail = q == null
                    ? null
                    : new QueueDetailViewModel(_svc, _browseService, _sendService, _receiveService, _purgeService, _confirmationService, q.Name);
                if (detail != null)
                    detail.NavigateBackRequested.Subscribe(_ => SelectedQueue = null);
                SelectedDetail = detail;
            });

        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsLoading = true;
            Error = null;
            try
            {
                var result = await _namespaceService.BrowseQueuesAsync(
                    new NamespaceBrowseRequest(
                        _scope,
                        _entityPath,
                        _capabilities,
                        BrowseSurface.Queues,
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
                return (IReadOnlyList<QueueInfo>)Array.Empty<QueueInfo>();
            }
            finally
            {
                IsLoading = false;
            }
        });

        CreateCommand = ReactiveCommand.CreateFromTask<CreateQueueOptions, QueueInfo>(async opts =>
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
                var item = list.FirstOrDefault(q => q.Name == name);
                if (item != null) list.Remove(item);
            });
            return Unit.Default;
        });

        BeginCreateCommand = ReactiveCommand.Create(() => { IsCreating = true; });
        CancelCreateCommand = ReactiveCommand.Create(() =>
        {
            IsCreating = false;
            NewQueueName = "";
        });

        var canQuickCreate = this.WhenAnyValue(x => x.NewQueueName,
            n => !string.IsNullOrWhiteSpace(n));
        QuickCreateCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var created = await _svc.CreateAsync(new CreateQueueOptions(NewQueueName));
            _source.Add(created);
            IsCreating = false;
            NewQueueName = "";
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
