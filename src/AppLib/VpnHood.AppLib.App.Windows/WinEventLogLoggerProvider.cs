using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.App.Windows;

// The Application log as a sink, for the process's life, under the service's name - the source its
// own start and stop entries have: the service's, and its window's, which runs as the person and may
// write to that source once the service's setup has registered it. One logger serves every category,
// without the level word and the scopes: the entries name the service, not a category, and show
// their level.
public sealed class WinEventLogLoggerProvider(string sourceName) : ILoggerProvider
{
    private readonly EventLog _eventLog = new("Application") { Source = sourceName };
    private TextLogger? _logger;

    public ILogger CreateLogger(string categoryName)
    {
        return _logger ??= new TextLogger(new WinEventLogLogger(_eventLog),
            singleLine: false, includeTime: false, includeScopes: false);
    }

    public void Dispose()
    {
        _eventLog.Dispose();
    }
}
