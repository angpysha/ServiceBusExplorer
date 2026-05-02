using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class RelayListViewModel : ReactiveObject
{
    private readonly IRelayService _svc;
    private readonly SourceList<RelayInfo> _source = new();
    private bool _isLoading;
    private string? _error;

    public ReadOnlyObservableCollection<RelayInfo> Relays { get; }

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

    public ReactiveCommand<Unit, IReadOnlyList<RelayInfo>> RefreshCommand { get; }

    public RelayListViewModel(IRelayService svc)
    {
        _svc = svc;

        _source.Connect()
            .Bind(out var bound)
            .Subscribe();
        Relays = bound;

        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsLoading = true;
            Error = null;
            try
            {
                var items = await _svc.ListAsync();
                _source.Edit(list => { list.Clear(); list.AddRange(items); });
                return items;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return (IReadOnlyList<RelayInfo>)Array.Empty<RelayInfo>();
            }
            finally
            {
                IsLoading = false;
            }
        });
    }
}
