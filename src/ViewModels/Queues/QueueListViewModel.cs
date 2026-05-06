using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class QueueListViewModel : ReactiveObject
{
    private readonly IQueueService _svc;
    private readonly SourceList<QueueInfo> _source = new();
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

    public QueueListViewModel(IQueueService svc)
    {
        _svc = svc;

        _source.Connect()
            .Bind(out var bound)
            .Subscribe();
        Queues = bound;

        this.WhenAnyValue(x => x.SelectedQueue)
            .Subscribe(q =>
            {
                var detail = q == null ? null : new QueueDetailViewModel(_svc, q.Name);
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
}
