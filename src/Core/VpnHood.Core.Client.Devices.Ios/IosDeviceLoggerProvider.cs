using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using OSLogHandle = CoreFoundation.OSLog;
using OSLogLevel = CoreFoundation.OSLogLevel;

namespace VpnHood.Core.Client.Devices.Ios;

// The "device" ILoggerProvider for iOS. An iOS Network Extension has no usable stdout/stderr (they go to
// /dev/null), so the default VhDeviceLoggerProvider (System.Diagnostics.Trace) is silent here. This routes
// LogToDevice output to Apple's unified log (os_log) under the given subsystem, where it is visible in
// Console.app and `log stream --device`. Pass an instance of this as the LogService deviceLoggerProviderFactory.
public sealed class IosDeviceLoggerProvider(string subsystem, bool includeScopes = true)
    : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, IosDeviceLogger> _loggers = new();

    // One os_log handle per logger category: the MEL category name doubles as the os_log category, so
    // unified-log lines are filterable by both subsystem (the extension) and category. GetOrAdd caches,
    // so the handle is created once per category; creating it is cheap (a handle bound to subsystem/category).
    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName,
            name => new IosDeviceLogger(new OSLogHandle(subsystem, name), includeScopes, name));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }

    // Writes formatted log lines to os_log. The OSLog handle is a lightweight value (nothing to release),
    // so neither this logger nor the provider needs disposal logic for it.
    private sealed class IosDeviceLogger(OSLogHandle osLog, bool includeScopes, string? categoryName)
        : TextLogger(includeScopes, categoryName)
    {
        public override void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var text = FormatLog(logLevel, eventId, state, exception, formatter);
            try {
                // os_log requires a constant format string; the .NET iOS binding passes `text` as a
                // %{public}s argument, so the content is not redacted and needs no %-escaping.
                osLog.Log(ToOsLogLevel(logLevel), text);
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
