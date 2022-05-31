using System;
using Microsoft.Extensions.Logging;

namespace VpnHood.Common.Logging;

public class SyncLogger : ILogger
{
    private readonly object _lock = new();
    private readonly ILogger _logger;

    public SyncLogger(ILogger logger)
    {
        _logger = logger;
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        lock (_lock)
        {
            return _logger.BeginScope(state);
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        lock (_lock)
            return _logger.IsEnabled(logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_lock)
        {
            _logger.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}