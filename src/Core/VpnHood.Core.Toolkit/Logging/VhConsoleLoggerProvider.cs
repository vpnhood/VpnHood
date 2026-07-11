using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Toolkit.Logging;

public class VhConsoleLoggerProvider(bool includeScopes = true, bool singleLine = true)
    : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, VhConsoleLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new VhConsoleLogger(
            includeScopes: includeScopes, singleLine: singleLine, categoryName: name));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}