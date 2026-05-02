using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class QueueListViewModel : ReactiveObject
{
    private readonly IQueueService _svc;
    private readonly SourceList<QueueInfo> _source = new();
    private bool _isLoading;
    private string? _error;

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

    public ReactiveCommand<Unit, IReadOnlyList<QueueInfo>> RefreshCommand { get; }
    public ReactiveCommand<CreateQueueOptions, QueueInfo> CreateCommand { get; }
    public ReactiveCommand<string, Unit> DeleteCommand { get; }

    public QueueListViewModel(IQueueService svc)
    {
        _svc = svc;

        _source.Connect()
            .Bind(out var bound)
            .Subscribe();
        Queues = bound;

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
    }
}
