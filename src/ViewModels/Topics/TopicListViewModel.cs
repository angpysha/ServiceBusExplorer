using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class TopicListViewModel : ReactiveObject
{
    private readonly ITopicService _svc;
    private readonly SourceList<TopicInfo> _source = new();
    private bool _isLoading;
    private string? _error;

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

    public ReactiveCommand<Unit, IReadOnlyList<TopicInfo>> RefreshCommand { get; }
    public ReactiveCommand<CreateTopicOptions, TopicInfo> CreateCommand { get; }
    public ReactiveCommand<string, Unit> DeleteCommand { get; }

    public TopicListViewModel(ITopicService svc)
    {
        _svc = svc;

        _source.Connect()
            .Bind(out var bound)
            .Subscribe();
        Topics = bound;

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
    }
}
