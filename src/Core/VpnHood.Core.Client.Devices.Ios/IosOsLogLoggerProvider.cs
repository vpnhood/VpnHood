using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using OSLogHandle = CoreFoundation.OSLog;
using OSLogLevel = CoreFoundation.OSLogLevel;

namespace VpnHood.Core.Client.Devices.Ios;

// Apple's unified log (os_log) as a sink, under the given subsystem, where the lines are visible in
// Console.app and `log stream --device`. An iOS Network Extension has no usable stdout/stderr (they go to
// /dev/null) and System.Diagnostics.Trace is silent there, so the extension adds this at its start
// (IosVpnService). One os_log handle per logger category: the MEL category name doubles as the os_log
// category, so unified-log lines are filterable by both subsystem (the extension) and category;
// creating a handle is cheap (a value bound to subsystem/category, nothing to release).
public sealed class IosOsLogLoggerProvider(string subsystem, bool includeScopes = true)
    : TextLoggerProvider(name => new OsLogLogger(new OSLogHandle(subsystem, name)), includeScopes: includeScopes)
{
    private sealed class OsLogLogger(OSLogHandle osLog) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            try {
                // os_log requires a constant format string; the .NET iOS binding passes the text as a
                // %{public}s argument, so the content is not redacted and needs no %-escaping.
                osLog.Log(ToOsLogLevel(logLevel), formatter(state, exception));
            }
            catch {
                // os_log is best-effort; never let device logging throw into the caller.
            }
        }

        // Map to native severities. Info/Debug are not persisted by default in the unified log (use
        // `log stream --level info` to watch live); Warning+ are persisted. The durable record is the
        // LogToFile copy, so the non-persisted live levels are intentional.
        private static OSLogLevel ToOsLogLevel(LogLevel logLevel) => logLevel switch {
            LogLevel.Trace => OSLogLevel.Debug,
            LogLevel.Debug => OSLogLevel.Debug,
            LogLevel.Information => OSLogLevel.Info,
            LogLevel.Warning => OSLogLevel.Default,
            LogLevel.Error => OSLogLevel.Error,
            LogLevel.Critical => OSLogLevel.Fault,
            _ => OSLogLevel.Default
        };
    }
}
