using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Toolkit.Logging;

public class FileLoggerProvider(
    string filePath,
    bool includeScopes = true,
    bool autoFlush = false)
    : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(
            filePath: filePath, includeScopes: includeScopes, autoFlush: autoFlush, categoryName: name));
    }

    public void Dispose()
    {
        // Dispose all loggers
        foreach (var logger in _loggers.Values)
            logger.Dispose(); 

        // Clear loggers
        _loggers.Clear();
    }
}