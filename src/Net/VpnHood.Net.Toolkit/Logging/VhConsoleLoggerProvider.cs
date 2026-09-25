using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

public class VhConsoleLoggerProvider(bool includeScopes = true)
    : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, VhConsoleLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new VhConsoleLogger(
            includeScopes: includeScopes, categoryName: name));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}