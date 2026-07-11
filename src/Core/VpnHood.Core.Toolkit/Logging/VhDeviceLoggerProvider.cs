using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Toolkit.Logging;

public class VhDeviceLoggerProvider(bool includeScopes = true)
    : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, VhDeviceLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new VhDeviceLogger(
            includeScopes: includeScopes, categoryName: name));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}