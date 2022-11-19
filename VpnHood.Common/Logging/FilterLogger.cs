using System;
using Microsoft.Extensions.Logging;

namespace VpnHood.Common.Logging;

public class FilterLogger : ILogger
{
    private readonly Func<EventId, bool> _eventFilter;
    private readonly ILogger _logger;

    public FilterLogger(ILogger logger, Func<EventId, bool> eventFilter)
    {
        _logger = logger;
        _eventFilter = eventFilter;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _logger.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _logger.IsEnabled(logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (_eventFilter(eventId))
            _logger.Log(logLevel, eventId, state, exception, formatter);
    }
}