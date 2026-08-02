using System.Reactive;
using System.Reflection;
using Avalonia.Threading;
using ReactiveUI;
using ServiceBusExplorer.ViewModels;

namespace ServiceBusExplorer.App;

public class MainWindowViewModel : ReactiveObject
{
    private readonly AppBootstrapper _bootstrapper;
    private readonly ConnectViewModel _connectVm;
    private ReactiveObject _currentPage;

    public ReactiveObject CurrentPage
    {
        get => _currentPage;
        private set => this.RaiseAndSetIfChanged(ref _currentPage, value);
    }

    public ReactiveCommand<Unit, Unit> DisconnectCommand { get; }
    public ObservableLoggerProvider LogSink { get; }
    public string BuildRevision { get; } =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";
    public string InternalLimitations { get; } =
        "Restricted evaluator build: feature parity is incomplete; SAS vault saving is opt-in when the platform vault is available.";

    public MainWindowViewModel(AppBootstrapper bootstrapper)
    {
        _bootstrapper = bootstrapper;
        _connectVm = bootstrapper.GetAppService<ConnectViewModel>();
        _currentPage = _connectVm;
        LogSink = bootstrapper.LogSink;

        _ = _connectVm.InitializeAsync();

        _ = LoadHistoryAsync();

        _connectVm.ConnectCommand.Subscribe(request =>
        {
            _connectVm.IsConnecting = true;
            _connectVm.ErrorMessage = null;

            _bootstrapper.ConnectAsync(request, _connectVm).ContinueWith(async task =>
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    _connectVm.IsConnecting = false;
                    if (task.IsFaulted)
                    {
                        _connectVm.ErrorMessage = task.Exception?.InnerException?.Message
                            ?? "Connection failed.";
                    }
                    else
                    {
                        var (appMainVm, profileId) = task.Result;
                        await _connectVm.HandlePostConnectVaultAsync(profileId).ConfigureAwait(true);
                        appMainVm.DisconnectCommand = DisconnectCommand;
                        CurrentPage = appMainVm;
                        appMainVm.Dashboard.RefreshCommand.Execute().Subscribe();

                        await RefreshHistoryAsync().ConfigureAwait(true);
                    }
                });
            });
        });

        DisconnectCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await _bootstrapper.DisconnectAsync().ConfigureAwait(true);
            CurrentPage = _connectVm;
        });
    }

    private async Task LoadHistoryAsync()
    {
        var store = _bootstrapper.GetAppService<IConnectionProfileStore>();
        var profiles = await store.ListAsync().ConfigureAwait(true);
        _connectVm.ConnectionHistory.Clear();
        foreach (var profile in profiles)
            _connectVm.ConnectionHistory.Add(profile);
    }

    private async Task RefreshHistoryAsync()
    {
        await LoadHistoryAsync().ConfigureAwait(true);
    }
}
