namespace VpnHood.Net.Toolkit.Logging;

public sealed class TraceLoggerProvider(bool includeScopes = true, bool includeTime = true)
    : TextLoggerProvider(new TraceLogger(), includeTime: includeTime, includeScopes: includeScopes);
