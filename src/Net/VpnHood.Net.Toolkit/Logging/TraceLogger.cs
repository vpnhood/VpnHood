using Microsoft.Extensions.Logging;

namespace VpnHood.Net.Toolkit.Logging;

// System.Diagnostics.Trace: a debugger's output, logcat on Android. Off on Linux, where .NET sends
// Trace to syslog from every run and the daemon's console is the journal's copy.
public sealed class TraceLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return !OperatingSystem.IsLinux();
    }

    // Trace serializes its writers itself
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
            System.Diagnostics.Trace.WriteLine(formatter(state, exception));
    }
}
