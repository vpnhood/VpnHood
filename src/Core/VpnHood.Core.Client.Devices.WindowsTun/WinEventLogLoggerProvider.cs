using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.Devices.WindowsTun;

/// <summary>
/// Provides the Application log as a sink, for the process's life, under the service's name: the source its own start
/// and stop entries have.
/// </summary>
/// <param name="sourceName">
/// The service's name: the service's process and its window's, which runs as the person and may write to that source
/// once the service's setup has registered it.
/// </param>
/// <remarks>
/// One logger serves every category, without the level word and the scopes: the entries name the service, not a
/// category, and show their level.
/// </remarks>
public sealed class WinEventLogLoggerProvider(string sourceName) : ILoggerProvider
{
    private readonly EventLog _eventLog = new("Application") { Source = sourceName };
    private TextLogger? _logger;

    /// <summary>Gets the one logger, which serves every category.</summary>
    /// <param name="categoryName">The category, which the entries leave out.</param>
    /// <returns>The logger.</returns>
    public ILogger CreateLogger(string categoryName)
    {
        return _logger ??= new TextLogger(new WinEventLogLogger(_eventLog),
            singleLine: false, includeTime: false, includeScopes: false);
    }

    /// <summary>Closes the Application log.</summary>
    public void Dispose()
    {
        _eventLog.Dispose();
    }
}
