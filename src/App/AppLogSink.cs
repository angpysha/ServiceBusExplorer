using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace ServiceBusExplorer.App;

public record LogEntry(DateTime Timestamp, string Level, string Category, string Message);

/// ILogger implementation that writes to an in-memory ObservableCollection.
public sealed class ObservableLogger(string category, ObservableCollection<LogEntry> entries) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var msg = formatter(state, exception);
        if (exception != null) msg += $"\n{exception.Message}";
        var entry = new LogEntry(DateTime.Now, logLevel.ToString(), category, msg);

        // Avalonia's thread dispatcher may not be available at startup — enqueue safely
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            Append(entry);
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Append(entry));
    }

    private void Append(LogEntry entry)
    {
        entries.Add(entry);
        // Cap at 500 entries to avoid unbounded growth
        while (entries.Count > 500)
            entries.RemoveAt(0);
    }
}

[ProviderAlias("Observable")]
public sealed class ObservableLoggerProvider : ILoggerProvider
{
    public readonly ObservableCollection<LogEntry> Entries = new();

    public ILogger CreateLogger(string categoryName) =>
        new ObservableLogger(categoryName, Entries);

    public void Dispose() { }
}
