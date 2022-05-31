using System;
using Microsoft.Extensions.Logging;

namespace VpnHood.App.Launcher;

internal class SimpleLogger : ILogger
{
    private readonly LoggerExternalScopeProvider _scopeProvider = new();

    public IDisposable BeginScope<TState>(TState state)
    {
        return _scopeProvider.Push(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Console.WriteLine(message);
    }
}