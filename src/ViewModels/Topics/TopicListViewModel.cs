using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class TopicListViewModel : ReactiveObject
{
    private readonly ITopicService _svc;
    private readonly ISubscriptionService _subSvc;
    private readonly IQueueService _queueSvc;
    private readonly SourceList<TopicInfo> _source = new();
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

    public TopicListViewModel(ITopicService svc, ISubscriptionService subSvc, IQueueService queueSvc)
    {
        _svc = svc;
        _subSvc = subSvc;
        _queueSvc = queueSvc;

        _source.Connect()
            .Bind(out var bound)
            .Subscribe();
        Topics = bound;

        this.WhenAnyValue(x => x.SelectedTopic)
            .Subscribe(t =>
            {
                var detail = t == null ? null : new TopicDetailViewModel(_svc, _subSvc, _queueSvc, t.Name);
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
                var items = await _svc.ListAsync();
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
}
