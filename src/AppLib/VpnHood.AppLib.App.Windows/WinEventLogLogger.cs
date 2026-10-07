using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using Microsoft.Extensions.Logging;

namespace VpnHood.AppLib.App.Windows;

// Warnings and errors in the Application log: a Windows service's, as .NET's own hosts send them
// there, and its window's. The rest stays out - the service's in the app's log - since any signed-in
// user can read this one. The log stamps each entry and shows its level, so its provider asks
// TextLogger for neither.
public sealed class WinEventLogLogger(EventLog eventLog) : ILogger
{
    private const int MaxMessageLength = 31839; // the most one entry takes

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel is >= LogLevel.Warning and < LogLevel.None;
    }

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
