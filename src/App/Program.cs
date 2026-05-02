using Avalonia;
using Avalonia.ReactiveUI;
using ServiceBusExplorer.App;

AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .LogToTrace()
    .UseReactiveUI()
    .StartWithClassicDesktopLifetime(args);
