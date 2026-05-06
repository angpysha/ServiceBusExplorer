using System.Reactive;
using System.Reactive.Subjects;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class NotificationHubDetailViewModel : ReactiveObject
{
    private readonly INotificationHubService _svc;
    private readonly string _hubName;
    private readonly Subject<Unit> _navigateBack = new();
    private NotificationHubInfo? _info;
    private bool _isLoading;
    private string? _error;

    public NotificationHubInfo? Info
    {
        get => _info;
        private set => this.RaiseAndSetIfChanged(ref _info, value);
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

    public IObservable<Unit> NavigateBackRequested => _navigateBack;
    public ReactiveCommand<Unit, Unit> NavigateBackCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

    public NotificationHubDetailViewModel(INotificationHubService svc, string hubName)
    {
        _svc = svc;
        _hubName = hubName;

        NavigateBackCommand = ReactiveCommand.Create(() => _navigateBack.OnNext(Unit.Default));

        RefreshCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsLoading = true;
            Error = null;
            try
            {
                Info = await _svc.GetAsync(_hubName);
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

        DeleteCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await _svc.DeleteAsync(_hubName);
            _navigateBack.OnNext(Unit.Default);
        });

        RefreshCommand.Execute().Subscribe();
    }
}
