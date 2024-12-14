using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Common.Logging;

public class FilterLogger(ILogger logger, Func<EventId, bool> eventFilter) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return logger.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logger.IsEnabled(logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (eventFilter(eventId))
            logger.Log(logLevel, eventId, state, exception, formatter);
    }
}