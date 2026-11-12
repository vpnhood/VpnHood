using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Client.Devices.WindowsTun;

/// <summary>
/// Represents warnings and errors in the Application log: a Windows service's, as .NET's own hosts send them there, and
/// its window's.
/// </summary>
/// <param name="eventLog">The Application log, with the source to write under.</param>
/// <remarks>
/// The rest stays out — the service's in the app's log — since any signed-in user can read this one. The log stamps
/// each entry and shows its level, so its provider asks <c>TextLogger</c> for neither.
/// </remarks>
internal sealed class WinEventLogLogger(EventLog eventLog) : ILogger
{
    private const int MaxMessageLength = 31839; // the most one entry takes

    /// <summary>Begins no scope: the log's entries have none.</summary>
    /// <typeparam name="TState">The type of the scope's state.</typeparam>
    /// <param name="state">The scope's state.</param>
    /// <returns><see langword="null"/>.</returns>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    /// <summary>Determines whether a level is written: warnings, errors and critical ones are.</summary>
    /// <param name="logLevel">The level.</param>
    /// <returns><see langword="true"/> for a warning or above; otherwise, <see langword="false"/>.</returns>
    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel is >= LogLevel.Warning and < LogLevel.None;
    }

    /// <summary>
    /// Writes a warning or above to the log, as a warning or an error entry, cut to the most one entry takes; an entry
    /// Windows refuses is dropped, as the app's log has it.
    /// </summary>
    /// <typeparam name="TState">The type of the entry's state.</typeparam>
    /// <param name="logLevel">The entry's level.</param>
    /// <param name="eventId">The entry's event, which the entry leaves out.</param>
    /// <param name="state">The entry's state.</param>
    /// <param name="exception">The entry's exception, or <see langword="null"/>.</param>
    /// <param name="formatter">The function that makes the entry's text.</param>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception).TrimEnd();
        try {
            eventLog.WriteEntry(message.Length > MaxMessageLength ? message[..MaxMessageLength] : message,
                logLevel == LogLevel.Warning ? EventLogEntryType.Warning : EventLogEntryType.Error);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or SecurityException) {
            // an entry that cannot be written is not the caller's failure; the app's log has it
        }
    }
}
