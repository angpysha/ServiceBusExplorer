using Avalonia.Threading;
using ReactiveUI;
using ServiceBusExplorer.ViewModels;

namespace ServiceBusExplorer.App;

public class MainWindowViewModel : ReactiveObject
{
    private readonly AppBootstrapper _bootstrapper;
    private ReactiveObject _currentPage;

    public ReactiveObject CurrentPage
    {
        get => _currentPage;
        private set => this.RaiseAndSetIfChanged(ref _currentPage, value);
    }

    public MainWindowViewModel(AppBootstrapper bootstrapper)
    {
        _bootstrapper = bootstrapper;
        var connectVm = bootstrapper.GetAppService<ConnectViewModel>();
        _currentPage = connectVm;

        connectVm.ConnectCommand.Subscribe(opts =>
        {
            connectVm.IsConnecting = true;
            connectVm.ErrorMessage = null;

            bootstrapper.ConnectAsync(opts).ContinueWith(task =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    connectVm.IsConnecting = false;
                    if (task.IsFaulted)
                    {
                        connectVm.ErrorMessage = task.Exception?.InnerException?.Message
                            ?? "Connection failed.";
                    }
                    else
                    {
                        var appMainVm = task.Result;
                        CurrentPage = appMainVm;
                        appMainVm.Dashboard.RefreshCommand.Execute().Subscribe();
                    }
                });
            });
        });
    }
}
