using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class EventHubListViewModel : ReactiveObject
{
    private readonly IEventHubService _svc;
    private readonly SourceList<EventHubInfo> _source = new();
    private bool _isLoading;
    private string? _error;

    public ReadOnlyObservableCollection<EventHubInfo> EventHubs { get; }
    public EventHubDetailViewModel Detail { get; }

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

    public ReactiveCommand<Unit, EventHubInfo?> RefreshCommand { get; }

    public EventHubListViewModel(IEventHubService svc, EventHubDetailViewModel detail)
    {
        _svc = svc;
        Detail = detail;

        _source.Connect()
            .Bind(out var bound)
            .Subscribe();
        EventHubs = bound;

        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsLoading = true;
            Error = null;
            try
            {
                var hub = await _svc.GetAsync();
                _source.Edit(list =>
                {
                    list.Clear();
                    list.Add(hub);
                });
                Detail.RefreshCommand.Execute().Subscribe();
                return (EventHubInfo?)hub;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return null;
            }
            finally
            {
                IsLoading = false;
            }
        });
    }
}
