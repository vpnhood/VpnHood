using Microsoft.Extensions.Logging;

namespace VpnHood.Core.Toolkit.Logging;

public class VhDeviceLogger(bool includeScopes = true, string? categoryName = null)
    : TextLogger(includeScopes, categoryName)
{
    public override void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var text = FormatLog(logLevel, eventId, state, exception, formatter);

        // System.Diagnostics.Trace is thread-safe (serialized via the global lock), so no extra locking is needed.
        System.Diagnostics.Trace.WriteLine(text);
    }
}
