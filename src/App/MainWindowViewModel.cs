using System.Reactive;
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

    public MainWindowViewModel(AppBootstrapper bootstrapper)
    {
        _bootstrapper = bootstrapper;
        _connectVm = bootstrapper.GetAppService<ConnectViewModel>();
        _currentPage = _connectVm;
        LogSink = bootstrapper.LogSink;

        // Populate connection history from disk
        var settings = bootstrapper.Settings.Load();
        foreach (var cs in settings.ConnectionHistory)
            _connectVm.ConnectionHistory.Add(cs);

        _connectVm.ConnectCommand.Subscribe(opts =>
        {
            _connectVm.IsConnecting = true;
            _connectVm.ErrorMessage = null;

            bootstrapper.ConnectAsync(opts).ContinueWith(task =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _connectVm.IsConnecting = false;
                    if (task.IsFaulted)
                    {
                        _connectVm.ErrorMessage = task.Exception?.InnerException?.Message
                            ?? "Connection failed.";
                    }
                    else
                    {
                        var appMainVm = task.Result;
                        appMainVm.DisconnectCommand = DisconnectCommand;
                        CurrentPage = appMainVm;
                        appMainVm.Dashboard.RefreshCommand.Execute().Subscribe();

                        // Keep history in sync with what was just saved
                        RefreshHistory();
                    }
                });
            });
        });

        DisconnectCommand = ReactiveCommand.Create(() =>
        {
            CurrentPage = _connectVm;
        });
    }

    private void RefreshHistory()
    {
        var settings = _bootstrapper.Settings.Load();
        _connectVm.ConnectionHistory.Clear();
        foreach (var cs in settings.ConnectionHistory)
            _connectVm.ConnectionHistory.Add(cs);
    }
}
