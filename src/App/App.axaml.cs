using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ServiceBusExplorer.App.Views;

namespace ServiceBusExplorer.App;

public class App : Application
{
    private AppBootstrapper? _bootstrapper;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        _bootstrapper = new AppBootstrapper();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(_bootstrapper)
            };
            desktop.Exit += (_, _) => _bootstrapper.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
