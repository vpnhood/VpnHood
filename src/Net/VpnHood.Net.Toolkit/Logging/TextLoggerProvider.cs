using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

// A destination's provider: a TextLogger per category over the destination's logger - one for every
// category, or one per category where the destination files by it (os_log) - with what the
// destination's lines need. So a destination is a plain logger and a line of provider.
public class TextLoggerProvider(
    Func<string, ILogger> createLogger,
    bool singleLine = true,
    bool includeTime = true,
    bool includeScopes = true)
    : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, TextLogger> _loggers = new();

    public TextLoggerProvider(ILogger logger, bool singleLine = true, bool includeTime = true, bool includeScopes = true)
        : this(_ => logger, singleLine, includeTime, includeScopes)
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName,
            name => new TextLogger(createLogger(name), singleLine, includeTime, includeScopes, name));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}
