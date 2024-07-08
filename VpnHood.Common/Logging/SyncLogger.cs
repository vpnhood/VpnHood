using Microsoft.Extensions.Logging;

namespace VpnHood.Common.Logging;

public class SyncLogger(ILogger logger) : ILogger
{
    private readonly object _lock = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        lock (_lock)
            return logger.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        lock (_lock)
            return logger.IsEnabled(logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_lock)
        {
            try
            {
                logger.Log(logLevel, eventId, state, exception, formatter);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Logger error! Could not write into logger. Error: {ex.Message}");
            }
        }
    }
}