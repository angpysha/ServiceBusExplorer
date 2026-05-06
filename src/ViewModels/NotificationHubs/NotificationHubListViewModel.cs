using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class NotificationHubListViewModel : ReactiveObject
{
    private readonly INotificationHubService _svc;
    private readonly SourceList<NotificationHubInfo> _source = new();
    private bool _isLoading;
    private string? _error;
    private NotificationHubInfo? _selectedHub;
    private NotificationHubDetailViewModel? _selectedDetail;

    public ReadOnlyObservableCollection<NotificationHubInfo> NotificationHubs { get; }

    public NotificationHubInfo? SelectedHub
    {
        get => _selectedHub;
        set => this.RaiseAndSetIfChanged(ref _selectedHub, value);
    }

    public NotificationHubDetailViewModel? SelectedDetail
    {
        get => _selectedDetail;
        private set => this.RaiseAndSetIfChanged(ref _selectedDetail, value);
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

    public ReactiveCommand<Unit, IReadOnlyList<NotificationHubInfo>> RefreshCommand { get; }
    public ReactiveCommand<string, NotificationHubInfo> CreateCommand { get; }
    public ReactiveCommand<string, Unit> DeleteCommand { get; }

    public NotificationHubListViewModel(INotificationHubService svc)
    {
        _svc = svc;

        this.WhenAnyValue(x => x.SelectedHub)
            .Subscribe(h =>
            {
                var detail = h == null ? null : new NotificationHubDetailViewModel(_svc, h.Name);
                if (detail != null)
                    detail.NavigateBackRequested.Subscribe(_ => SelectedHub = null);
                SelectedDetail = detail;
            });

        _source.Connect()
            .Bind(out var bound)
            .Subscribe();
        NotificationHubs = bound;

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
                return (IReadOnlyList<NotificationHubInfo>)Array.Empty<NotificationHubInfo>();
            }
            finally
            {
                IsLoading = false;
            }
        });

        CreateCommand = ReactiveCommand.CreateFromTask<string, NotificationHubInfo>(async name =>
        {
            var created = await _svc.CreateAsync(name);
            _source.Add(created);
            return created;
        });

        DeleteCommand = ReactiveCommand.CreateFromTask<string, Unit>(async name =>
        {
            await _svc.DeleteAsync(name);
            _source.Edit(list =>
            {
                var item = list.FirstOrDefault(h => h.Name == name);
                if (item != null) list.Remove(item);
            });
            return Unit.Default;
        });
    }
}
