using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class EventHubDetailViewModel : ReactiveObject
{
    private readonly IEventHubService _svc;
    private readonly SourceList<PartitionInfo> _partitionSource = new();
    private readonly SourceList<ConsumerGroupInfo> _consumerGroupSource = new();
    private EventHubInfo? _hub;
    private bool _isLoading;
    private string? _error;

    public EventHubInfo? Hub
    {
        get => _hub;
        private set => this.RaiseAndSetIfChanged(ref _hub, value);
    }

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

    public ReadOnlyObservableCollection<PartitionInfo> Partitions { get; }
    public ReadOnlyObservableCollection<ConsumerGroupInfo> ConsumerGroups { get; }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public EventHubDetailViewModel(IEventHubService svc)
    {
        _svc = svc;

        _partitionSource.Connect().Bind(out var partitions).Subscribe();
        _consumerGroupSource.Connect().Bind(out var consumerGroups).Subscribe();
        Partitions = partitions;
        ConsumerGroups = consumerGroups;

        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsLoading = true;
            Error = null;
            try
            {
                var (hub, parts, groups) = (
                    await _svc.GetAsync(),
                    await _svc.ListPartitionsAsync(),
                    await _svc.ListConsumerGroupsAsync());
                Hub = hub;
                _partitionSource.Edit(list => { list.Clear(); list.AddRange(parts); });
                _consumerGroupSource.Edit(list => { list.Clear(); list.AddRange(groups); });
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        });
    }
}
